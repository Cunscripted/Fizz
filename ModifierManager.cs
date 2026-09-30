using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Per-flavor bonus applied to any CUP additive matching the flavor, evaluated
/// fresh every scoring pass. Kept as an ongoing rule (rather than baked into
/// AdditiveInstance fields once) so additives acquired AFTER the syrup is picked
/// still benefit from it.
/// </summary>
public class FlavorRule
{
    public FlavorType flavor;
    public float pointsBonus;
    public float multBonus;
    public int retriggerBonus;
}

/// <summary>
/// Global bonus scaling with how many additives matching ANY of `flavors` sit on
/// the BELT (not the cup) when scored. Added once per soda, not per cup additive.
/// </summary>
/// <summary>
/// Ongoing BoostAmountGrowth syrup: adds bonus to the per-use amount change of every additive
/// whose own change is non-zero - optionally only additives with a specific flavor.
/// </summary>
public class AmountGrowthRule
{
    public bool anyFlavor = true;
    public FlavorType flavor;
    public AmountGrowthMode mode = AmountGrowthMode.FlatAddition;
    public float bonus;    // FlatAddition
    public float flipRate; // FlipNegativeToPositive

    public bool Matches(AdditiveInstance a) => anyFlavor || (a != null && a.HasFlavor(flavor));
}

public class FlavorScalingBonus
{
    public List<FlavorType> flavors;
    public bool isMult;
    public float amountPerCount;
}

/// <summary>
/// What ApplyModifier actually did, for a presentation layer (e.g. SyrupApplyPopup)
/// to react to - which additive (if any) was gained or lost as part of this syrup.
/// </summary>
public struct ModifierApplyResult
{
    public AdditiveData addedAdditive;
    public AdditiveData removedAdditive;
    /// <summary>Extra cup slots this syrup granted (AddCupCapacity), 0 otherwise.</summary>
    public int cupCapacityAdded;
}

/// <summary>
/// Offers and applies ModifierData ("syrup") choices, made every 5th round.
/// Runtime effects are written to FlavorComboRule's runtime fields, this
/// manager's own ongoing rule lists, or (for AddAttempt) surfaced as an event -
/// never to a ScriptableObject's serialized fields, which would leak into the
/// editor asset permanently.
/// </summary>
public class ModifierManager : MonoBehaviour
{
    public List<ModifierData> modifierPool = new List<ModifierData>();
    public int offerCount = 3;

    [Header("Base weights (before skip-streak skew)")]
    public float commonWeight = 100f;
    public float uncommonWeight = 40f;
    public float rareWeight = 12f;
    public float legendaryWeight = 3f;
    [Tooltip("How hard EACH consecutive shop/modifier skip (RoundManager.SkipStreak) pulls odds toward " +
             "rarer tiers. 0 = no effect - skipping does nothing special, offers stay a uniform random draw.")]
    public float skipSkewPerStack = 0.75f;

    public float GlobalMultBonus { get; private set; } = 0f;

    /// <summary>The ModifierManager in the scene (for UI like the hover panel).</summary>
    public static ModifierManager Instance { get; private set; }

    private readonly List<AmountGrowthRule> _amountGrowthRules = new List<AmountGrowthRule>();
    public IReadOnlyList<AmountGrowthRule> AmountGrowthRules => _amountGrowthRules;

    /// <summary>Total Flat Addition bonus for this additive (flip rules not included).</summary>
    public float GetAmountGrowthBonus(AdditiveInstance additive)
    {
        if (additive == null) return 0f;
        float total = 0f;
        foreach (var rule in _amountGrowthRules)
            if (rule.mode == AmountGrowthMode.FlatAddition && rule.Matches(additive)) total += rule.bonus;
        return total;
    }

    /// <summary>
    /// Applies every BoostAmountGrowth syrup to an additive's per-use amount change. Zero stays zero.
    /// Order: first a negative change is flipped positive (if any flip syrup matches - the best rate
    /// wins, they don't stack), then all Flat Addition bonuses are added.
    /// </summary>
    public float ModifyAmountChange(AdditiveInstance additive, float change)
    {
        if (additive == null || change == 0f) return change;

        if (change < 0f)
        {
            float bestRate = -1f;
            foreach (var rule in _amountGrowthRules)
                if (rule.mode == AmountGrowthMode.FlipNegativeToPositive && rule.Matches(additive))
                    bestRate = Mathf.Max(bestRate, rule.flipRate);
            if (bestRate >= 0f) change = -change * bestRate;
        }

        return change + GetAmountGrowthBonus(additive);
    }

    private void Awake()
    {
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }
    public IReadOnlyList<ModifierData> ActiveModifiers => _active;
    public IReadOnlyList<FlavorRule> FlavorRules => _flavorRules;
    public IReadOnlyList<FlavorScalingBonus> FlavorScalingBonuses => _flavorScaling;

    /// <summary>Fired for AddAttempt syrups; RoundManager subscribes to grow its attempt pool.</summary>
    public event Action<int> OnAttemptBonusGranted;

    /// <summary>Fired for AddBeltSize syrups; RoundManager subscribes to grow how many cards it draws each round.</summary>
    public event Action<int> OnBeltSizeBonusGranted;

    /// <summary>Fired for AddCupCapacity syrups; RoundManager subscribes to grow the soda bottle's capacity.</summary>
    public event Action<int> OnCupCapacityBonusGranted;

    private readonly List<ModifierData> _active = new List<ModifierData>();
    private readonly List<FlavorRule> _flavorRules = new List<FlavorRule>();
    private readonly List<FlavorScalingBonus> _flavorScaling = new List<FlavorScalingBonus>();

    /// <summary>
    /// skipStreak (see RoundManager.SkipStreak) skews odds toward rarer-tier modifiers
    /// the longer the player has been skipping shop/modifier offers without picking -
    /// same weighted-by-rarity mechanism ShopManager uses for additive offers, via the
    /// shared RarityWeightedPicker.
    /// </summary>
    public List<ModifierData> GenerateOffers(int skipStreak = 0)
    {
        float skew = 1f + Mathf.Max(0, skipStreak) * skipSkewPerStack;
        var weights = new RarityWeightedPicker.Weights
        {
            common = commonWeight,
            uncommon = uncommonWeight,
            rare = rareWeight,
            legendary = legendaryWeight
        };

        return RarityWeightedPicker.PickMany(modifierPool, m => m.rarity, weights, skew, offerCount);
    }

    public ModifierApplyResult ApplyModifier(ModifierData modifier, List<AdditiveInstance> ownedAdditives, List<AdditiveData> allAdditivesPool = null)
    {
        var result = new ModifierApplyResult();
        _active.Add(modifier);

        switch (modifier.effectType)
        {
            case ModifierEffectType.BoostComboRule:
                if (modifier.targetComboRule != null)
                    modifier.targetComboRule.runtimeBonusAdd += modifier.amount;
                break;

            case ModifierEffectType.RetriggerComboRule:
                if (modifier.targetComboRule != null)
                    modifier.targetComboRule.runtimeExtraTriggers += Mathf.Max(1, Mathf.RoundToInt(modifier.amount));
                break;

            case ModifierEffectType.GlobalMult:
                GlobalMultBonus += modifier.amount;
                break;

            case ModifierEffectType.RetriggerForFlavor:
                _flavorRules.Add(new FlavorRule
                {
                    flavor = modifier.targetFlavor,
                    retriggerBonus = Mathf.Max(1, Mathf.RoundToInt(modifier.amount))
                });
                break;

            case ModifierEffectType.BoostFlavorPoints:
                _flavorRules.Add(new FlavorRule { flavor = modifier.targetFlavor, pointsBonus = modifier.amount });
                break;

            case ModifierEffectType.BoostFlavorMult:
                _flavorRules.Add(new FlavorRule { flavor = modifier.targetFlavor, multBonus = modifier.amount });
                break;

            case ModifierEffectType.ScalePerFlavorOnBelt:
                _flavorScaling.Add(new FlavorScalingBonus
                {
                    flavors = modifier.scalingFlavors,
                    isMult = modifier.scaleIsMult,
                    amountPerCount = modifier.amount
                });
                break;

            case ModifierEffectType.AddAdditiveToDeck:
                if (modifier.additiveToAdd != null)
                {
                    ownedAdditives.Add(new AdditiveInstance(modifier.additiveToAdd));
                    result.addedAdditive = modifier.additiveToAdd;
                }
                break;

            case ModifierEffectType.AddRandomAdditiveToDeck:
            {
                var pick = RandomAdditivePicker.Pick(modifier.randomAdditivePool, modifier.filterByFlavor,
                                                      modifier.targetFlavor, allAdditivesPool);
                if (pick != null)
                {
                    ownedAdditives.Add(new AdditiveInstance(pick));
                    result.addedAdditive = pick;
                }
                break;
            }

            case ModifierEffectType.RemoveAdditiveFromDeck:
                if (modifier.additiveToRemove != null)
                {
                    var match = ownedAdditives.FirstOrDefault(a => a.template == modifier.additiveToRemove);
                    if (match != null)
                    {
                        ownedAdditives.Remove(match);
                        result.removedAdditive = modifier.additiveToRemove;
                    }
                }
                break;

            case ModifierEffectType.RemoveRandomAdditiveFromDeck:
            {
                var candidates = modifier.filterByFlavor
                    ? ownedAdditives.Where(a => a.HasFlavor(modifier.targetFlavor)).ToList()
                    : new List<AdditiveInstance>(ownedAdditives);
                if (candidates.Count > 0)
                {
                    var pick = candidates[UnityEngine.Random.Range(0, candidates.Count)];
                    ownedAdditives.Remove(pick);
                    result.removedAdditive = pick.template;
                }
                break;
            }

            case ModifierEffectType.AddAttempt:
                OnAttemptBonusGranted?.Invoke(Mathf.Max(1, modifier.attemptBonus));
                break;

            case ModifierEffectType.AddBeltSize:
                OnBeltSizeBonusGranted?.Invoke(Mathf.Max(1, modifier.beltSizeBonus));
                break;

            case ModifierEffectType.BoostAmountGrowth:
                _amountGrowthRules.Add(new AmountGrowthRule
                {
                    anyFlavor = !modifier.filterByFlavor,
                    flavor = modifier.targetFlavor,
                    mode = modifier.amountGrowthMode,
                    bonus = modifier.amount,
                    flipRate = Mathf.Max(0f, modifier.flipRate)
                });
                break;

            case ModifierEffectType.ReduceHeatPercent:
            {
                var pm = PopularityManager.Instance;
                if (pm == null) break;
                float fraction = Mathf.Clamp01(modifier.amount);
                pm.ReduceAllHeat(fraction);
                if (modifier.repeatEveryRound) pm.AddRoundEndCoolingPercent(fraction);
                break;
            }

            case ModifierEffectType.AddCupCapacity:
            {
                int bonus = Mathf.Max(1, modifier.cupCapacityBonus);
                OnCupCapacityBonusGranted?.Invoke(bonus);
                result.cupCapacityAdded = bonus;
                break;
            }
        }

        return result;
    }
}