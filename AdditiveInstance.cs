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

    // Permanently increases how many random OTHER owned additives this instance's OWN
    // Buff* effect (BuffRandomAdditivePoints/Mult/Retrigger) targets each time it fires -
    // 0 means the default of 1. Written to by EffectType.BuffRandomBufferTargetCount
    // landing on this instance (see AmplifyRandomBuffer below); does nothing on an
    // instance whose effectType isn't one of the three Buff* types.
    public int extraBuffTargets;

    // Fractional carry-over toward the NEXT extraBuffTargets increment - lets a
    // BuffRandomBufferTargetCount additive with amount < 1 (e.g. 0.5) accumulate
    // partial progress across multiple fires instead of instantly granting a whole
    // extra target on the very first hit. See AmplifyRandomBuffer below for how this
    // is consumed. Always in [0, 1) between fires - a whole number is immediately
    // converted into extraBuffTargets the moment it's crossed, never left sitting here.
    public float buffTargetProgress;

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
    public bool IsUnmodified => bonusPoints == 0f && bonusMult == 0f && bonusRetrigger == 0 &&
                                 extraBuffTargets == 0 && buffTargetProgress == 0f;

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
            case EffectType.BuffRandomBufferTargetCount:
                AmplifyRandomBuffer(context, anchor, emit);
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

            case EffectType.AddRandomClawToDeck:
            {
                var claw = ClawPicker.Pick(template.clawPool, context?.ownedAdditives);
                if (claw != null)
                {
                    context?.addToDeck?.Invoke(claw);
                    emit?.Invoke(new ScoreEvent { kind = ScoreEvent.Kind.AddedToDeck, amount = 0f, anchor = anchor, sourceLabel = Name, addedAdditive = claw });
                }
                else
                {
                    // Makes the most common misconfigurations visible instead of the effect
                    // just quietly doing nothing every fire with no feedback anywhere - same
                    // reasoning as the combo AddAdditiveToDeck warning in SodaScoringManager.
                    string reason;
                    if (template.clawPool == null || template.clawPool.Count == 0)
                    {
                        reason = "clawPool is empty";
                    }
                    else if (template.clawPool.TrueForAll(e => e.additive == null || e.weight <= 0f))
                    {
                        reason = "every entry in clawPool has no additive assigned or a weight of 0 (a NEW entry " +
                                 "added via the list's own \"+\" button defaults to weight 1, but one added via the " +
                                 "plain Unity list/array \"Size\" field defaults to weight 0 - double check each " +
                                 "entry's weight)";
                    }
                    else
                    {
                        reason = "every usable (weight > 0) entry is set to Requires Owned Flavor, and none of " +
                                  "their Match Flavor values are owned yet - either own one, set an entry's Match " +
                                  "Flavor to something you already own, or switch an entry's Eligibility to " +
                                  "Always In Pool";
                    }
                    Debug.LogWarning($"[AdditiveInstance] '{Name}' fired AddRandomClawToDeck but added nothing: {reason}.");
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

            case EffectType.DeletePlayerChosenThenSelf:
                RequestPlayerChosenDeletion(context, anchor, emit);
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

    /// <summary>
    /// Waste-flavored additives are exempt from ever being a buff target - excluded from
    /// every random-buff selection below (BuffRandomOwned's points/mult/retrigger targets
    /// AND AmplifyRandomBuffer's buffer-to-amplify target), no matter how few other
    /// candidates are owned. An all-Waste (or Waste-plus-self) collection just makes the
    /// buff effect a no-op for that fire instead of landing on a Waste additive anyway.
    /// </summary>
    private static bool CanBeBuffed(AdditiveInstance a) => a != null && !a.HasFlavor(FlavorType.Waste);

    private void BuffRandomOwned(ScoringContext context, BuffTarget targetKind, RectTransform anchor, Action<ScoreEvent> emit)
    {
        if (context?.ownedAdditives == null || context.ownedAdditives.Count == 0) return;

        // Prefer buffing a different additive than this one; fall back to self only if
        // this is the only eligible (non-Waste) additive the player owns.
        var others = context.ownedAdditives.Where(a => a != this && CanBeBuffed(a)).ToList();
        var candidates = others.Count > 0 ? others : (CanBeBuffed(this) ? new List<AdditiveInstance> { this } : null);
        if (candidates == null || candidates.Count == 0) return;

        // Normally buffs exactly 1 target - extraBuffTargets (permanently added by
        // EffectType.BuffRandomBufferTargetCount landing on THIS instance, see
        // AmplifyRandomBuffer below) raises that count, e.g. 1 -> 2 additives buffed
        // per fire. Clamped to how many candidates actually exist and picked without
        // repeats, so a small owned collection never buffs the same additive twice
        // in one fire just because extraBuffTargets is high.
        int targetCount = Mathf.Clamp(1 + extraBuffTargets, 1, candidates.Count);
        var targets = candidates.OrderBy(_ => UnityEngine.Random.value).Take(targetCount);

        foreach (var target in targets)
        {
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
        }

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

    /// <summary>True for the three effect types BuffRandomBufferTargetCount is allowed to amplify.</summary>
    private static bool IsBuffer(AdditiveInstance a) =>
        a?.template != null &&
        (a.template.effectType == EffectType.BuffRandomAdditivePoints ||
         a.template.effectType == EffectType.BuffRandomAdditiveMult ||
         a.template.effectType == EffectType.BuffRandomAdditiveRetrigger);

    /// <summary>
    /// EffectType.BuffRandomBufferTargetCount: finds a random owned additive that is
    /// itself a "buffer" (BuffRandomAdditivePoints/Mult/Retrigger) and permanently
    /// increases how many additives IT buffs each time it fires, via extraBuffTargets
    /// (read by BuffRandomOwned above). Prefers a different buffer than this one; falls
    /// back to amplifying itself only if this additive is ALSO a buffer and no other
    /// buffer is owned. If no eligible buffer exists at all, this is a silent no-op for
    /// this fire - same as the other "no eligible target" effects elsewhere in this file.
    ///
    /// EffectiveAmount is NOT rounded up to a minimum of 1 here (unlike the retrigger
    /// buff, say) - an amount below 1 instead accumulates as fractional progress on the
    /// TARGET's buffTargetProgress across repeated fires, only converting into an actual
    /// extraBuffTargets increment once that progress crosses a whole number. E.g. 0.5
    /// amount needs 2 fires landing on the same target to grant +1 extra target, 0.33
    /// needs 3, 1.5 grants +1 immediately and leaves 0.5 progress toward the next, etc.
    /// An amount of 1+ behaves exactly as before (always grants at least +1 the same fire).
    /// </summary>
    private void AmplifyRandomBuffer(ScoringContext context, RectTransform anchor, Action<ScoreEvent> emit)
    {
        if (context?.ownedAdditives == null || context.ownedAdditives.Count == 0) return;

        var otherBuffers = context.ownedAdditives.Where(a => a != this && IsBuffer(a) && CanBeBuffed(a)).ToList();
        List<AdditiveInstance> candidates = otherBuffers.Count > 0
            ? otherBuffers
            : (IsBuffer(this) && CanBeBuffed(this) ? new List<AdditiveInstance> { this } : null);

        if (candidates == null || candidates.Count == 0) return;

        var target = candidates[UnityEngine.Random.Range(0, candidates.Count)];

        target.buffTargetProgress += Mathf.Max(0f, EffectiveAmount);
        int wholeTargetsGained = Mathf.FloorToInt(target.buffTargetProgress);

        if (wholeTargetsGained <= 0)
        {
            // Progress accumulated (e.g. 0.5 -> 0.5) but not enough yet to grant a whole
            // extra target this fire - no score event, matching the silent-no-op
            // convention used elsewhere for a fire that changes nothing observable.
            return;
        }

        target.buffTargetProgress -= wholeTargetsGained; // keep only the leftover fractional carry (always lands back in [0, 1))
        target.extraBuffTargets += wholeTargetsGained;

        emit?.Invoke(new ScoreEvent { kind = ScoreEvent.Kind.Buff, amount = wholeTargetsGained, anchor = anchor, sourceLabel = Name });
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

    /// <summary>
    /// Doesn't pick a deletion target itself like the other Delete*ThenSelf variants -
    /// instead asks context.requestPlayerDeletion to queue a PLAYER-chosen deletion,
    /// resolved later (once scoring and its playback finish) via
    /// DeckStatsPanel.ShowForDeletion - see RoundManager's pending-deletion queue. This
    /// additive still deletes ITSELF immediately, synchronously, exactly like the other
    /// Delete*ThenSelf variants; only the separate, additional deletion is deferred to
    /// let the player choose. If context.requestPlayerDeletion isn't wired, only this
    /// additive deletes itself - same "nothing else happens" fallback as the others when
    /// there's nothing eligible/available.
    /// </summary>
    private void RequestPlayerChosenDeletion(ScoringContext context, RectTransform anchor, Action<ScoreEvent> emit)
    {
        if (context?.removeFromDeck == null) return;

        context.requestPlayerDeletion?.Invoke(this);

        context.removeFromDeck.Invoke(this);
        emit?.Invoke(new ScoreEvent { kind = ScoreEvent.Kind.Deleted, amount = 0f, anchor = anchor, sourceLabel = Name });
    }
}