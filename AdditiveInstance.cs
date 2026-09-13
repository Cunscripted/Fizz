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

    // Guards deleteSelfAfterUse's "only once" option.
    [NonSerialized] public bool hasDeletedSelf;

    // Permanent drift from amountChangePerUse, accumulated over the run. Never applied
    // directly to template.amount (see class doc) - EffectiveAmount below is what every
    // effect actually reads instead of template.amount.
    public float amountGrowth;

    public AdditiveInstance(AdditiveData template)
    {
        this.template = template;
    }

    public List<FlavorType> Flavors => template.flavors;
    public bool HasFlavor(FlavorType f) => template.HasFlavor(f);
    public string Name => template.additiveName;
    public int TotalRetriggers => template.baseRetriggerCount + bonusRetrigger;

    /// <summary>True if this instance has never been buffed/upgraded - no accumulated bonuses of any kind.</summary>
    public bool IsUnmodified => bonusPoints == 0f && bonusMult == 0f && bonusRetrigger == 0;

    /// <summary>
    /// template.amount plus the accumulated amountGrowth - NOT floored at 0, so a card
    /// that keeps depreciating (negative amountChangePerUse) can go past 0 and end up
    /// genuinely negative, meaning it actively SUBTRACTS from the score instead of just
    /// contributing nothing once depleted. Every effect below reads THIS instead of
    /// template.amount, so the drift applies uniformly regardless of what the amount
    /// means for a given effect type (points, mult, x-mult, buff strength, etc), and
    /// works the same whether the card fires from the cup or passively from the belt.
    /// </summary>
    public float EffectiveAmount => template.amount + amountGrowth;

    /// <summary>
    /// Applies this instance's effect once. context may be null for effect types
    /// that don't need it (FlatPoints/FlatMult/XMult/RetriggerSelf); it's required
    /// for anything that reaches outside this one card. emit, if provided, is called
    /// for every effect that actually contributes points/mult to this soda's score -
    /// used to drive floating score text and audio during scoring playback.
    ///
    /// bonusPoints/bonusMult are applied AFTER this card's own effect, unconditionally,
    /// regardless of this card's effectType - so a points-only card can still carry (and
    /// benefit from) a mult bonus from a BuffRandomAdditiveMult effect elsewhere, and vice
    /// versa. Any additive can accumulate bonuses of either kind, not just its own "native" one.
    ///
    /// deleteSelfAfterUse/amountChangePerUse are likewise applied AFTER the main
    /// switch, independent of effectType - optional downsides stackable on any card.
    ///
    /// isInCup distinguishes "actually mixed into the soda" from "just fired passively
    /// while sitting on the belt" (SodaScoringManager passes false for its belt-passive
    /// pass, true for the normal cup pass) - deleteSelfOnlyWhenInCup reads this to let a
    /// card self-destruct only when actually played, never just for sitting on the belt.
    /// </summary>
    public void ApplyEffect(ref float points, ref float mult, ScoringContext context, Action<ScoreEvent> emit = null, bool isInCup = true)
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
                points += EffectiveAmount;
                Emit(ScoreEvent.Kind.Points, EffectiveAmount);
                break;
            case EffectType.FlatMult:
                mult += EffectiveAmount;
                Emit(ScoreEvent.Kind.Mult, EffectiveAmount);
                break;
            case EffectType.XMult:
                mult *= EffectiveAmount;
                Emit(ScoreEvent.Kind.XMult, EffectiveAmount);
                break;
            case EffectType.RetriggerSelf:
                if (template.retriggerPayload == RetriggerPayloadType.Mult)
                {
                    mult += EffectiveAmount;
                    Emit(ScoreEvent.Kind.Mult, EffectiveAmount);
                }
                else
                {
                    points += EffectiveAmount;
                    Emit(ScoreEvent.Kind.Points, EffectiveAmount);
                }
                break;

            case EffectType.BuffRandomAdditivePoints:
                BuffRandomOwned(context, BuffTarget.Points, anchor, emit);
                break;
            case EffectType.BuffRandomAdditiveMult:
                BuffRandomOwned(context, BuffTarget.Mult, anchor, emit);
                break;
            case EffectType.BuffRandomAdditiveRetrigger:
                BuffRandomOwned(context, BuffTarget.Retrigger, anchor, emit);
                break;

            case EffectType.PointsPerFlavorOnBelt:
            {
                float p = EffectiveAmount * GetBeltFlavorCount(context);
                points += p;
                Emit(ScoreEvent.Kind.Points, p);
                break;
            }
            case EffectType.MultPerFlavorOnBelt:
            {
                float m = EffectiveAmount * GetBeltFlavorCount(context);
                mult += m;
                Emit(ScoreEvent.Kind.Mult, m);
                break;
            }
            case EffectType.XMultPerFlavorOnBelt:
            {
                int count = GetBeltFlavorCount(context);
                if (count > 0)
                {
                    float multiplier = Mathf.Pow(EffectiveAmount, count);
                    mult *= multiplier;
                    Emit(ScoreEvent.Kind.XMult, multiplier);
                }
                break;
            }

            case EffectType.AddAdditiveToDeck:
                if (template.additiveToAdd != null && (!hasAddedToDeck || !template.addToDeckOnlyOnce))
                {
                    context?.addToDeck?.Invoke(template.additiveToAdd);
                    hasAddedToDeck = true;
                    // Bypasses the zero-amount filter in Emit() above - this has no
                    // numeric contribution, but still needs a sound/notification.
                    emit?.Invoke(new ScoreEvent { kind = ScoreEvent.Kind.AddedToDeck, amount = 0f, anchor = anchor, sourceLabel = Name, addedAdditive = template.additiveToAdd });
                }
                break;

            case EffectType.AddRandomAdditiveToDeck:
            {
                var pick = RandomAdditivePicker.Pick(template.randomAdditivePool, template.filterByFlavor,
                                                      template.randomFlavorFilter, context?.allAdditivesPool);
                if (pick != null)
                {
                    context?.addToDeck?.Invoke(pick);
                    emit?.Invoke(new ScoreEvent { kind = ScoreEvent.Kind.AddedToDeck, amount = 0f, anchor = anchor, sourceLabel = Name, addedAdditive = pick });
                }
                break;
            }

            case EffectType.DeleteCreatorThenSelf:
                DeleteCreatorAndSelf(context, anchor, emit);
                break;

            case EffectType.DeleteRandomUnmodifiedFlavorThenSelf:
                DeleteRandomUnmodifiedAndSelf(context, anchor, emit);
                break;

            case EffectType.DeleteSpecificAdditiveThenSelf:
                DeleteSpecificAndSelf(context, anchor, emit);
                break;

            case EffectType.AddBeltSizePermanent:
            {
                int growth = Mathf.Max(1, Mathf.RoundToInt(EffectiveAmount));
                context?.addBeltSize?.Invoke(growth);
                emit?.Invoke(new ScoreEvent { kind = ScoreEvent.Kind.BeltSizeIncreased, amount = growth, anchor = anchor, sourceLabel = Name });
                break;
            }

            case EffectType.PassiveOnBelt:
                switch (template.beltPassivePayload)
                {
                    case BeltPassivePayloadType.Points:
                        points += EffectiveAmount;
                        Emit(ScoreEvent.Kind.Points, EffectiveAmount);
                        break;
                    case BeltPassivePayloadType.Mult:
                        mult += EffectiveAmount;
                        Emit(ScoreEvent.Kind.Mult, EffectiveAmount);
                        break;
                    case BeltPassivePayloadType.XMult:
                        mult *= EffectiveAmount;
                        Emit(ScoreEvent.Kind.XMult, EffectiveAmount);
                        break;
                }
                break;
        }

        // Permanent cross-type bonuses, applied on top of whatever this card's own
        // effect just did - independent of effectType, so buffs of either kind always
        // do something even when they land on a card whose native effect is the other kind.
        if (bonusPoints != 0f)
        {
            points += bonusPoints;
            Emit(ScoreEvent.Kind.Points, bonusPoints);
        }
        if (bonusMult != 0f)
        {
            mult += bonusMult;
            Emit(ScoreEvent.Kind.Mult, bonusMult);
        }

        // Optional value drift, also independent of effectType - stack with whatever
        // this card's main effect (and any bonuses above) just did. Positive values grow
        // the card over time; negative values shrink it (and can push EffectiveAmount
        // below 0, at which point it starts actively subtracting from the score).
        // useSeparatePlayedAmountChange lets "played" and "belt-passive" drift at
        // different rates - e.g. deteriorating on the belt without that same
        // deterioration also biting once the card is actually mixed into the soda.
        float appliedChange = (template.useSeparatePlayedAmountChange && isInCup)
            ? template.amountChangeWhenPlayed
            : template.amountChangePerUse;
        if (appliedChange != 0f)
            amountGrowth += appliedChange;

        if (template.deleteSelfAfterUse
            && (!template.deleteSelfOnlyWhenInCup || isInCup)
            && (!hasDeletedSelf || !template.deleteSelfOnlyOnce)
            && context?.removeFromDeck != null)
        {
            context.removeFromDeck.Invoke(this);
            hasDeletedSelf = true;
            emit?.Invoke(new ScoreEvent { kind = ScoreEvent.Kind.Deleted, amount = 0f, anchor = anchor, sourceLabel = Name });
        }
    }

    private int GetBeltFlavorCount(ScoringContext context)
    {
        if (context?.beltFlavorCounts == null || template.scalingFlavors == null) return 0;

        int total = 0;
        foreach (var flavor in template.scalingFlavors)
            if (context.beltFlavorCounts.TryGetValue(flavor, out int c)) total += c;
        return total;
    }

    private enum BuffTarget { Points, Mult, Retrigger }

    private void BuffRandomOwned(ScoringContext context, BuffTarget targetKind, RectTransform anchor, Action<ScoreEvent> emit)
    {
        if (context?.ownedAdditives == null || context.ownedAdditives.Count == 0) return;

        // Prefer buffing a different additive than this one; fall back to self
        // if this is the only additive the player owns.
        var others = context.ownedAdditives.Where(a => a != this).ToList();
        var candidates = others.Count > 0 ? others : context.ownedAdditives;

        var target = candidates[UnityEngine.Random.Range(0, candidates.Count)];
        switch (targetKind)
        {
            case BuffTarget.Points:
                target.bonusPoints += EffectiveAmount;
                break;
            case BuffTarget.Mult:
                target.bonusMult += EffectiveAmount;
                break;
            case BuffTarget.Retrigger:
                target.bonusRetrigger += Mathf.Max(1, Mathf.RoundToInt(EffectiveAmount));
                break;
        }

        emit?.Invoke(new ScoreEvent { kind = ScoreEvent.Kind.Buff, amount = EffectiveAmount, anchor = anchor, sourceLabel = Name });

        // Optional companion effect: this permanent-scaling card can ALSO create an
        // additive after use, reusing the same Add/Add-Random fields as the dedicated
        // add-to-deck effect types rather than needing a separate set of fields.
        // Gated behind createAdditiveChance - at 1 (default) this always happens, same
        // as before that field existed; lower values make it a probabilistic bonus.
        if (template.alsoCreateAdditiveAfterUse && UnityEngine.Random.value < template.createAdditiveChance)
        {
            var created = template.createRandomAdditive
                ? RandomAdditivePicker.Pick(template.randomAdditivePool, template.filterByFlavor,
                                             template.randomFlavorFilter, context.allAdditivesPool)
                : template.additiveToAdd;

            if (created != null)
            {
                context.addToDeck?.Invoke(created);
                emit?.Invoke(new ScoreEvent { kind = ScoreEvent.Kind.AddedToDeck, amount = 0f, anchor = anchor, sourceLabel = Name, addedAdditive = created });
            }
        }
    }

    /// <summary>
    /// Finds an owned additive whose AddAdditiveToDeck effect creates cards of
    /// template.targetCreatorFlavor, removes it from the owned collection, then
    /// removes this additive too. If no matching "creator" is found, only this
    /// additive deletes itself (nothing to remove alongside it).
    /// </summary>
    private void DeleteCreatorAndSelf(ScoringContext context, RectTransform anchor, Action<ScoreEvent> emit)
    {
        if (context?.ownedAdditives == null || context.removeFromDeck == null) return;

        var creator = context.ownedAdditives.FirstOrDefault(a =>
            a != this &&
            a.template != null &&
            a.template.effectType == EffectType.AddAdditiveToDeck &&
            a.template.additiveToAdd != null &&
            a.template.additiveToAdd.HasFlavor(template.targetCreatorFlavor));

        if (creator != null)
        {
            context.removeFromDeck.Invoke(creator);
            emit?.Invoke(new ScoreEvent { kind = ScoreEvent.Kind.Deleted, amount = 0f, anchor = anchor, sourceLabel = creator.Name });
        }

        context.removeFromDeck.Invoke(this);
        emit?.Invoke(new ScoreEvent { kind = ScoreEvent.Kind.Deleted, amount = 0f, anchor = anchor, sourceLabel = Name });
    }

    /// <summary>
    /// Picks a random unmodified (IsUnmodified) owned additive matching
    /// template.deleteFilterMode - any flavor, a specific flavor, additives with
    /// NO flavor of their own, or a curated pool - removes it, then removes this
    /// additive too. If nothing eligible is found, only this additive deletes itself.
    /// </summary>
    private void DeleteRandomUnmodifiedAndSelf(ScoringContext context, RectTransform anchor, Action<ScoreEvent> emit)
    {
        if (context?.ownedAdditives == null || context.removeFromDeck == null) return;

        IEnumerable<AdditiveInstance> candidates = context.ownedAdditives.Where(a => a != this && a.IsUnmodified);

        switch (template.deleteFilterMode)
        {
            case DeleteFilterMode.SpecificFlavor:
                candidates = candidates.Where(a => a.HasFlavor(template.targetDeleteFlavor));
                break;
            case DeleteFilterMode.NoFlavor:
                candidates = candidates.Where(a => a.Flavors == null || a.Flavors.Count == 0);
                break;
            case DeleteFilterMode.FromPool:
                var pool = template.deletePool != null ? new HashSet<AdditiveData>(template.deletePool) : null;
                candidates = candidates.Where(a => pool != null && pool.Contains(a.template));
                break;
            // AnyFlavor: no extra filter beyond "unmodified and not this card".
        }

        var candidateList = candidates.ToList();

        if (candidateList.Count > 0)
        {
            var target = PickWeightedByCreatorPriority(candidateList);
            context.removeFromDeck.Invoke(target);
            emit?.Invoke(new ScoreEvent { kind = ScoreEvent.Kind.Deleted, amount = 0f, anchor = anchor, sourceLabel = target.Name });
        }

        context.removeFromDeck.Invoke(this);
        emit?.Invoke(new ScoreEvent { kind = ScoreEvent.Kind.Deleted, amount = 0f, anchor = anchor, sourceLabel = Name });
    }

    private static bool CreatesOtherAdditives(AdditiveInstance a) =>
        a.template != null && (a.template.effectType == EffectType.AddAdditiveToDeck || a.template.effectType == EffectType.AddRandomAdditiveToDeck);

    /// <summary>
    /// Weighted (not exclusive) random pick: candidates that themselves create other
    /// additives (AddAdditiveToDeck/AddRandomAdditiveToDeck) are template.creatorPriorityWeight
    /// times more likely to be chosen than a normal candidate, but every eligible candidate
    /// keeps a real (if smaller) chance - this is a "claw" prioritizing creators heavily
    /// without ever fully ruling out anything else.
    /// </summary>
    private AdditiveInstance PickWeightedByCreatorPriority(List<AdditiveInstance> candidates)
    {
        float weightPerCreator = Mathf.Max(0.01f, template.creatorPriorityWeight);
        float totalWeight = 0f;
        var weights = new float[candidates.Count];

        for (int i = 0; i < candidates.Count; i++)
        {
            weights[i] = CreatesOtherAdditives(candidates[i]) ? weightPerCreator : 1f;
            totalWeight += weights[i];
        }

        float roll = UnityEngine.Random.value * totalWeight;
        float cumulative = 0f;
        for (int i = 0; i < candidates.Count; i++)
        {
            cumulative += weights[i];
            if (roll <= cumulative) return candidates[i];
        }
        return candidates[candidates.Count - 1];
    }

    /// <summary>
    /// Removes one owned additive matching template.additiveToDelete's template exactly
    /// (any flavor, buffed or not), then removes this additive too. If you don't own a
    /// match, only this additive deletes itself.
    /// </summary>
    private void DeleteSpecificAndSelf(ScoringContext context, RectTransform anchor, Action<ScoreEvent> emit)
    {
        if (context?.ownedAdditives == null || context.removeFromDeck == null) return;

        if (template.additiveToDelete != null)
        {
            var match = context.ownedAdditives.FirstOrDefault(a => a != this && a.template == template.additiveToDelete);
            if (match != null)
            {
                context.removeFromDeck.Invoke(match);
                emit?.Invoke(new ScoreEvent { kind = ScoreEvent.Kind.Deleted, amount = 0f, anchor = anchor, sourceLabel = match.Name });
            }
        }

        context.removeFromDeck.Invoke(this);
        emit?.Invoke(new ScoreEvent { kind = ScoreEvent.Kind.Deleted, amount = 0f, anchor = anchor, sourceLabel = Name });
    }
}