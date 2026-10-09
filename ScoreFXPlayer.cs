using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using TMPro;
using UnityEngine;

/// <summary>
/// Steps through a soda's ScoreEvents (in the order SodaScoringManager produced
/// them) one at a time: spawns a floating popup at each event's anchor and plays
/// a sound - a different clip per category (points / mult / retrigger / added to
/// deck / permanent buff) - with pitch climbing a little across the whole sequence
/// regardless of which clip is chosen. Wire an instance of this into
/// RoundManager.scoreFX; RoundManager waits for PlaySequence() to finish before
/// resolving pass/fail, so the reveal always plays out before the round moves on.
///
/// After speedUpAfterEvents events in a mix, the delay between events and the
/// score/points/mult bounce durations shrink exponentially toward minSpeedFactor
/// of their normal values (approaching it, never reaching it).
/// </summary>
public class ScoreFXPlayer : MonoBehaviour
{
    [Header("Floating Text")]
    public FloatingScoreText floatingTextPrefab;
    [Tooltip("Parent the floating text spawns under - usually a top-level Canvas RectTransform so it renders above cards.")]
    public RectTransform textLayer;

    [Header("Colors")]
    public Color pointsColor = new Color(0.4f, 0.8f, 1f);    // blue-ish, chips-style
    public Color multColor = new Color(1f, 0.4f, 0.4f);      // red-ish, mult-style
    public Color buffColor = new Color(1f, 0.85f, 0.3f);     // gold - permanent upgrade
    public Color addedToDeckColor = new Color(0.6f, 1f, 0.6f); // green - new additive gained
    public Color comboColor = new Color(0.75f, 0.4f, 1f);    // purple - a FlavorComboRule triggered
    public Color deletedColor = new Color(0.6f, 0.6f, 0.6f); // gray - an additive was removed from the collection
    public Color beltSizeColor = new Color(0.3f, 0.9f, 0.9f); // cyan - permanent belt-size growth
    public Color retriggerAllColor = new Color(1f, 0.6f, 0.2f); // orange - "everything fires again!"
    public Color cupCapacityColor = new Color(0.4f, 1f, 0.8f);  // mint - permanent cup-capacity growth
    public Color heatReducedColor = new Color(0.55f, 0.8f, 1f); // icy blue - flavor heat cooled off

    [Header("Timing")]
    [Tooltip("Delay between each scoring event's reveal (before any speed-up).")]
    public float delayBetweenEvents = 0.12f;

    [Header("Sound - one clip per category")]
    public AudioSource audioSource;
    public AudioClip pointsClip;
    public AudioClip multClip;
    [Tooltip("Plays instead of the points/mult clip whenever a fire happened because of a retrigger " +
             "(2nd+ time an effect fired this scoring pass), regardless of whether it added points or mult.")]
    public AudioClip retriggerClip;
    public AudioClip addedToDeckClip;
    public AudioClip buffClip;
    [Tooltip("Plays instead of the points/mult clip whenever this contribution came from a FlavorComboRule.")]
    public AudioClip comboClip;
    public AudioClip deletedClip;
    public AudioClip beltSizeClip;
    [Tooltip("Plays when a RetriggerAllOtherAdditives card announces it's about to re-fire the rest of the cup. " +
             "Falls back to retriggerClip, then fallbackClip, if unset.")]
    public AudioClip retriggerAllClip;
    [Tooltip("Plays when an AddCupCapacityPermanent card grows the bottle. Falls back to beltSizeClip, then fallbackClip, if unset.")]
    public AudioClip cupCapacityClip;
    [Tooltip("Plays when a combo levels up. Falls back to comboClip, then fallbackClip, if unset.")]
    public AudioClip comboLevelUpClip;
    [Tooltip("Used if the category-specific clip above is left empty, so nothing plays silently by default.")]
    public AudioClip fallbackClip;

    [Header("Pitch")]
    [Tooltip("Pitch used for the very first event in a playback.")]
    public float basePitch = 0.9f;
    [Tooltip("How much pitch climbs per successive event, regardless of category.")]
    public float pitchStepPerEvent = 0.05f;
    public float maxPitch = 2.2f;
    public float eventsThisMix = 0.0f;

    [Header("Score Display Sync (optional)")]
    [Tooltip("Counted up event by event during playback (via each ScoreEvent.runningTotal), " +
             "with a small bounce in sync with the scored card's jump. Point this at the same " +
             "TMP_Text as SodaScoringManager.currentScoreText if you want them to match.")]
    public TMP_Text runningScoreText;
    public string scoreFormat = "N0";
    [Tooltip("How big the score text's punch-scale bounce is (1 = no bounce).")]
    public float scoreBouncePunchScale = 1.25f;
    [Tooltip("Score bounce length (before any speed-up).")]
    public float scoreBounceDuration = 0.2f;

    [Header("Points / Mult Display (optional)")]
    [Tooltip("Shows the soda's current POINTS while it's being scored. Bounces every time points are added.")]
    public TMP_Text pointsText;
    [Tooltip("Shows the soda's current MULT while it's being scored. Bounces every time mult is added or multiplied.")]
    public TMP_Text multText;
    [Tooltip("C# format string for pointsText - {0} is the points value, e.g. \"{0:N0}\" or \"{0:N0} pts\".")]
    public string pointsTextFormat = "{0:N0}";
    [Tooltip("C# format string for multText - {0} is the mult value, e.g. \"{0:0.##}\" or \"x{0:0.##}\".")]
    public string multTextFormat = "{0:0.##}";
    [Tooltip("Scoring treats mult below 1 as 1 (total = points x max(mult, 1)). On = the mult text shows that " +
             "effective value (so an empty soda reads 0 x 1), off = shows the raw mult (starts at 0).")]
    public bool showEffectiveMult = true;
    [Tooltip("How big the points/mult bounce is (1 = no bounce).")]
    public float statBouncePunchScale = 1.3f;
    [Tooltip("Points/mult bounce length (before any speed-up).")]
    public float statBounceDuration = 0.18f;
    [Tooltip("Optional - briefly tints the text this color at the peak of its bounce, then fades back to its normal color. " +
             "Leave alpha at 0 to turn the flash off.")]
    public Color pointsFlashColor = new Color(0.4f, 0.8f, 1f, 0f);
    public Color multFlashColor = new Color(1f, 0.4f, 0.4f, 0f);
    [Tooltip("Resets both texts to 0 once the whole sequence has finished playing. Off = they keep showing the " +
             "last soda's final points/mult until the next brew starts.")]
    public bool resetStatsAfterSequence = false;
    [Tooltip("How long to wait after the last event before resetting, if Reset Stats After Sequence is on.")]
    public float resetStatsDelay = 0.6f;

    [Header("Speed Up")]
    [Tooltip("How many events in a mix play at normal speed before the speed-up kicks in.")]
    public int speedUpAfterEvents = 13;
    [Tooltip("How quickly timing shrinks once the speed-up starts. Higher = faster ramp (0.1 - 0.3 is a good range).")]
    public float speedUpRate = 0.15f;
    [Tooltip("Fastest timing allowed, as a fraction of normal (0.25 = 4x speed). Timing approaches this but never reaches it.")]
    [Range(0.01f, 1f)]
    public float minSpeedFactor = 0.25f;
    [Tooltip("Also play sounds faster as the sequence speeds up. In Unity, faster playback also raises pitch.")]
    public bool speedUpSounds = true;
    [Tooltip("How closely sound speed follows the visual speed-up. 1 = matches it exactly, 0.5 = halfway, 0 = no change.")]
    [Range(0f, 1f)]
    public float soundSpeedUpAmount = 0.5f;
    [Tooltip("Highest final pitch a sound can play at, after the per-event climb and the speed-up are combined.")]
    public float maxSoundPitch = 3f;

    // Current multiplier applied to delay and bounce durations (1 = normal speed).
    private float _speedFactor = 1f;

    private Coroutine _scoreBounceRoutine;
    private readonly Dictionary<TMP_Text, Coroutine> _statBounces = new Dictionary<TMP_Text, Coroutine>();
    private readonly Dictionary<TMP_Text, Vector3> _baseScales = new Dictionary<TMP_Text, Vector3>();
    private readonly Dictionary<TMP_Text, Color> _baseColors = new Dictionary<TMP_Text, Color>();

    private void Start()
    {
        SetStats(0f, 0f); // show "0 x 1" (or your formats' equivalent) from the start rather than placeholder text
    }

    /// <summary>
    /// 1 for the first speedUpAfterEvents events, then decays exponentially toward
    /// minSpeedFactor without ever reaching it.
    /// </summary>
    private float SpeedFactorFor(float eventCount)
    {
        float n = Mathf.Max(0f, eventCount - speedUpAfterEvents);
        float factor = minSpeedFactor + (1f - minSpeedFactor) * Mathf.Exp(-speedUpRate * n);
        return Mathf.Max(factor, minSpeedFactor); // guards against float precision issues
    }

    /// <summary>Directly sets the points/mult texts without bouncing, e.g. to clear them between rounds.</summary>
    public void SetStats(double points, double mult)
    {
        if (pointsText != null) pointsText.text = FormatStat(pointsTextFormat, points);
        if (multText != null) multText.text = FormatStat(multTextFormat, showEffectiveMult ? System.Math.Max(mult, 1.0) : mult);
    }

    private static string FormatStat(string format, double value)
    {
        // Huge values: swap the number for "1.23e8" style but keep any text around it in the format
        // (e.g. "x{0:0.##}" -> "x1.23e8").
        if (System.Math.Abs(value) >= ScoreFormat.BigThreshold || double.IsInfinity(value))
        {
            try { return string.Format(CultureInfo.InvariantCulture, StripNumberFormat(format), ScoreFormat.Big(value)); }
            catch (System.FormatException) { return ScoreFormat.Big(value); }
        }
        try { return string.Format(CultureInfo.InvariantCulture, format, value); }
        catch (System.FormatException) { return value.ToString("0.##", CultureInfo.InvariantCulture); }
    }

    /// <summary>Turns "{0:N0} pts" into "{0} pts" so a pre-formatted string can be dropped in.</summary>
    private static string StripNumberFormat(string format) =>
        System.Text.RegularExpressions.Regex.Replace(format ?? "{0}", @"\{0(:[^}]*)?\}", "{0}");

    /// <summary>
    /// Plays every event in order, yielding delayBetweenEvents between each. Safe to
    /// call with an empty/null list. baselineScore is added to every event's
    /// runningTotal for display purposes - pass in the round's score from BEFORE this
    /// attempt if scores accumulate across brews, so the display counts up from where
    /// it already was rather than resetting to zero each attempt.
    /// </summary>
    public IEnumerator PlaySequence(List<ScoreEvent> events, double baselineScore = 0.0)
    {
        if (events == null) yield break;

        if (runningScoreText != null) runningScoreText.text = ScoreFormat.Big(baselineScore, scoreFormat);
        SetStats(0f, 0f); // every soda starts from 0 points / 0 mult

        // Each mix starts back at normal speed. Remove these two lines if you want
        // the speed-up to carry over from one sequence to the next.
        eventsThisMix = 0;
        _speedFactor = 1f;

        float pitch = basePitch;
        foreach (var ev in events)
        {
            eventsThisMix++;
            _speedFactor = SpeedFactorFor(eventsThisMix);

            SpawnFloatingText(ev);
            PlaySound(ev, pitch);
            TriggerCardJump(ev);
            UpdateRunningScore(baselineScore + ev.runningTotal);
            UpdateStats(ev);
            pitch = Mathf.Min(pitch + pitchStepPerEvent, maxPitch);
            yield return new WaitForSeconds(delayBetweenEvents * _speedFactor);
        }

        if (resetStatsAfterSequence)
        {
            yield return new WaitForSeconds(resetStatsDelay);
            SetStats(0f, 0f);
            eventsThisMix = 0;
        }
    }

    /// <summary>
    /// Updates the points/mult texts to this event's running values, and bounces whichever one
    /// this event actually added to (Points events bounce points; Mult and XMult bounce mult).
    /// Events that don't touch either (buffs, deletions, added-to-deck...) just leave them be.
    /// </summary>
    private void UpdateStats(ScoreEvent ev)
    {
        bool isPoints = ev.kind == ScoreEvent.Kind.Points;
        bool isMult = ev.kind == ScoreEvent.Kind.Mult || ev.kind == ScoreEvent.Kind.XMult;
        if (!isPoints && !isMult) return;

        SetStats(ev.runningPoints, ev.runningMult);

        if (isPoints && pointsText != null) BounceStat(pointsText, pointsFlashColor);
        if (isMult && multText != null) BounceStat(multText, multFlashColor);
    }

    private void BounceStat(TMP_Text text, Color flashColor)
    {
        // Remember the text's own resting scale/color the FIRST time it bounces, so the
        // bounce always returns to exactly how you set it up in the scene - even when a
        // new bounce interrupts one that's still mid-punch.
        if (!_baseScales.ContainsKey(text)) _baseScales[text] = text.rectTransform.localScale;
        if (!_baseColors.ContainsKey(text)) _baseColors[text] = text.color;

        if (_statBounces.TryGetValue(text, out var running) && running != null) StopCoroutine(running);
        _statBounces[text] = StartCoroutine(BounceStatRoutine(text, flashColor, statBounceDuration * _speedFactor));
    }

    private IEnumerator BounceStatRoutine(TMP_Text text, Color flashColor, float duration)
    {
        var rect = text.rectTransform;
        Vector3 baseScale = _baseScales[text];
        Color baseColor = _baseColors[text];
        bool flash = flashColor.a > 0f;

        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            float p = Mathf.Clamp01(t / duration);
            float arc = Mathf.Sin(p * Mathf.PI); // 0 -> 1 -> 0
            rect.localScale = baseScale * (1f + arc * (statBouncePunchScale - 1f));
            if (flash) text.color = Color.Lerp(baseColor, new Color(flashColor.r, flashColor.g, flashColor.b, baseColor.a), arc);
            yield return null;
        }

        rect.localScale = baseScale;
        if (flash) text.color = baseColor;
        _statBounces[text] = null;
    }

    /// <summary>Updates the running score text to match this event's post-contribution total, with a punch-scale bounce.</summary>
    private void UpdateRunningScore(double total)
    {
        if (runningScoreText == null) return;

        runningScoreText.text = ScoreFormat.Big(total, scoreFormat);

        if (_scoreBounceRoutine != null) StopCoroutine(_scoreBounceRoutine);
        _scoreBounceRoutine = StartCoroutine(BounceScoreText(scoreBounceDuration * _speedFactor));
    }

    private IEnumerator BounceScoreText(float duration)
    {
        var rect = runningScoreText.rectTransform;
        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            float p = Mathf.Clamp01(t / duration);
            // Same sine-arc shape as AdditiveCard's jump, but on scale instead of
            // position - punches up then eases back to normal size.
            float scale = 1f + Mathf.Sin(p * Mathf.PI) * (scoreBouncePunchScale - 1f);
            rect.localScale = Vector3.one * scale;
            yield return null;
        }
        rect.localScale = Vector3.one;
        _scoreBounceRoutine = null;
    }

    /// <summary>Hops the specific card this event came from, if it has one (combo/syrup/global bonuses don't).</summary>
    private void TriggerCardJump(ScoreEvent ev)
    {
        if (ev.anchor == null) return;
        var card = ev.anchor.GetComponent<AdditiveCard>();
        if (card != null) card.PlayScoreJump();
    }

    private void SpawnFloatingText(ScoreEvent ev)
    {
        if (floatingTextPrefab == null) return;

        RectTransform parent = textLayer != null ? textLayer : (RectTransform)transform;
        var text = Instantiate(floatingTextPrefab, parent);

        // World-space position copy - works correctly regardless of Canvas render
        // mode (Overlay/Camera), no camera reference needed. Falls back to the
        // parent's own position if this event had no specific card anchor.
        RectTransform anchor = ev.anchor != null ? ev.anchor : parent;
        text.Rect.position = anchor.position;

        text.Play(FormatLabel(ev), ColorFor(ev), ev.addedAdditive != null ? ev.addedAdditive.icon : null);
    }

    private string FormatLabel(ScoreEvent ev)
    {
        string prefix = ev.isCombo && !string.IsNullOrEmpty(ev.sourceLabel) ? $"{ev.sourceLabel}! " : "";
        switch (ev.kind)
        {
            case ScoreEvent.Kind.Points:      return $"{prefix}{ScoreFormat.Signed(ev.amount)}";
            case ScoreEvent.Kind.Mult:        return $"{prefix}{ScoreFormat.Signed(ev.amount)} Mult";
            case ScoreEvent.Kind.XMult:       return $"{prefix}x{ev.amount:0.#}";
            case ScoreEvent.Kind.Buff:        return "Buffed!";
            case ScoreEvent.Kind.AddedToDeck: return ev.addedAdditive != null ? $"New: {ev.addedAdditive.additiveName}!" : "New Additive!";
            case ScoreEvent.Kind.Deleted:     return $"Deleted {ev.sourceLabel}!";
            case ScoreEvent.Kind.BeltSizeIncreased: return $"Belt +{ev.amount:0}!"; // always positive - AddBeltSizePermanent clamps growth to >= 1
            case ScoreEvent.Kind.RetriggerAll: return ev.amount > 1f ? $"Retrigger All x{ev.amount:0}!" : "Retrigger All!";
            case ScoreEvent.Kind.CupCapacityIncreased: return $"Cup +{ev.amount:0}!";
            case ScoreEvent.Kind.ComboLevelUp: return $"{ev.sourceLabel} Lv {ev.amount:0}!";
            case ScoreEvent.Kind.HeatReduced: return ev.amount >= 0.999f ? "Heat Reset!" : $"Heat -{ev.amount * 100f:0}%!";
            case ScoreEvent.Kind.AttemptAdded: return $"{prefix}Attempts +{ev.amount:0}!";
            case ScoreEvent.Kind.Upgrade: return $"{prefix}{ev.customLabel}!";
            default: return $"{prefix}{ev.amount:0.#}";
        }
    }

    private Color ColorFor(ScoreEvent ev)
    {
        if (ev.isCombo) return comboColor;
        switch (ev.kind)
        {
            case ScoreEvent.Kind.Points: return pointsColor;
            case ScoreEvent.Kind.Buff: return buffColor;
            case ScoreEvent.Kind.AddedToDeck: return addedToDeckColor;
            case ScoreEvent.Kind.Deleted: return deletedColor;
            case ScoreEvent.Kind.BeltSizeIncreased: return beltSizeColor;
            case ScoreEvent.Kind.RetriggerAll: return retriggerAllColor;
            case ScoreEvent.Kind.CupCapacityIncreased: return cupCapacityColor;
            case ScoreEvent.Kind.HeatReduced: return heatReducedColor;
            case ScoreEvent.Kind.AttemptAdded: return cupCapacityColor;
            case ScoreEvent.Kind.Upgrade: return buffColor;
            default: return multColor; // Mult or XMult
        }
    }

    private AudioClip ClipFor(ScoreEvent ev)
    {
        if (ev.kind == ScoreEvent.Kind.ComboLevelUp)
            return comboLevelUpClip != null ? comboLevelUpClip : (comboClip != null ? comboClip : fallbackClip);

        AudioClip clip = ev.isCombo ? comboClip : ev.kind switch
        {
            ScoreEvent.Kind.Buff => buffClip,
            ScoreEvent.Kind.AddedToDeck => addedToDeckClip,
            ScoreEvent.Kind.Deleted => deletedClip,
            ScoreEvent.Kind.BeltSizeIncreased => beltSizeClip,
            ScoreEvent.Kind.RetriggerAll => retriggerAllClip != null ? retriggerAllClip : retriggerClip,
            ScoreEvent.Kind.CupCapacityIncreased => cupCapacityClip != null ? cupCapacityClip : beltSizeClip,
            ScoreEvent.Kind.AttemptAdded => cupCapacityClip != null ? cupCapacityClip : beltSizeClip,
            ScoreEvent.Kind.Upgrade => buffClip,
            ScoreEvent.Kind.Points => ev.isRetrigger ? retriggerClip : pointsClip,
            _ => ev.isRetrigger ? retriggerClip : multClip, // Mult or XMult
        };
        return clip != null ? clip : fallbackClip;
    }

    private void PlaySound(ScoreEvent ev, float pitch)
    {
        var clip = ClipFor(ev);
        if (audioSource == null || clip == null) return;
        // PlayOneShot bakes in the source's pitch AT CALL TIME, so rapid successive
        // calls with different pitches layer correctly even if they overlap.
        float finalPitch = pitch;
        if (speedUpSounds)
        {
            // _speedFactor shrinks toward minSpeedFactor, so 1/_speedFactor is how many
            // times faster things are running (e.g. 0.25 -> 4x). Blend toward that.
            float soundSpeed = Mathf.Lerp(1f, 1f / _speedFactor, soundSpeedUpAmount);
            finalPitch = Mathf.Min(pitch * soundSpeed, maxSoundPitch);
        }
        audioSource.pitch = finalPitch;
        audioSource.PlayOneShot(clip);
    }
}