using UnityEngine;

/// <summary>
/// One individual contribution to a soda's score - a single additive fire, a
/// syrup bonus, a combo trigger, etc. SodaScoringManager emits a full ordered
/// list of these while computing the total, so a presentation layer (floating
/// text + escalating pitch audio) can play them back one at a time afterward,
/// completely decoupled from the actual (instant, deterministic) score math.
/// </summary>
public struct ScoreEvent
{
    public enum Kind
    {
        Points,
        Mult,
        XMult,
        Buff,          // BuffRandomAdditivePoints/Mult - permanently boosted another owned additive
        AddedToDeck,   // AddAdditiveToDeck - permanently added a new additive to the collection
        Deleted,       // DeleteCreatorThenSelf - an additive (the creator, or this one) was removed from the collection
        BeltSizeIncreased, // AddBeltSizePermanent - permanently grew how many additives are drawn onto the belt each round
        RetriggerAll,      // RetriggerAllOtherAdditives - announces that every other cup additive is about to fire again (amount = times each)
        CupCapacityIncreased, // AddCupCapacityPermanent - permanently grew how many additives the soda bottle can hold
        ComboLevelUp,         // a FlavorComboRule reached a new level (amount = the new level)
        HeatReduced,          // flavor popularity heat was cut (amount = fraction removed, 1 = reset)
        AttemptAdded,         // a combo permanently granted extra attempts per round (amount = how many)
        Upgrade               // a combo granted a permanent syrup-style rule - the popup shows customLabel
    }

    public Kind kind;
    public float amount;

    /// <summary>
    /// True if this fire happened because of a retrigger (the 2nd+ time an effect
    /// fired this scoring pass), rather than its normal first/base fire. Doesn't
    /// change what text/color shows (still the actual points/mult/xmult value),
    /// only which sound ScoreFXPlayer picks for it.
    /// </summary>
    public bool isRetrigger;

    /// <summary>
    /// True if this contribution came from a FlavorComboRule bonus (BonusPoints/
    /// BonusMult/BonusXMult), rather than an additive's own effect. ScoreFXPlayer
    /// gives these a distinct color/sound and prefixes the combo's name (from
    /// sourceLabel) onto the popup text.
    /// </summary>
    public bool isCombo;

    /// <summary>
    /// The soda's total (points * mult) immediately after this event's contribution
    /// was applied - lets a presentation layer count the score display up event by
    /// event during playback, in sync with each popup/jump, rather than only
    /// jumping straight to the final total.
    /// </summary>
    public double runningTotal;

    /// <summary>The soda's raw points immediately after this event - drives ScoreFXPlayer.pointsText.</summary>
    public double runningPoints;

    /// <summary>The soda's raw mult immediately after this event - drives ScoreFXPlayer.multText.</summary>
    public double runningMult;

    /// <summary>Where to spawn the floating text from. Null falls back to a general anchor (e.g. the bottle).</summary>
    public RectTransform anchor;

    /// <summary>Additive/combo/syrup name - shown directly in the popup text when isCombo is true.</summary>
    public string sourceLabel;

    /// <summary>
    /// Set only for AddedToDeck events - the actual additive template that got added,
    /// so ScoreFXPlayer can show its icon on the popup, not just a generic "New Additive!" text.
    /// </summary>
    public AdditiveData addedAdditive;

    /// <summary>Set only for Upgrade events - the short text to show, e.g. "Sour +5 Pts forever".</summary>
    public string customLabel;
}