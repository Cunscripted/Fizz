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

    /// <summary>Per-run progress of one combo toward its next level. Lives here (not on the asset) so it resets every run.</summary>
    public class ComboProgress
    {
        public int level = 1;
        public int uses; // uses so far toward the NEXT level
    }

    private readonly Dictionary<FlavorComboRule, ComboProgress> _comboProgress = new Dictionary<FlavorComboRule, ComboProgress>();

    /// <summary>Fires (rule, new level) when a combo levels up - e.g. for a sound or banner.</summary>
    public event System.Action<FlavorComboRule, int> OnComboLeveledUp;

    /// <summary>The combo's current level/uses this run (created at level 1 on first request).</summary>
    public ComboProgress GetProgress(FlavorComboRule rule)
    {
        if (rule == null) return new ComboProgress();
        if (!_comboProgress.TryGetValue(rule, out var p))
        {
            p = new ComboProgress();
            _comboProgress[rule] = p;
        }
        return p;
    }

    public struct ScoreResult
    {
        public double finalPoints;
        public double finalMult;
        public double total; // double: scores can go far past float/int limits
        public List<string> log;
        public List<ScoreEvent> events;
    }

    private void Awake()
    {
        // Combo assets keep runtime fields (syrup boosts, "added once" flags) in memory between
        // scene loads - clear them so a retry/new run doesn't inherit the last run's upgrades.
        foreach (var rule in comboRules)
            if (rule != null) rule.ResetRuntimeState();

        var names = comboRules.Where(r => r != null).Select(r => r.comboName);
        Debug.Log($"[SodaScoringManager] {comboRules.Count(r => r != null)} combo rule(s) registered: " +
                  $"{(comboRules.Count > 0 ? string.Join(", ", names) : "(none)")}. " +
                  "If a combo you expect isn't listed here, it was never dragged into this component's " +
                  "Combo Rules list - it can never fire or show as available regardless of anything else.", this);
    }

    public ScoreResult ScoreSoda(ScoringContext context)
    {
        double points = 0.0;
        double mult = 0.0;
        var log = new List<string>();
        var events = new List<ScoreEvent>();
        var cup = context.cup;
        context.retriggerAllDepth = 0;

        void AddEvent(ScoreEvent ev)
        {
            // Stamped here, centrally, AFTER whatever mutated points/mult for this
            // specific event has already run - so every event carries the correct
            // running total at exactly that point in the sequence, letting a
            // presentation layer count the score up event by event during playback.
            ev.runningTotal = points * System.Math.Max(mult, 1.0);
            ev.runningPoints = points;
            ev.runningMult = mult;
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
                    // |= rather than = - a RetriggerAllOtherAdditives card's base fire still
                    // emits the OTHER cards' re-fires through here, already flagged as retriggers.
                    ev.isRetrigger |= isRetrigger;
                    AddEvent(ev);
                });
            }

            // Syrup flavor bonuses are flavor-based too, so popularity scales them the same way.
            float effectiveness = context.getEffectiveness != null ? context.getEffectiveness(additive) : 1f;
            flavorPointsBonus *= effectiveness;
            flavorMultBonus *= effectiveness;

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

            var progress = GetProgress(rule);
            int level = progress.level;
            int triggerCount = rule.EffectiveTriggerCount;
            bool consumedThisPass = false;

            for (int t = 0; t < triggerCount; t++)
            {
                // Every effect stacks - e.g. +20 points AND +3 mult AND retrigger, in list order.
                for (int e = 0; e < rule.effects.Count; e++)
                {
                    var effect = rule.effects[e];
                    if (effect == null) continue;
                    if (effect.onlyOncePerRun && rule.HasEffectFiredOnce(e)) continue;
                    if (effect.onlyOncePerRun) rule.MarkEffectFiredOnce(e);
                    float amount = rule.GetEffectAmount(e, level);

                    void Upgrade(string label) => AddEvent(new ScoreEvent
                    {
                        kind = ScoreEvent.Kind.Upgrade, customLabel = label, anchor = context.fallbackAnchor,
                        sourceLabel = rule.comboName, isCombo = true
                    });

                    switch (effect.type)
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
                        {
                            int times = FlavorComboRule.RetriggerCount(amount);
                            for (int r = 0; r < times; r++)
                                foreach (var a in matching)
                                    a.ApplyEffect(ref points, ref mult, context, ev =>
                                    {
                                        ev.isRetrigger = true; // these fires only happen because the combo retriggered them
                                        AddEvent(ev);
                                    });
                            break;
                        }
                        case ComboBonusType.AddAdditiveToDeck:
                            if (AddComboAdditives(rule, rule.AddCount(amount), matching, context, AddEvent, log, !consumedThisPass))
                                consumedThisPass = true;
                            break;
                        case ComboBonusType.ReduceHeatPercent:
                        {
                            float fraction = Mathf.Clamp01(amount);
                            if (fraction > 0f && context.reduceHeat != null)
                            {
                                context.reduceHeat(fraction);
                                AddEvent(new ScoreEvent { kind = ScoreEvent.Kind.HeatReduced, amount = fraction, anchor = context.fallbackAnchor, sourceLabel = rule.comboName, isCombo = true });
                            }
                            if (fraction > 0f && effect.repeatEveryRound && PopularityManager.Instance != null)
                                PopularityManager.Instance.AddRoundEndCoolingPercent(fraction);
                            break;
                        }

                        // ---------- Syrup (ModifierData) abilities ----------

                        case ComboBonusType.BoostComboRule:
                        {
                            var target = effect.targetComboRule != null ? effect.targetComboRule : rule;
                            target.runtimeBonusAdd += amount;
                            Upgrade($"{target.comboName} {ScoreFormat.Signed(amount)}");
                            log.Add($"Combo '{rule.comboName}' boosted combo '{target.comboName}' by {amount}");
                            break;
                        }
                        case ComboBonusType.RetriggerComboRule:
                        {
                            // Safe even when targeting itself: this pass's triggerCount was read before the loop,
                            // so the extra triggers start from the NEXT soda.
                            var target = effect.targetComboRule != null ? effect.targetComboRule : rule;
                            int extra = FlavorComboRule.WholeCount(amount);
                            target.runtimeExtraTriggers += extra;
                            Upgrade($"{target.comboName} +{extra} trigger{(extra > 1 ? "s" : "")}");
                            break;
                        }
                        case ComboBonusType.GlobalMult:
                            // Granted before the global-mult step below, so it already counts for this soda.
                            if (RequireModifierManager(rule, effect))
                            {
                                modifierManager.AddGlobalMult(amount);
                                Upgrade($"{ScoreFormat.Signed(amount)} Mult forever");
                            }
                            break;

                        case ComboBonusType.RetriggerForFlavor:
                        {
                            int times = FlavorComboRule.WholeCount(amount);
                            if (effect.duration == ComboEffectDuration.RestOfRun)
                            {
                                if (RequireModifierManager(rule, effect))
                                {
                                    modifierManager.AddFlavorRetrigger(effect.targetFlavor, times);
                                    Upgrade($"{effect.targetFlavor} retrigger +{times} forever");
                                }
                                break;
                            }
                            var flavorMatches = cup.Where(a => a != null && a.HasFlavor(effect.targetFlavor)).ToList();
                            for (int r = 0; r < times; r++)
                                foreach (var a in flavorMatches)
                                    a.ApplyEffect(ref points, ref mult, context, ev =>
                                    {
                                        ev.isRetrigger = true;
                                        AddEvent(ev);
                                    });
                            break;
                        }

                        case ComboBonusType.BoostFlavorPoints:
                        case ComboBonusType.BoostFlavorMult:
                        {
                            bool isMult = effect.type == ComboBonusType.BoostFlavorMult;
                            if (effect.duration == ComboEffectDuration.RestOfRun)
                            {
                                if (RequireModifierManager(rule, effect))
                                {
                                    if (isMult) modifierManager.AddFlavorMult(effect.targetFlavor, amount);
                                    else modifierManager.AddFlavorPoints(effect.targetFlavor, amount);
                                    Upgrade($"{effect.targetFlavor} {ScoreFormat.Signed(amount)} {(isMult ? "Mult" : "Pts")} forever");
                                }
                                break;
                            }
                            // This soda: every cup additive of the flavor pays out now, scaled by popularity
                            // exactly like the syrup's per-additive bonus.
                            foreach (var a in cup)
                            {
                                if (a == null || !a.HasFlavor(effect.targetFlavor)) continue;
                                float bonus = amount * (context.getEffectiveness != null ? context.getEffectiveness(a) : 1f);
                                if (isMult) mult += bonus; else points += bonus;
                                Emit(isMult ? ScoreEvent.Kind.Mult : ScoreEvent.Kind.Points, bonus,
                                     context.resolveCardRect?.Invoke(a) ?? context.fallbackAnchor, rule.comboName, isCombo: true);
                            }
                            break;
                        }

                        case ComboBonusType.ScalePerFlavorOnBelt:
                        {
                            if (effect.duration == ComboEffectDuration.RestOfRun)
                            {
                                if (RequireModifierManager(rule, effect))
                                {
                                    modifierManager.AddFlavorScaling(effect.scalingFlavors, effect.scaleIsMult, amount);
                                    Upgrade($"{ScoreFormat.Signed(amount)} {(effect.scaleIsMult ? "Mult" : "Pts")} per belt match forever");
                                }
                                break;
                            }
                            int count = 0;
                            if (effect.scalingFlavors != null && context.beltFlavorCounts != null)
                                foreach (var flavor in effect.scalingFlavors)
                                    if (context.beltFlavorCounts.TryGetValue(flavor, out int c)) count += c;
                            if (count <= 0) break;
                            float scaled = amount * count;
                            if (effect.scaleIsMult) mult += scaled; else points += scaled;
                            Emit(effect.scaleIsMult ? ScoreEvent.Kind.Mult : ScoreEvent.Kind.Points, scaled,
                                 context.fallbackAnchor, rule.comboName, isCombo: true);
                            break;
                        }

                        case ComboBonusType.RemoveAdditiveFromDeck:
                        case ComboBonusType.RemoveRandomAdditiveFromDeck:
                            RemoveComboAdditives(rule, effect, FlavorComboRule.WholeCount(amount), context, AddEvent, log);
                            break;

                        case ComboBonusType.AddAttempt:
                        {
                            int n = FlavorComboRule.WholeCount(amount);
                            if (context.addAttempts == null)
                            {
                                Debug.LogWarning($"[SodaScoringManager] Combo '{rule.comboName}' AddAttempt fired, but context.addAttempts is unassigned - nothing granted.", this);
                                break;
                            }
                            context.addAttempts(n);
                            AddEvent(new ScoreEvent { kind = ScoreEvent.Kind.AttemptAdded, amount = n, anchor = context.fallbackAnchor, sourceLabel = rule.comboName, isCombo = true });
                            break;
                        }
                        case ComboBonusType.AddBeltSize:
                        {
                            int n = FlavorComboRule.WholeCount(amount);
                            context.addBeltSize?.Invoke(n);
                            AddEvent(new ScoreEvent { kind = ScoreEvent.Kind.BeltSizeIncreased, amount = n, anchor = context.fallbackAnchor, sourceLabel = rule.comboName, isCombo = true });
                            break;
                        }
                        case ComboBonusType.AddCupCapacity:
                        {
                            int n = FlavorComboRule.WholeCount(amount);
                            context.addCupCapacity?.Invoke(n);
                            AddEvent(new ScoreEvent { kind = ScoreEvent.Kind.CupCapacityIncreased, amount = n, anchor = context.fallbackAnchor, sourceLabel = rule.comboName, isCombo = true });
                            break;
                        }
                        case ComboBonusType.BoostAmountGrowth:
                            if (RequireModifierManager(rule, effect))
                            {
                                modifierManager.AddAmountGrowthRule(effect.filterByFlavor, effect.targetFlavor,
                                                                    effect.amountGrowthMode, amount, effect.flipRate);
                                Upgrade(effect.amountGrowthMode == AmountGrowthMode.FlipNegativeToPositive
                                    ? "Shrinking cards now grow"
                                    : $"Growth {ScoreFormat.Signed(amount)}");
                            }
                            break;
                    }
                }
            }
            log.Add($"Combo '{rule.comboName}' (Lv {level}) triggered {triggerCount}x with {rule.effects.Count} effect(s)");

            // Leveling: one use per soda the combo triggers in (not per trigger/retrigger).
            if (!rule.IsMaxLevel(progress.level))
            {
                progress.uses++;
                if (progress.uses >= rule.UsesRequiredForLevel(progress.level))
                {
                    progress.uses = 0;
                    progress.level++;
                    AddEvent(new ScoreEvent
                    {
                        kind = ScoreEvent.Kind.ComboLevelUp,
                        amount = progress.level,
                        anchor = context.fallbackAnchor,
                        sourceLabel = rule.comboName,
                        isCombo = true
                    });
                    log.Add($"Combo '{rule.comboName}' leveled up to {progress.level}!");
                    OnComboLeveledUp?.Invoke(rule, progress.level);
                }
            }
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
            total = points * System.Math.Max(mult, 1.0),
            log = log,
            events = events
        };

        return result;
    }

    /// <summary>
    /// A combo's AddAdditiveToDeck effect: creates `count` additives (specific or random, per the
    /// rule's Add Additive fields) and, if consumeMatchedAdditives is on and consumeAllowed (only the
    /// first time per scoring pass), removes the matched additives - a crafting/fusion effect.
    /// Returns true if it consumed the matched additives.
    /// </summary>
    private bool AddComboAdditives(FlavorComboRule rule, int count, List<AdditiveInstance> matching, ScoringContext context,
                                   System.Action<ScoreEvent> addEvent, List<string> log, bool consumeAllowed)
    {
        if (rule.hasAddedToDeck && rule.addOnlyOnce)
        {
            Debug.Log($"[SodaScoringManager] Combo '{rule.comboName}' AddAdditiveToDeck skipped - " +
                      "already added once and Add Only Once is on.", this);
            return false;
        }

        bool addedAny = false;
        for (int i = 0; i < count; i++)
        {
            var created = rule.createRandomAdditive
                ? RandomAdditivePicker.Pick(rule.randomAdditivePool, rule.filterByFlavor, rule.randomFlavorFilter, context.allAdditivesPool)
                : rule.additiveToAdd;

            if (created == null)
            {
                // Silent no-op otherwise - this makes the two most common misconfigurations
                // visible instead of the combo just quietly doing nothing.
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
            addEvent(new ScoreEvent
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

        // Consume the additives that satisfied this combo - only once per scoring pass, and only
        // if something was actually created, so a misconfigured creation doesn't destroy cards for nothing.
        if (!rule.consumeMatchedAdditives || !addedAny || !consumeAllowed) return false;

        if (context.removeFromDeck == null)
        {
            Debug.LogWarning($"[SodaScoringManager] Combo '{rule.comboName}' has consumeMatchedAdditives on, but " +
                             "context.removeFromDeck is unassigned - nothing was actually consumed.", this);
            return false;
        }

        foreach (var consumed in matching)
        {
            if (consumed == null) continue;
            context.removeFromDeck.Invoke(consumed);
            addEvent(new ScoreEvent
            {
                kind = ScoreEvent.Kind.Deleted,
                amount = 0f,
                anchor = context.resolveCardRect?.Invoke(consumed) ?? context.fallbackAnchor,
                sourceLabel = consumed.Name
            });
        }
        log.Add($"Combo '{rule.comboName}' consumed {matching.Count} matched additive(s).");
        return true;
    }

    /// <summary>Permanent syrup-style combo rewards are stored on the ModifierManager - warns (instead of silently doing nothing) if none is assigned.</summary>
    private bool RequireModifierManager(FlavorComboRule rule, ComboEffect effect)
    {
        if (modifierManager != null) return true;
        Debug.LogWarning($"[SodaScoringManager] Combo '{rule.comboName}' {effect.type} fired, but this SodaScoringManager " +
                         "has no Modifier Manager assigned - permanent combo rewards are stored there, so nothing was granted.", this);
        return false;
    }

    /// <summary>
    /// A combo's RemoveAdditiveFromDeck / RemoveRandomAdditiveFromDeck effect: removes up to `count` owned
    /// additives (a specific template, or random - optionally of one flavor), same as the matching syrups.
    /// </summary>
    private void RemoveComboAdditives(FlavorComboRule rule, ComboEffect effect, int count, ScoringContext context,
                                      System.Action<ScoreEvent> addEvent, List<string> log)
    {
        if (context.ownedAdditives == null || context.removeFromDeck == null)
        {
            Debug.LogWarning($"[SodaScoringManager] Combo '{rule.comboName}' {effect.type} fired, but the scoring context " +
                             "has no owned additives / removeFromDeck - nothing was removed.", this);
            return;
        }

        bool specific = effect.type == ComboBonusType.RemoveAdditiveFromDeck;
        if (specific && effect.additiveToRemove == null)
        {
            Debug.LogWarning($"[SodaScoringManager] Combo '{rule.comboName}' RemoveAdditiveFromDeck fired, but Additive To Remove is unassigned.", this);
            return;
        }

        // Snapshot first - removeFromDeck changes the owned list as we go.
        var candidates = context.ownedAdditives
            .Where(a => a != null && (specific
                ? a.template == effect.additiveToRemove
                : !effect.filterByFlavor || a.HasFlavor(effect.targetFlavor)))
            .ToList();

        int removed = 0;
        for (int i = 0; i < count && candidates.Count > 0; i++)
        {
            int index = specific ? 0 : Random.Range(0, candidates.Count);
            var victim = candidates[index];
            candidates.RemoveAt(index);

            context.removeFromDeck.Invoke(victim);
            removed++;
            addEvent(new ScoreEvent
            {
                kind = ScoreEvent.Kind.Deleted,
                amount = 0f,
                anchor = context.resolveCardRect?.Invoke(victim) ?? context.fallbackAnchor,
                sourceLabel = victim.Name
            });
        }
        if (removed > 0) log.Add($"Combo '{rule.comboName}' removed {removed} additive(s) from the deck.");
    }

    /// <summary>
    /// Call whenever the round's score requirement changes (e.g. from
    /// RoundManager.BeginRound()) to keep requiredScoreText in sync.
    /// </summary>
    public void SetRequiredScoreDisplay(double requirement)
    {
        if (requiredScoreText != null)
            requiredScoreText.text = ScoreFormat.Big(requirement, scoreFormat);
    }

    public void SetRequiredScoreDisplay(int requirement) => SetRequiredScoreDisplay((double)requirement);

    /// <summary>
    /// Directly sets currentScoreText, e.g. to the round's cumulative total after an
    /// attempt. Not called automatically by ScoreSoda anymore - the caller (RoundManager)
    /// is the one that actually knows whether/how scores accumulate across attempts.
    /// </summary>
    public void SetCurrentScoreDisplay(double total)
    {
        if (currentScoreText != null)
            currentScoreText.text = ScoreFormat.Big(total, scoreFormat);
    }
}