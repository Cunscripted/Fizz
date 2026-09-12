using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Runs the scoring pass for a built soda. Order of resolution:
///   1. Each cup additive fires its own effect (1 + its retriggers + any syrup
///      retrigger-for-flavor bonus), plus any flat flavor point/mult bonus from syrups.
///   2. Belt-flavor-scaling syrups add their bonus once, based on belt contents.
///   3. Combo rules are evaluated against the whole cup, each firing its effective
///      trigger count (normally 1, more with a retrigger-combo syrup).
///   4. Any global mult bonus from syrups is added last.
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
    [Tooltip("Optional - supplies syrup-driven flavor rules, belt scaling, and global mult.")]
    [SerializeField] private ModifierManager modifierManager;

    public struct ScoreResult
    {
        public float finalPoints;
        public float finalMult;
        public float total;
        public List<string> log;
        public List<ScoreEvent> events;
    }

    public ScoreResult ScoreSoda(ScoringContext context)
    {
        float points = 0f;
        float mult = 0f;
        var log = new List<string>();
        var events = new List<ScoreEvent>();
        var cup = context.cup;

        void Emit(ScoreEvent.Kind kind, float amount, RectTransform anchor, string label)
        {
            if (Mathf.Approximately(amount, 0f)) return;
            events.Add(new ScoreEvent { kind = kind, amount = amount, anchor = anchor, sourceLabel = label });
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
                additive.ApplyEffect(ref points, ref mult, context, events.Add);

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
                context.beltFlavorCounts?.TryGetValue(scaling.flavor, out count);
                if (count <= 0) continue;

                float bonus = scaling.amountPerCount * count;
                if (scaling.isMult) mult += bonus; else points += bonus;
                Emit(scaling.isMult ? ScoreEvent.Kind.Mult : ScoreEvent.Kind.Points, bonus, context.fallbackAnchor, "Syrup belt-scaling");
                log.Add($"Syrup belt-scaling ({scaling.flavor} x{count}): +{bonus} {(scaling.isMult ? "mult" : "points")}");
            }
        }

        foreach (var rule in comboRules)
        {
            if (!rule.Evaluate(cup, out var matching)) continue;

            int triggerCount = rule.EffectiveTriggerCount;
            for (int t = 0; t < triggerCount; t++)
            {
                float amount = rule.EffectiveBonusAmount;
                switch (rule.bonusType)
                {
                    case ComboBonusType.BonusPoints:
                        points += amount;
                        Emit(ScoreEvent.Kind.Points, amount, context.fallbackAnchor, rule.comboName);
                        break;
                    case ComboBonusType.BonusMult:
                        mult += amount;
                        Emit(ScoreEvent.Kind.Mult, amount, context.fallbackAnchor, rule.comboName);
                        break;
                    case ComboBonusType.BonusXMult:
                        mult *= amount;
                        Emit(ScoreEvent.Kind.XMult, amount, context.fallbackAnchor, rule.comboName);
                        break;
                    case ComboBonusType.RetriggerMatchingAdditives:
                        foreach (var a in matching)
                            a.ApplyEffect(ref points, ref mult, context, events.Add);
                        break;
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

        return new ScoreResult
        {
            finalPoints = points,
            finalMult = mult,
            total = points * Mathf.Max(mult, 1f),
            log = log,
            events = events
        };
    }
}