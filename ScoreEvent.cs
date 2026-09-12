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
    public enum Kind { Points, Mult, XMult }

    public Kind kind;
    public float amount;

    /// <summary>Where to spawn the floating text from. Null falls back to a general anchor (e.g. the bottle).</summary>
    public RectTransform anchor;

    /// <summary>Additive/combo/syrup name, for debugging or richer popups later.</summary>
    public string sourceLabel;
}
