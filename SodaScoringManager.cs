using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;

/// <summary>
/// Runs the scoring pass for a built soda. Order of resolution:
///   1. Each cup additive fires its own effect (1 + its retriggers + any syrup
///      retrigger-for-flavor bonus), plus any flat flavor point/mult bonus from syrups.
///   2. Belt-flavor-scaling syrups add their bonus once, based on belt contents.
///   3. Combo rules are evaluated against the whole cup, each firing its effective
///      trigger count (normally 1, more with a retrigger-combo syrup).
///   4. Any global mult bonus from syrups is added last.
///   5. PassiveOnBelt additives still sitting on the belt (not the cup) contribute
///      their bonus once every cup additive/combo/syrup above has finished - so a
///      PassiveOnBelt x-mult, say, multiplies the FULL total the cup just built,
///      rather than a smaller running total from before the cup even fired.
/// Mirrors Balatro's chips -> mult -> total flow (points here == chips).
///
/// Every step that actually changes points/mult also appends a ScoreEvent to
/// ScoreResult.events, in the exact order it happened. The total/points/mult
/// are fully computed synchronously here - events are just a record of how we
/// got there, for a presentation layer (floating text + audio) to play back
/// afterward at whatever pace it likes, completely decoupled from this math.
/// </summary>
public class SodaScoringManager : MonoBehaviour
{
    [SerializeField] private List<FlavorComboRule> comboRules = new List<FlavorComboRule>();
    /// <summary>Exposed so other systems (e.g. an active-combo indicator) can evaluate the same rule set live.</summary>
    public IReadOnlyList<FlavorComboRule> ComboRules => comboRules;
    [Tooltip("Optional - supplies syrup-driven flavor rules, belt scaling, and global mult.")]
    [SerializeField] private ModifierManager modifierManager;

    [Header("Score Display (optional - just drag TMP_Text fields in, no event wiring needed)")]
    [Tooltip("Not auto-updated by ScoreSoda anymore (scores can accumulate across attempts, which this " +
             "class doesn't track) - call SetCurrentScoreDisplay() from whatever does track the total, " +
             "e.g. RoundManager.")]
    public TMP_Text currentScoreText;
    [Tooltip("Not auto-updated by ScoreSoda (the requirement isn't known here) - call " +
             "SetRequiredScoreDisplay() whenever the round's requirement changes, e.g. from " +
             "RoundManager.BeginRound().")]
    public TMP_Text requiredScoreText;
    [Tooltip("Number format applied to both fields above, e.g. \"N0\" for comma-separated whole numbers.")]
    public string scoreFormat = "N0";

    public struct ScoreResult
    {
        public float finalPoints;
        public float finalMult;
        public float total;
        public List<string> log;
        public List<ScoreEvent> events;
    }

    private void Awake()
    {
        var names = comboRules.Where(r => r != null).Select(r => r.comboName);
        Debug.Log($"[SodaScoringManager] {comboRules.Count(r => r != null)} combo rule(s) registered: " +
                  $"{(comboRules.Count > 0 ? string.Join(", ", names) : "(none)")}. " +
                  "If a combo you expect isn't listed here, it was never dragged into this component's " +
                  "Combo Rules list - it can never fire or show as available regardless of anything else.", this);
    }

    public ScoreResult ScoreSoda(ScoringContext context)
    {
        float points = 0f;
        float mult = 0f;
        var log = new List<string>();
        var events = new List<ScoreEvent>();
        var cup = context.cup;

        void AddEvent(ScoreEvent ev)
        {
            // Stamped here, centrally, AFTER whatever mutated points/mult for this
            // specific event has already run - so every event carries the correct
            // running total at exactly that point in the sequence, letting a
            // presentation layer count the score up event by event during playback.
            ev.runningTotal = points * Mathf.Max(mult, 1f);
            events.Add(ev);
        }

        void Emit(ScoreEvent.Kind kind, float amount, RectTransform anchor, string label, bool isCombo = false)
        {
            if (Mathf.Approximately(amount, 0f)) return;
            AddEvent(new ScoreEvent { kind = kind, amount = amount, anchor = anchor, sourceLabel = label, isCombo = isCombo });
        }

        foreach (var additive in cup)
        {
            float flavorPointsBonus = 0f;
            float flavorMultBonus = 0f;
            int flavorRetriggerBonus = 0;

            if (modifierManager != null)
            {
                foreach (var rule in modifierManager.FlavorRules)
                {
                    if (!additive.HasFlavor(rule.flavor)) continue;
                    flavorPointsBonus += rule.pointsBonus;
                    flavorMultBonus += rule.multBonus;
                    flavorRetriggerBonus += rule.retriggerBonus;
                }
            }

            var anchor = context.resolveCardRect?.Invoke(additive) ?? context.fallbackAnchor;

            int fires = 1 + additive.TotalRetriggers + flavorRetriggerBonus;
            for (int i = 0; i < fires; i++)
            {
                bool isRetrigger = i > 0; // first fire is the "base" trigger; every fire after is a retrigger
                additive.ApplyEffect(ref points, ref mult, context, ev =>
                {
                    ev.isRetrigger = isRetrigger;
                    AddEvent(ev);
                });
            }

            if (flavorPointsBonus != 0f)
            {
                points += flavorPointsBonus;
                Emit(ScoreEvent.Kind.Points, flavorPointsBonus, anchor, $"{additive.Name} (syrup)");
            }
            if (flavorMultBonus != 0f)
            {
                mult += flavorMultBonus;
                Emit(ScoreEvent.Kind.Mult, flavorMultBonus, anchor, $"{additive.Name} (syrup)");
            }

            log.Add($"{additive.Name}: fired {fires}x -> running {points:0} pts / {mult:0.0} mult");
        }

        if (modifierManager != null)
        {
            foreach (var scaling in modifierManager.FlavorScalingBonuses)
            {
                int count = 0;
                if (scaling.flavors != null && context.beltFlavorCounts != null)
                {
                    foreach (var flavor in scaling.flavors)
                        if (context.beltFlavorCounts.TryGetValue(flavor, out int c)) count += c;
                }
                if (count <= 0) continue;

                float bonus = scaling.amountPerCount * count;
                if (scaling.isMult) mult += bonus; else points += bonus;
                Emit(scaling.isMult ? ScoreEvent.Kind.Mult : ScoreEvent.Kind.Points, bonus, context.fallbackAnchor, "Syrup belt-scaling");
                log.Add($"Syrup belt-scaling ({string.Join("/", scaling.flavors ?? new List<FlavorType>())} x{count}): +{bonus} {(scaling.isMult ? "mult" : "points")}");
            }
        }

        foreach (var rule in comboRules)
        {
            if (rule == null) continue;

            if (!rule.Evaluate(cup, out var matching, out var failReason))
            {
                if (!string.IsNullOrEmpty(failReason))
                    Debug.Log($"[SodaScoringManager] Combo '{rule.comboName}' did not trigger: {failReason}.", rule);
                continue;
            }

            int triggerCount = rule.EffectiveTriggerCount;
            for (int t = 0; t < triggerCount; t++)
            {
                float amount = rule.EffectiveBonusAmount;
                switch (rule.bonusType)
                {
                    case ComboBonusType.BonusPoints:
                        points += amount;
                        Emit(ScoreEvent.Kind.Points, amount, context.fallbackAnchor, rule.comboName, isCombo: true);
                        break;
                    case ComboBonusType.BonusMult:
                        mult += amount;
                        Emit(ScoreEvent.Kind.Mult, amount, context.fallbackAnchor, rule.comboName, isCombo: true);
                        break;
                    case ComboBonusType.BonusXMult:
                        mult *= amount;
                        Emit(ScoreEvent.Kind.XMult, amount, context.fallbackAnchor, rule.comboName, isCombo: true);
                        break;
                    case ComboBonusType.RetriggerMatchingAdditives:
                        foreach (var a in matching)
                            a.ApplyEffect(ref points, ref mult, context, ev =>
                            {
                                ev.isRetrigger = true; // these fires only happen because the combo retriggered them
                                AddEvent(ev);
                            });
                        break;
                    case ComboBonusType.AddAdditiveToDeck:
                    {
                        if (rule.hasAddedToDeck && rule.addOnlyOnce)
                        {
                            Debug.Log($"[SodaScoringManager] Combo '{rule.comboName}' AddAdditiveToDeck skipped - " +
                                      "already added once and Add Only Once is on.", this);
                            break;
                        }

                        int addCount = Mathf.Max(1, rule.additiveAddCount);
                        bool addedAny = false;
                        for (int i = 0; i < addCount; i++)
                        {
                            var created = rule.createRandomAdditive
                                ? RandomAdditivePicker.Pick(rule.randomAdditivePool, rule.filterByFlavor, rule.randomFlavorFilter, context.allAdditivesPool)
                                : rule.additiveToAdd;

                            if (created == null)
                            {
                                // Silent no-op otherwise - this makes the two most common
                                // misconfigurations visible instead of the combo just quietly
                                // doing nothing with no error and no feedback.
                                string reason = rule.createRandomAdditive
                                    ? (rule.filterByFlavor
                                        ? $"no additive of flavor '{rule.randomFlavorFilter}' found in randomAdditivePool or allAdditivesPool"
                                        : "randomAdditivePool is empty and Filter By Flavor is off, so there's nothing to search - " +
                                          "either fill the pool or turn Filter By Flavor on")
                                    : "additiveToAdd is not assigned (and Create Randomly is off)";
                                Debug.LogWarning($"[SodaScoringManager] Combo '{rule.comboName}' AddAdditiveToDeck fired but added nothing: {reason}.", this);
                                continue;
                            }

                            context.addToDeck?.Invoke(created);
                            addedAny = true;
                            AddEvent(new ScoreEvent
                            {
                                kind = ScoreEvent.Kind.AddedToDeck,
                                amount = 0f,
                                anchor = context.fallbackAnchor,
                                sourceLabel = rule.comboName,
                                isCombo = true,
                                addedAdditive = created
                            });
                        }
                        if (addedAny) rule.hasAddedToDeck = true;

                        // Consume the additives that satisfied this combo's requirement, turning it into a
                        // crafting/fusion effect - only on the FIRST trigger of this scoring pass (t == 0),
                        // since `matching` is the same fixed set every trigger and a card can't be consumed
                        // twice just because a retrigger-combo syrup makes this rule fire more than once.
                        // Gated on addedAny so a misconfigured creation (nothing actually added) doesn't
                        // destroy the player's cards for nothing.
                        if (rule.consumeMatchedAdditives && addedAny && t == 0)
                        {
                            if (context.removeFromDeck == null)
                            {
                                Debug.LogWarning($"[SodaScoringManager] Combo '{rule.comboName}' has " +
                                                  "consumeMatchedAdditives on, but context.removeFromDeck is " +
                                                  "unassigned - nothing was actually consumed.", this);
                            }
                            else
                            {
                                foreach (var consumed in matching)
                                {
                                    if (consumed == null) continue;
                                    context.removeFromDeck.Invoke(consumed);
                                    var consumedAnchor = context.resolveCardRect?.Invoke(consumed) ?? context.fallbackAnchor;
                                    AddEvent(new ScoreEvent
                                    {
                                        kind = ScoreEvent.Kind.Deleted,
                                        amount = 0f,
                                        anchor = consumedAnchor,
                                        sourceLabel = consumed.Name
                                    });
                                }
                                log.Add($"Combo '{rule.comboName}' consumed {matching.Count} matched additive(s).");
                            }
                        }
                        break;
                    }
                }
            }
            log.Add($"Combo '{rule.comboName}' triggered {triggerCount}x ({rule.bonusType}: {rule.EffectiveBonusAmount})");
        }

        if (modifierManager != null && modifierManager.GlobalMultBonus > 0f)
        {
            mult += modifierManager.GlobalMultBonus;
            Emit(ScoreEvent.Kind.Mult, modifierManager.GlobalMultBonus, context.fallbackAnchor, "Global syrup mult");
            log.Add($"Syrups: +{modifierManager.GlobalMultBonus} global mult");
        }

        // Passive belt effects: additives with PassiveOnBelt still sitting on the belt
        // (not the cup) contribute their bonus just by being present - no need to be
        // mixed into the soda. Applied LAST, after every cup additive, combo, and syrup
        // bonus above has already run, so e.g. a PassiveOnBelt x-mult multiplies the
        // full total the rest of the soda just built, instead of a smaller running
        // total from before the cup's own cards had fired.
        if (context.beltInstances != null)
        {
            foreach (var beltAdditive in context.beltInstances)
            {
                if (beltAdditive?.template == null || beltAdditive.template.effectType != EffectType.PassiveOnBelt)
                    continue;
                beltAdditive.ApplyEffect(ref points, ref mult, context, AddEvent, isInCup: false);
            }
        }

        var result = new ScoreResult
        {
            finalPoints = points,
            finalMult = mult,
            total = points * Mathf.Max(mult, 1f),
            log = log,
            events = events
        };

        return result;
    }

    /// <summary>
    /// Call whenever the round's score requirement changes (e.g. from
    /// RoundManager.BeginRound()) to keep requiredScoreText in sync.
    /// </summary>
    public void SetRequiredScoreDisplay(int requirement)
    {
        if (requiredScoreText != null)
            requiredScoreText.text = requirement.ToString(scoreFormat);
    }

    /// <summary>
    /// Directly sets currentScoreText, e.g. to the round's cumulative total after an
    /// attempt. Not called automatically by ScoreSoda anymore - the caller (RoundManager)
    /// is the one that actually knows whether/how scores accumulate across attempts.
    /// </summary>
    public void SetCurrentScoreDisplay(float total)
    {
        if (currentScoreText != null)
            currentScoreText.text = Mathf.RoundToInt(total).ToString(scoreFormat);
    }
}