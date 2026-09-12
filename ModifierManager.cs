using System;
using System.Collections.Generic;
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
/// Global bonus scaling with how many of a flavor sit on the BELT (not the cup)
/// when scored. Added once per soda, not per cup additive.
/// </summary>
public class FlavorScalingBonus
{
    public FlavorType flavor;
    public bool isMult;
    public float amountPerCount;
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

    private readonly List<ModifierData> _active = new List<ModifierData>();
    private readonly List<FlavorRule> _flavorRules = new List<FlavorRule>();
    private readonly List<FlavorScalingBonus> _flavorScaling = new List<FlavorScalingBonus>();

    public List<ModifierData> GenerateOffers()
    {
        var shuffled = new List<ModifierData>(modifierPool);
        for (int i = shuffled.Count - 1; i > 0; i--)
        {
            int j = UnityEngine.Random.Range(0, i + 1);
            (shuffled[i], shuffled[j]) = (shuffled[j], shuffled[i]);
        }
        return shuffled.GetRange(0, Mathf.Min(offerCount, shuffled.Count));
    }

    public void ApplyModifier(ModifierData modifier, List<AdditiveInstance> ownedAdditives)
    {
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
                    flavor = modifier.targetFlavor,
                    isMult = modifier.scaleIsMult,
                    amountPerCount = modifier.amount
                });
                break;

            case ModifierEffectType.AddAdditiveToDeck:
                if (modifier.additiveToAdd != null)
                    ownedAdditives.Add(new AdditiveInstance(modifier.additiveToAdd));
                break;

            case ModifierEffectType.AddAttempt:
                OnAttemptBonusGranted?.Invoke(Mathf.Max(1, modifier.attemptBonus));
                break;
        }
    }
}
