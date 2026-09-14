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

    public float GlobalMultBonus { get; private set; } = 0f;
    public IReadOnlyList<ModifierData> ActiveModifiers => _active;
    public IReadOnlyList<FlavorRule> FlavorRules => _flavorRules;
    public IReadOnlyList<FlavorScalingBonus> FlavorScalingBonuses => _flavorScaling;

    /// <summary>Fired for AddAttempt syrups; RoundManager subscribes to grow its attempt pool.</summary>
    public event Action<int> OnAttemptBonusGranted;

    /// <summary>Fired for AddBeltSize syrups; RoundManager subscribes to grow how many cards it draws each round.</summary>
    public event Action<int> OnBeltSizeBonusGranted;

    private readonly List<ModifierData> _active = new List<ModifierData>();
    private readonly List<FlavorRule> _flavorRules = new List<FlavorRule>();
    private readonly List<FlavorScalingBonus> _flavorScaling = new List<FlavorScalingBonus>();

    public List<ModifierData> GenerateOffers()
    {
        // Filter out empty/unassigned slots in modifierPool before shuffling - an
        // Inspector list with a gap in it would otherwise hand a null straight to
        // whatever UI spawns offer entries.
        var shuffled = modifierPool.Where(m => m != null).ToList();
        for (int i = shuffled.Count - 1; i > 0; i--)
        {
            int j = UnityEngine.Random.Range(0, i + 1);
            (shuffled[i], shuffled[j]) = (shuffled[j], shuffled[i]);
        }
        return shuffled.GetRange(0, Mathf.Min(offerCount, shuffled.Count));
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
        }

        return result;
    }
}