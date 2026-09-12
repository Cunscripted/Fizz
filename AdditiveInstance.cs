using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Runtime, per-playthrough wrapper around an AdditiveData template.
/// Modifiers and upgrades write their bonuses HERE, never onto the
/// ScriptableObject asset itself (writing to the SO would persist the
/// change in the editor across play sessions - a classic Unity gotcha).
/// One of these exists per additive the player owns for the run.
/// </summary>
[Serializable]
public class AdditiveInstance
{
    public AdditiveData template;

    // Runtime bonuses stacked on by modifiers/upgrades/other additives over a run.
    public float bonusPoints;
    public float bonusMult;
    public int bonusRetrigger;

    // Guards AddAdditiveToDeck's "only once" option.
    [NonSerialized] public bool hasAddedToDeck;

    public AdditiveInstance(AdditiveData template)
    {
        this.template = template;
    }

    public List<FlavorType> Flavors => template.flavors;
    public bool HasFlavor(FlavorType f) => template.HasFlavor(f);
    public string Name => template.additiveName;
    public int TotalRetriggers => template.baseRetriggerCount + bonusRetrigger;

    /// <summary>
    /// Applies this instance's effect once. context may be null for effect types
    /// that don't need it (FlatPoints/FlatMult/XMult/RetriggerSelf); it's required
    /// for anything that reaches outside this one card. emit, if provided, is called
    /// for every effect that actually contributes points/mult to this soda's score -
    /// used to drive floating score text and audio during scoring playback.
    /// </summary>
    public void ApplyEffect(ref float points, ref float mult, ScoringContext context, Action<ScoreEvent> emit = null)
    {
        RectTransform anchor = context?.resolveCardRect?.Invoke(this) ?? context?.fallbackAnchor;
        void Emit(ScoreEvent.Kind kind, float amount)
        {
            if (Mathf.Approximately(amount, 0f)) return;
            emit?.Invoke(new ScoreEvent { kind = kind, amount = amount, anchor = anchor, sourceLabel = Name });
        }

        switch (template.effectType)
        {
            case EffectType.FlatPoints:
            {
                float p = template.amount + bonusPoints;
                points += p;
                Emit(ScoreEvent.Kind.Points, p);
                break;
            }
            case EffectType.FlatMult:
            {
                float m = template.amount + bonusMult;
                mult += m;
                Emit(ScoreEvent.Kind.Mult, m);
                break;
            }
            case EffectType.XMult:
            {
                float x = template.amount + bonusMult;
                mult *= x;
                Emit(ScoreEvent.Kind.XMult, x);
                break;
            }
            case EffectType.RetriggerSelf:
                if (template.retriggerPayload == RetriggerPayloadType.Mult)
                {
                    float m = template.amount + bonusMult;
                    mult += m;
                    Emit(ScoreEvent.Kind.Mult, m);
                }
                else
                {
                    float p = template.amount + bonusPoints;
                    points += p;
                    Emit(ScoreEvent.Kind.Points, p);
                }
                break;

            case EffectType.BuffRandomAdditivePoints:
                BuffRandomOwned(context, isMult: false);
                break;
            case EffectType.BuffRandomAdditiveMult:
                BuffRandomOwned(context, isMult: true);
                break;

            case EffectType.PointsPerFlavorOnBelt:
            {
                float p = template.amount * GetBeltFlavorCount(context);
                points += p;
                Emit(ScoreEvent.Kind.Points, p);
                break;
            }
            case EffectType.MultPerFlavorOnBelt:
            {
                float m = template.amount * GetBeltFlavorCount(context);
                mult += m;
                Emit(ScoreEvent.Kind.Mult, m);
                break;
            }

            case EffectType.AddAdditiveToDeck:
                if (template.additiveToAdd != null && (!hasAddedToDeck || !template.addToDeckOnlyOnce))
                {
                    context?.addToDeck?.Invoke(template.additiveToAdd);
                    hasAddedToDeck = true;
                }
                break;
        }
    }

    private int GetBeltFlavorCount(ScoringContext context)
    {
        if (context?.beltFlavorCounts == null) return 0;
        return context.beltFlavorCounts.TryGetValue(template.scalingFlavor, out int c) ? c : 0;
    }

    private void BuffRandomOwned(ScoringContext context, bool isMult)
    {
        if (context?.ownedAdditives == null || context.ownedAdditives.Count == 0) return;

        // Prefer buffing a different additive than this one; fall back to self
        // if this is the only additive the player owns.
        var others = context.ownedAdditives.Where(a => a != this).ToList();
        var candidates = others.Count > 0 ? others : context.ownedAdditives;

        var target = candidates[UnityEngine.Random.Range(0, candidates.Count)];
        if (isMult) target.bonusMult += template.amount;
        else target.bonusPoints += template.amount;
    }
}