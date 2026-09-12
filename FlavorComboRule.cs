using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public enum ComboBonusType
{
    BonusPoints,
    BonusMult,
    BonusXMult,
    RetriggerMatchingAdditives, // re-runs the effect of every matching additive instance
}

/// <summary>
/// Data-driven synergy rule, e.g. "Citrus + Mint present anywhere in the cup -> +2 mult"
/// or "3 or more Sweet additives -> retrigger all Sweet additives once".
/// Create via Assets > Create > Soda > Combo Rule.
/// </summary>
[CreateAssetMenu(fileName = "New Combo Rule", menuName = "Soda/Combo Rule")]
public class FlavorComboRule : ScriptableObject
{
    [Header("Requirement")]
    [Tooltip("All of these flavors must be present somewhere in the cup for the combo to fire.")]
    public List<FlavorType> requiredFlavors = new List<FlavorType>();

    [Tooltip("If > 0, requires at least this many additives touching a required flavor " +
             "(e.g. 3 for 'three or more Sweet additives'). Leave at 0 to just require presence.")]
    public int minimumMatchingAdditives = 0;

    [Header("Reward")]
    public ComboBonusType bonusType = ComboBonusType.BonusMult;
    public float bonusAmount = 2f;
    public string comboName = "Combo";

    /// <summary>
    /// Modifiers can raise this at runtime (e.g. "Refreshing Combo now gives +1 more mult").
    /// Kept separate from bonusAmount so the base asset value is never touched.
    /// </summary>
    [System.NonSerialized] public float runtimeBonusAdd = 0f;

    /// <summary>
    /// Modifiers can raise this to make the combo fire additional times per soda
    /// (the "retrigger a synergy between families" syrup effect).
    /// </summary>
    [System.NonSerialized] public int runtimeExtraTriggers = 0;

    public float EffectiveBonusAmount => bonusAmount + runtimeBonusAdd;
    public int EffectiveTriggerCount => 1 + runtimeExtraTriggers;

    public bool Evaluate(List<AdditiveInstance> cup, out List<AdditiveInstance> matchingAdditives)
    {
        bool allFlavorsPresent = requiredFlavors.All(f => cup.Any(a => a.HasFlavor(f)));
        matchingAdditives = new List<AdditiveInstance>();
        if (!allFlavorsPresent) return false;

        var touching = cup.Where(a => requiredFlavors.Any(f => a.HasFlavor(f))).ToList();
        matchingAdditives = touching;

        if (minimumMatchingAdditives > 0)
            return touching.Count >= minimumMatchingAdditives;

        return true;
    }
}
