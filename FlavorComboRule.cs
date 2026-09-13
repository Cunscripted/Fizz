using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public enum ComboBonusType
{
    BonusPoints,
    BonusMult,
    BonusXMult,
    RetriggerMatchingAdditives, // re-runs the effect of every matching additive instance
    AddAdditiveToDeck,          // adds an additive to the owned collection - specific (additiveToAdd) or random (see RandomAdditivePicker fields below)
}

/// <summary>
/// Data-driven synergy rule, e.g. "Citrus + Mint present anywhere in the cup -> +2 mult",
/// "3 or more Sweet additives -> retrigger all Sweet additives once", or "Citrus Zest AND
/// Mint Kick specifically both present -> +5 points". Create via Assets > Create > Soda >
/// Combo Rule.
///
/// requiredFlavors and requiredAdditives are independent requirements that both apply if
/// both are set (AND between the two categories) - so a rule can be flavor-only,
/// specific-additive-only, or a mix of both. At least one of the two must have entries,
/// or the rule can never fire.
/// </summary>
[CreateAssetMenu(fileName = "New Combo Rule", menuName = "Soda/Combo Rule")]
public class FlavorComboRule : ScriptableObject
{
    [Header("Requirement")]
    [Tooltip("All of these flavors must be present somewhere in the cup for the combo to fire.")]
    public List<FlavorType> requiredFlavors = new List<FlavorType>();

    [Tooltip("All of these SPECIFIC additives must be present somewhere in the cup for the combo to fire - " +
             "independent of (and combinable with) the flavor requirement above. E.g. require 'Citrus Zest' " +
             "and 'Mint Kick' by name, regardless of what flavors they happen to have.")]
    public List<AdditiveData> requiredAdditives = new List<AdditiveData>();

    [Tooltip("If > 0, requires at least this many additives touching a required flavor " +
             "(e.g. 3 for 'three or more Sweet additives'). Leave at 0 to just require presence. " +
             "Only checks flavor-matching additives, not requiredAdditives matches.")]
    public int minimumMatchingAdditives = 0;

    [Tooltip("Optional - overrides the auto-generated requirement text (e.g. \"Sour + Fizzy (x3+)\") shown " +
             "in the active-combo panel and elsewhere, with whatever you type here instead - e.g. \"Three " +
             "fizzy drinks in a row\". Doesn't change what actually has to be in the cup for the combo to " +
             "fire (that's still requiredFlavors/requiredAdditives/minimumMatchingAdditives above), just how " +
             "it's described. Leave blank to auto-generate as before.")]
    [TextArea]
    public string customRequirementDescription = "";

    [Header("Reward")]
    public ComboBonusType bonusType = ComboBonusType.BonusMult;
    public float bonusAmount = 2f;
    public string comboName = "Combo";

    [Header("Add Additive To Deck (AddAdditiveToDeck bonus type)")]
    [Tooltip("How many additives to add each time this fires. If Create Randomly is on, each one is picked " +
             "independently (so duplicates are possible). If off, this many copies of additiveToAdd are added.")]
    public int additiveAddCount = 1;
    [Tooltip("If true, the created additive is picked randomly (see the fields below). If false, additiveToAdd is used directly.")]
    public bool createRandomAdditive = false;
    [Tooltip("Used when Create Random Additive is off.")]
    public AdditiveData additiveToAdd;
    [Tooltip("Used when Create Random Additive is on. Leave empty if you only want to filter by flavor below - " +
             "in that case it searches every additive obtainable in the game instead of a curated list.")]
    public List<AdditiveData> randomAdditivePool = new List<AdditiveData>();
    public bool filterByFlavor = false;
    public FlavorType randomFlavorFilter;
    [Tooltip("If true, this combo only creates an additive the first time it fires this run, not every time " +
             "it triggers. If false, it creates one every single time the combo fires - which can snowball " +
             "fast for an easy-to-trigger combo, so consider whether that's intended.")]
    public bool addOnlyOnce = false;

    /// <summary>Guards addOnlyOnce - not serialized, resets each play session same as the asset's other runtime fields.</summary>
    [System.NonSerialized] public bool hasAddedToDeck = false;

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

    /// <summary>Human-readable summary for UI (e.g. "Sour + Fizzy: +2 Mult"), used by the active-combo panel.</summary>
    public string FormatEffectDescription()
    {
        string requirement;
        if (!string.IsNullOrWhiteSpace(customRequirementDescription))
        {
            // Custom text stands entirely on its own - no auto count note appended, since
            // the author writing it presumably already accounted for whatever they want said.
            requirement = customRequirementDescription;
        }
        else
        {
            var parts = new List<string>();
            if (requiredFlavors != null && requiredFlavors.Count > 0)
                parts.Add(string.Join(" + ", requiredFlavors));
            if (requiredAdditives != null && requiredAdditives.Count > 0)
                parts.Add(string.Join(" + ", requiredAdditives.Where(a => a != null).Select(a => a.additiveName)));

            requirement = parts.Count > 0 ? string.Join(" & ", parts) : "(no requirement set)";
            if (minimumMatchingAdditives > 0) requirement += $" (x{minimumMatchingAdditives}+)";
        }

        string bonus = bonusType switch
        {
            ComboBonusType.BonusPoints => $"{ScoreFormat.Signed(EffectiveBonusAmount)} Points",
            ComboBonusType.BonusMult => $"{ScoreFormat.Signed(EffectiveBonusAmount)} Mult",
            ComboBonusType.BonusXMult => $"x{EffectiveBonusAmount:0.#} Mult",
            ComboBonusType.RetriggerMatchingAdditives => "Retriggers matching additives",
            ComboBonusType.AddAdditiveToDeck => createRandomAdditive
                ? $"Adds {(additiveAddCount > 1 ? $"{additiveAddCount} random additives" : "a random additive")} to your deck"
                : (additiveToAdd != null
                    ? $"Adds '{additiveToAdd.additiveName}'{(additiveAddCount > 1 ? $" x{additiveAddCount}" : "")} to your deck"
                    : "Adds an additive to your deck"),
            _ => ""
        };
        return $"{requirement}: {bonus}";
    }

    /// <summary>
    /// True if the cup satisfies BOTH the flavor requirement (if any) and the specific
    /// additive requirement (if any). matchingAdditives is the union of flavor-touching
    /// cup additives and the specific requiredAdditives matches - used for
    /// RetriggerMatchingAdditives and for display in the active-combo panel.
    /// </summary>
    public bool Evaluate(List<AdditiveInstance> cup, out List<AdditiveInstance> matchingAdditives)
    {
        return Evaluate(cup, out matchingAdditives, out _);
    }

    /// <summary>Same as Evaluate(cup, out matchingAdditives), but also explains WHY it returned false via failReason (null when it returns true).</summary>
    public bool Evaluate(List<AdditiveInstance> cup, out List<AdditiveInstance> matchingAdditives, out string failReason)
    {
        matchingAdditives = new List<AdditiveInstance>();
        failReason = null;

        bool hasFlavorReq = requiredFlavors != null && requiredFlavors.Count > 0;
        bool hasAdditiveReq = requiredAdditives != null && requiredAdditives.Count > 0;
        if (!hasFlavorReq && !hasAdditiveReq)
        {
            failReason = "nothing configured - both Required Flavors and Required Additives are empty";
            return false;
        }

        if (hasFlavorReq)
        {
            foreach (var f in requiredFlavors)
            {
                if (!cup.Any(a => a.HasFlavor(f)))
                {
                    failReason = $"missing flavor '{f}' somewhere in the cup";
                    return false;
                }
            }
        }

        var specificMatches = new List<AdditiveInstance>();
        if (hasAdditiveReq)
        {
            foreach (var required in requiredAdditives)
            {
                if (required == null)
                {
                    failReason = "one of the Required Additives entries is unassigned (a null slot in the list)";
                    return false;
                }
                var match = cup.FirstOrDefault(a => a.template == required);
                if (match == null)
                {
                    failReason = $"missing specific additive '{required.additiveName}' in the cup";
                    return false;
                }
                specificMatches.Add(match);
            }
        }

        var flavorTouching = hasFlavorReq
            ? cup.Where(a => requiredFlavors.Any(f => a.HasFlavor(f))).ToList()
            : new List<AdditiveInstance>();

        matchingAdditives = flavorTouching.Union(specificMatches).ToList();

        if (minimumMatchingAdditives > 0 && flavorTouching.Count < minimumMatchingAdditives)
        {
            failReason = $"only {flavorTouching.Count} flavor-matching additive(s) in the cup, needs at least {minimumMatchingAdditives}";
            return false;
        }

        return true;
    }
}