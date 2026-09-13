using System.Collections;
using System.Collections.Generic;
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

    [Header("Timing")]
    [Tooltip("Delay between each scoring event's reveal.")]
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
    [Tooltip("Used if the category-specific clip above is left empty, so nothing plays silently by default.")]
    public AudioClip fallbackClip;

    [Header("Pitch")]
    [Tooltip("Pitch used for the very first event in a playback.")]
    public float basePitch = 0.9f;
    [Tooltip("How much pitch climbs per successive event, regardless of category.")]
    public float pitchStepPerEvent = 0.05f;
    public float maxPitch = 2.2f;

    [Header("Score Display Sync (optional)")]
    [Tooltip("Counted up event by event during playback (via each ScoreEvent.runningTotal), " +
             "with a small bounce in sync with the scored card's jump. Point this at the same " +
             "TMP_Text as SodaScoringManager.currentScoreText if you want them to match.")]
    public TMP_Text runningScoreText;
    public string scoreFormat = "N0";
    [Tooltip("How big the score text's punch-scale bounce is (1 = no bounce).")]
    public float scoreBouncePunchScale = 1.25f;
    public float scoreBounceDuration = 0.2f;

    private Coroutine _scoreBounceRoutine;

    /// <summary>
    /// Plays every event in order, yielding delayBetweenEvents between each. Safe to
    /// call with an empty/null list. baselineScore is added to every event's
    /// runningTotal for display purposes - pass in the round's score from BEFORE this
    /// attempt if scores accumulate across brews, so the display counts up from where
    /// it already was rather than resetting to zero each attempt.
    /// </summary>
    public IEnumerator PlaySequence(List<ScoreEvent> events, float baselineScore = 0f)
    {
        if (events == null) yield break;

        if (runningScoreText != null) runningScoreText.text = Mathf.RoundToInt(baselineScore).ToString(scoreFormat);

        float pitch = basePitch;
        foreach (var ev in events)
        {
            SpawnFloatingText(ev);
            PlaySound(ev, pitch);
            TriggerCardJump(ev);
            UpdateRunningScore(baselineScore + ev.runningTotal);
            pitch = Mathf.Min(pitch + pitchStepPerEvent, maxPitch);
            yield return new WaitForSeconds(delayBetweenEvents);
        }
    }

    /// <summary>Updates the running score text to match this event's post-contribution total, with a punch-scale bounce.</summary>
    private void UpdateRunningScore(float total)
    {
        if (runningScoreText == null) return;

        runningScoreText.text = Mathf.RoundToInt(total).ToString(scoreFormat);

        if (_scoreBounceRoutine != null) StopCoroutine(_scoreBounceRoutine);
        _scoreBounceRoutine = StartCoroutine(BounceScoreText());
    }

    private IEnumerator BounceScoreText()
    {
        var rect = runningScoreText.rectTransform;
        float t = 0f;
        while (t < scoreBounceDuration)
        {
            t += Time.deltaTime;
            float p = Mathf.Clamp01(t / scoreBounceDuration);
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
            default: return multColor; // Mult or XMult
        }
    }

    private AudioClip ClipFor(ScoreEvent ev)
    {
        AudioClip clip = ev.isCombo ? comboClip : ev.kind switch
        {
            ScoreEvent.Kind.Buff => buffClip,
            ScoreEvent.Kind.AddedToDeck => addedToDeckClip,
            ScoreEvent.Kind.Deleted => deletedClip,
            ScoreEvent.Kind.BeltSizeIncreased => beltSizeClip,
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
        audioSource.pitch = pitch;
        audioSource.PlayOneShot(clip);
    }
}