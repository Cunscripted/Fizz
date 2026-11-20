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
    ReduceHeatPercent,          // cuts every flavor's popularity heat by `amount` as a fraction (0.25 = 25%, 1 = reset)

    // --- Everything a syrup (ModifierData) can do, as a combo reward ---
    // NOTE: new entries MUST go at the END - Unity serializes enums as integers, so inserting one
    // in the middle would silently change the effect type of every existing combo asset.
    BoostComboRule,             // permanently raises a combo's first effect by `amount` (targetComboRule, or this combo if empty)
    RetriggerComboRule,         // permanently makes a combo (targetComboRule, or this one) fire `amount` more times per soda
    GlobalMult,                 // permanently adds `amount` mult to every soda for the rest of the run
    RetriggerForFlavor,         // cup additives of targetFlavor fire `amount` more times (this soda, or every soda - see duration)
    BoostFlavorPoints,          // cup additives of targetFlavor give `amount` bonus points (this soda, or every soda)
    BoostFlavorMult,            // cup additives of targetFlavor give `amount` bonus mult (this soda, or every soda)
    ScalePerFlavorOnBelt,       // `amount` points/mult per belt additive matching scalingFlavors (this soda, or every soda)
    RemoveAdditiveFromDeck,     // removes `amount` owned additive(s) matching additiveToRemove
    RemoveRandomAdditiveFromDeck, // removes `amount` random owned additive(s), optionally of targetFlavor only
    AddAttempt,                 // permanently grants `amount` extra attempts per round (applies to the current round too)
    AddBeltSize,                // permanently draws `amount` more additives onto the belt each round
    AddCupCapacity,             // permanently lets the bottle hold `amount` more additives
    BoostAmountGrowth,          // permanently changes every additive's per-use amount change (like the syrup)
}

/// <summary>How long a combo's flavor/belt bonus lasts once it fires.</summary>
public enum ComboEffectDuration
{
    ThisSoda,  // applied immediately to the soda being scored, then gone
    RestOfRun  // becomes a permanent rule for every soda after this one, exactly like the matching syrup
}

/// <summary>
/// One reward a combo gives when it triggers. A combo can have any number of these.
///   BonusPoints / BonusMult : amount = points / mult added
///   BonusXMult              : amount = the multiplier (e.g. 1.5)
///   RetriggerMatchingAdditives : amount = how many times the matching additives fire again (min 1)
///   AddAdditiveToDeck       : uses the rule's Add Additive fields; amount above 1 adds extra copies
///   ReduceHeatPercent       : amount = fraction of every flavor's popularity heat removed (0.25 = 25%, 1 = reset)
///   The syrup-style effects (BoostComboRule onward) use the same meaning of amount as the matching
///   ModifierEffectType, plus the target fields below that apply to their type.
/// amountPerLevel is added to amount for every level above 1 (see FlavorComboRule leveling).
/// </summary>
[System.Serializable]
public class ComboEffect
{
    public ComboBonusType type = ComboBonusType.BonusMult;
    public float amount = 2f;
    [Tooltip("Added to Amount for every level this combo has above level 1. E.g. amount 2, +1 per level = 2, 3, 4...")]
    public float amountPerLevel = 0f;

    [Tooltip("If on, this effect only does anything the FIRST time the combo fires each run. Recommended for " +
             "permanent effects (attempts, belt size, cup capacity, global mult...) on an easy-to-trigger combo, " +
             "since otherwise they stack every single brew.")]
    public bool onlyOncePerRun = false;

    [Tooltip("Retrigger For Flavor / Boost Flavor Points / Boost Flavor Mult / Scale Per Flavor On Belt: " +
             "This Soda = applies right now to the soda being scored. Rest Of Run = becomes a permanent rule " +
             "for every LATER soda, exactly like the syrup version.")]
    public ComboEffectDuration duration = ComboEffectDuration.ThisSoda;

    [Tooltip("Boost Combo Rule / Retrigger Combo Rule: which combo to upgrade. Leave empty to upgrade THIS combo.")]
    public FlavorComboRule targetComboRule;

    [Tooltip("Retrigger For Flavor / Boost Flavor Points / Boost Flavor Mult: the flavor affected. " +
             "Remove Random Additive / Boost Amount Growth: the flavor filter (when Filter By Flavor is on).")]
    public FlavorType targetFlavor;

    [Tooltip("Remove Random Additive From Deck / Boost Amount Growth: only affect additives with Target Flavor.")]
    public bool filterByFlavor = false;

    [Tooltip("Scale Per Flavor On Belt: belt additives matching ANY of these are counted (a card with two listed " +
             "flavors counts twice, same as the syrup).")]
    public List<FlavorType> scalingFlavors = new List<FlavorType>();
    [Tooltip("Scale Per Flavor On Belt: give mult instead of points.")]
    public bool scaleIsMult = false;

    [Tooltip("Remove Additive From Deck: removes owned additive(s) made from this template.")]
    public AdditiveData additiveToRemove;

    [Tooltip("Boost Amount Growth: Flat Addition adds Amount to every non-zero per-use change. Flip Negative To " +
             "Positive makes shrinking cards grow instead, at Flip Rate.")]
    public AmountGrowthMode amountGrowthMode = AmountGrowthMode.FlatAddition;
    [Tooltip("Boost Amount Growth (Flip Negative To Positive only): how much of a negative change comes back as growth. 0.5 = half.")]
    [Min(0f)] public float flipRate = 0.5f;

    [Tooltip("Reduce Heat Percent: also cut heat by the same fraction at the end of EVERY round for the rest of the run.")]
    public bool repeatEveryRound = false;

    /// <summary>True for effects whose Duration setting matters.</summary>
    public static bool HasDuration(ComboBonusType t) =>
        t == ComboBonusType.RetriggerForFlavor || t == ComboBonusType.BoostFlavorPoints ||
        t == ComboBonusType.BoostFlavorMult || t == ComboBonusType.ScalePerFlavorOnBelt;

    /// <summary>True for effects that permanently change the run when they fire (worth flagging "Only Once Per Run").</summary>
    public bool IsPermanent =>
        type == ComboBonusType.BoostComboRule || type == ComboBonusType.RetriggerComboRule ||
        type == ComboBonusType.GlobalMult || type == ComboBonusType.AddAttempt ||
        type == ComboBonusType.AddBeltSize || type == ComboBonusType.AddCupCapacity ||
        type == ComboBonusType.BoostAmountGrowth ||
        (HasDuration(type) && duration == ComboEffectDuration.RestOfRun);
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

    public string comboName = "Combo";

    [Header("Rewards (stack as many as you like)")]
    [Tooltip("Every effect in this list fires each time the combo triggers, top to bottom - e.g. +20 Points AND " +
             "+3 Mult AND retrigger the matching additives. Each effect can grow per combo level via Amount Per Level.")]
    public List<ComboEffect> effects = new List<ComboEffect>();

    [Header("Leveling")]
    [Tooltip("If on, this combo levels up after being used enough times, making every effect stronger by its Amount Per Level.")]
    public bool canLevelUp = true;
    [Tooltip("Brews this combo must trigger in to go from level 1 to level 2.")]
    [Min(1)] public int usesForFirstLevelUp = 3;
    [Tooltip("Extra uses added to the requirement after EACH level up. E.g. 3 first + 2 per level = 3, 5, 7, 9...")]
    [Min(0)] public int extraUsesPerLevel = 2;
    [Tooltip("Multiplies the requirement after each level up (applied before Extra Uses Per Level). 1 = no multiplying. " +
             "E.g. 3 first x1.5 = 3, 5, 7, 10...")]
    [Min(1f)] public float usesGrowthMultiplier = 1f;
    [Tooltip("Highest level this combo can reach. 0 = no cap.")]
    [Min(0)] public int maxLevel = 0;

    [Header("Add Additive To Deck (used by any AddAdditiveToDeck effect above)")]
    [Tooltip("How many additives to add each time this fires. If Create Randomly is on, each one is picked " +
             "independently (so duplicates are possible). If off, this many copies of additiveToAdd are added. " +
             "An AddAdditiveToDeck effect's Amount Per Level adds more on top at higher levels.")]
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
    [Tooltip("If true, every additive currently satisfying this combo's requirement (matchingAdditives) is " +
             "permanently removed from the collection when this combo fires - combined with the creation " +
             "above, this turns the combo into a crafting/fusion effect: the matched additives are consumed " +
             "and replaced by whatever gets created. Removal happens once per scoring pass (not once per " +
             "trigger - a card can't be consumed twice just because a retrigger-combo syrup fires this rule " +
             "more than once), and only if at least one additive was actually successfully created this fire, " +
             "so a misconfigured creation (e.g. additiveToAdd left unassigned) doesn't destroy the player's " +
             "cards for nothing.")]
    public bool consumeMatchedAdditives = false;

    // ---------- Legacy single reward (pre-1.5.2) ----------
    // Combos used to have exactly one bonusType + bonusAmount. On load, those are copied into
    // `effects` once (see OnEnable) so existing combo assets keep working unchanged.
    [HideInInspector] public ComboBonusType bonusType = ComboBonusType.BonusMult;
    [HideInInspector] public float bonusAmount = 2f;
    [HideInInspector, SerializeField] private bool legacyRewardMigrated = false;

    /// <summary>Guards addOnlyOnce - not serialized. Reset at scene load by SodaScoringManager.</summary>
    [System.NonSerialized] public bool hasAddedToDeck = false;

    /// <summary>
    /// Modifiers can raise this at runtime (e.g. "Refreshing Combo now gives +1 more mult").
    /// Added to the FIRST effect in the list. Kept separate so the asset value is never touched.
    /// </summary>
    [System.NonSerialized] public float runtimeBonusAdd = 0f;

    /// <summary>
    /// Modifiers can raise this to make the combo fire additional times per soda
    /// (the "retrigger a synergy between families" syrup effect).
    /// </summary>
    [System.NonSerialized] public int runtimeExtraTriggers = 0;

    public int EffectiveTriggerCount => 1 + runtimeExtraTriggers;

    /// <summary>Indices of effects with onlyOncePerRun that have already fired this run. Not serialized.</summary>
    [System.NonSerialized] private HashSet<int> _effectsFiredOnce;

    /// <summary>For onlyOncePerRun effects: true if this effect index has already fired this run.</summary>
    public bool HasEffectFiredOnce(int effectIndex) => _effectsFiredOnce != null && _effectsFiredOnce.Contains(effectIndex);

    public void MarkEffectFiredOnce(int effectIndex)
    {
        if (_effectsFiredOnce == null) _effectsFiredOnce = new HashSet<int>();
        _effectsFiredOnce.Add(effectIndex);
    }

    /// <summary>Clears everything modifiers/scoring changed at runtime. SodaScoringManager calls this when the gameplay scene loads, so a retry starts clean.</summary>
    public void ResetRuntimeState()
    {
        hasAddedToDeck = false;
        runtimeBonusAdd = 0f;
        runtimeExtraTriggers = 0;
        _effectsFiredOnce?.Clear();
    }

    private void OnEnable()
    {
        MigrateLegacyReward();
    }

    private void MigrateLegacyReward()
    {
        if (legacyRewardMigrated) return;
        legacyRewardMigrated = true;
        if (effects == null) effects = new List<ComboEffect>();
        if (effects.Count > 0) return;

        // The old RetriggerMatchingAdditives ignored bonusAmount and retriggered once - keep that behavior.
        float amount = bonusType == ComboBonusType.RetriggerMatchingAdditives ? 1f : bonusAmount;
        effects.Add(new ComboEffect { type = bonusType, amount = amount, amountPerLevel = 0f });
    }

    // ---------- Levels ----------

    /// <summary>How many uses it takes to go from `level` to level + 1.</summary>
    public int UsesRequiredForLevel(int level)
    {
        int steps = Mathf.Max(0, level - 1);
        float required = usesForFirstLevelUp * Mathf.Pow(usesGrowthMultiplier, steps) + extraUsesPerLevel * steps;
        return Mathf.Max(1, Mathf.RoundToInt(required));
    }

    public bool IsMaxLevel(int level) => !canLevelUp || (maxLevel > 0 && level >= maxLevel);

    /// <summary>An effect's amount at the given level (plus any syrup boost, on the first effect).</summary>
    public float GetEffectAmount(int effectIndex, int level)
    {
        if (effects == null || effectIndex < 0 || effectIndex >= effects.Count) return 0f;
        var e = effects[effectIndex];
        float amount = e.amount + e.amountPerLevel * Mathf.Max(0, level - 1);
        if (effectIndex == 0) amount += runtimeBonusAdd;
        return amount;
    }

    /// <summary>Human-readable summary for UI (e.g. "Sour + Fizzy: +2 Mult, x1.5 Mult"), used by the active-combo panel.</summary>
    public string FormatEffectDescription(int level = 1)
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

        var bonuses = new List<string>();
        for (int i = 0; i < (effects?.Count ?? 0); i++)
        {
            if (effects[i] == null) continue;
            string text = DescribeEffect(effects[i], GetEffectAmount(i, level));
            if (!string.IsNullOrEmpty(text) && effects[i].onlyOncePerRun) text += " (once per run)";
            if (!string.IsNullOrEmpty(text)) bonuses.Add(text);
        }

        return $"{requirement}: {(bonuses.Count > 0 ? string.Join(", ", bonuses) : "(no rewards set)")}";
    }

    /// <summary>One effect's UI text at a given amount, e.g. "+3 Mult" or "Sour additives +5 Points for the rest of the run".</summary>
    private string DescribeEffect(ComboEffect e, float amount)
    {
        int whole = WholeCount(amount);
        string forever = e.duration == ComboEffectDuration.RestOfRun ? " for the rest of the run" : "";
        string comboName = e.targetComboRule != null && e.targetComboRule != this ? $"'{e.targetComboRule.comboName}'" : "this combo";
        switch (e.type)
        {
            case ComboBonusType.BonusPoints: return $"{ScoreFormat.Signed(amount)} Points";
            case ComboBonusType.BonusMult: return $"{ScoreFormat.Signed(amount)} Mult";
            case ComboBonusType.BonusXMult: return $"x{amount:0.##} Mult";
            case ComboBonusType.RetriggerMatchingAdditives:
                return RetriggerCount(amount) > 1 ? $"Retriggers matching additives x{RetriggerCount(amount)}" : "Retriggers matching additives";
            case ComboBonusType.AddAdditiveToDeck: return DescribeAddToDeck(AddCount(amount));
            case ComboBonusType.ReduceHeatPercent:
            {
                string heat = amount >= 0.999f ? "Resets all flavor heat" : $"Cools all flavors by {Mathf.Clamp01(amount) * 100f:0}%";
                return e.repeatEveryRound ? heat + " (and every round after)" : heat;
            }
            case ComboBonusType.BoostComboRule: return $"Permanently boosts {comboName} by {ScoreFormat.Signed(amount)}";
            case ComboBonusType.RetriggerComboRule: return $"{comboName} permanently fires {whole} more time{(whole > 1 ? "s" : "")}";
            case ComboBonusType.GlobalMult: return $"{ScoreFormat.Signed(amount)} Mult on every soda for the rest of the run";
            case ComboBonusType.RetriggerForFlavor:
                return $"Retriggers {e.targetFlavor} additives{(whole > 1 ? $" x{whole}" : "")}{forever}";
            case ComboBonusType.BoostFlavorPoints: return $"{e.targetFlavor} additives {ScoreFormat.Signed(amount)} Points{forever}";
            case ComboBonusType.BoostFlavorMult: return $"{e.targetFlavor} additives {ScoreFormat.Signed(amount)} Mult{forever}";
            case ComboBonusType.ScalePerFlavorOnBelt:
            {
                string flavors = e.scalingFlavors != null && e.scalingFlavors.Count > 0 ? string.Join("/", e.scalingFlavors) : "(no flavor)";
                return $"{ScoreFormat.Signed(amount)} {(e.scaleIsMult ? "Mult" : "Points")} per {flavors} additive on the belt{forever}";
            }
            case ComboBonusType.RemoveAdditiveFromDeck:
                return e.additiveToRemove != null
                    ? $"Removes '{e.additiveToRemove.additiveName}'{(whole > 1 ? $" x{whole}" : "")} from your deck"
                    : "Removes an additive from your deck";
            case ComboBonusType.RemoveRandomAdditiveFromDeck:
                return $"Removes {(whole > 1 ? $"{whole} random" : "a random")}{(e.filterByFlavor ? $" {e.targetFlavor}" : "")} additive{(whole > 1 ? "s" : "")} from your deck";
            case ComboBonusType.AddAttempt: return $"+{whole} attempt{(whole > 1 ? "s" : "")} per round";
            case ComboBonusType.AddBeltSize: return $"Belt +{whole}";
            case ComboBonusType.AddCupCapacity: return $"Cup +{whole}";
            case ComboBonusType.BoostAmountGrowth:
            {
                string who = e.filterByFlavor ? $"{e.targetFlavor} additives" : "additives";
                return e.amountGrowthMode == AmountGrowthMode.FlipNegativeToPositive
                    ? $"Shrinking {who} grow instead (x{e.flipRate:0.##})"
                    : $"Growing/shrinking {who} change {ScoreFormat.Signed(amount)} more per use";
            }
            default: return "";
        }
    }

    /// <summary>Whole-number amount for count-style effects (attempts, belt, cup, removals, retriggers). Min 1.</summary>
    public static int WholeCount(float amount) => Mathf.Max(1, Mathf.RoundToInt(amount));

    /// <summary>For RetriggerMatchingAdditives: amount = how many times the matching additives fire again (min 1).</summary>
    public static int RetriggerCount(float amount) => Mathf.Max(1, Mathf.RoundToInt(amount));

    /// <summary>For AddAdditiveToDeck: additiveAddCount plus the effect's level growth (its amount - 1, so amount 1 = just additiveAddCount).</summary>
    public int AddCount(float amount) => Mathf.Max(1, additiveAddCount + Mathf.RoundToInt(Mathf.Max(0f, amount - 1f)));

    private string DescribeAddToDeck(int count)
    {
        return (createRandomAdditive
                   ? $"Adds {(count > 1 ? $"{count} random additives" : "a random additive")} to your deck"
                   : (additiveToAdd != null
                       ? $"Adds '{additiveToAdd.additiveName}'{(count > 1 ? $" x{count}" : "")} to your deck"
                       : "Adds an additive to your deck"))
               + (consumeMatchedAdditives ? " (consumes the matched additives)" : "");
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

        // Each Required Additives entry must be matched by a DIFFERENT cup additive - so listing
        // Potato twice really needs two Potatoes in the cup, not one Potato counted twice.
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
                var match = cup.FirstOrDefault(a => a.template == required && !specificMatches.Contains(a));
                if (match == null)
                {
                    // Most common cause of "it's in the cup but the combo won't fire": the cup has an
                    // additive with the same NAME, but it's a different asset (a duplicate, or an old
                    // copy still sitting in the shop pool / starting deck).
                    var sameName = cup.FirstOrDefault(a => a.template != null && a.template != required &&
                                                            a.Name == required.additiveName);
                    int owned = cup.Count(a => a.template == required);
                    failReason = sameName != null
                        ? $"the cup has an additive named '{required.additiveName}', but it's a DIFFERENT asset " +
                          $"('{sameName.template.name}') than the one in Required Additives ('{required.name}') - " +
                          "point both at the same asset, or delete the duplicate"
                        : owned > 0
                            ? $"Required Additives lists '{required.additiveName}' more times than there are in the cup ({owned})"
                            : $"missing specific additive '{required.additiveName}' in the cup";
                    return false;
                }
                specificMatches.Add(match);
            }
        }

        var flavorTouching = hasFlavorReq
            ? cup.Where(a => requiredFlavors.Any(f => a.HasFlavor(f))).ToList()
            : new List<AdditiveInstance>();

        matchingAdditives = flavorTouching.Union(specificMatches).ToList();

        // Minimum Matching Additives counts flavor matches. On a combo with NO required flavors
        // (only specific additives, e.g. Potato + Oil) there's nothing to count flavor-wise, so it
        // counts the specific matches instead - previously any minimum at all made such a combo
        // impossible to trigger.
        int matchCount = hasFlavorReq ? flavorTouching.Count : specificMatches.Count;
        if (minimumMatchingAdditives > 0 && matchCount < minimumMatchingAdditives)
        {
            failReason = hasFlavorReq
                ? $"only {matchCount} flavor-matching additive(s) in the cup, needs at least {minimumMatchingAdditives}"
                : $"only {matchCount} of the required additive(s) in the cup, needs at least {minimumMatchingAdditives}";
            return false;
        }

        return true;
    }
}