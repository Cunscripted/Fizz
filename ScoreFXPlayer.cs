using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Steps through a soda's ScoreEvents (in the order SodaScoringManager produced
/// them) one at a time: spawns a floating +N/xN popup at each event's anchor and
/// plays a short sound whose pitch climbs a little with every event, resetting
/// at the start of each new playback. Wire an instance of this into
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
    public Color pointsColor = new Color(0.4f, 0.8f, 1f);   // blue-ish, chips-style
    public Color multColor = new Color(1f, 0.4f, 0.4f);     // red-ish, mult-style

    [Header("Timing")]
    [Tooltip("Delay between each scoring event's reveal.")]
    public float delayBetweenEvents = 0.12f;

    [Header("Sound")]
    public AudioSource audioSource;
    public AudioClip scoreClip;
    [Tooltip("Pitch used for the very first event in a playback.")]
    public float basePitch = 0.9f;
    [Tooltip("How much pitch climbs per successive event.")]
    public float pitchStepPerEvent = 0.05f;
    public float maxPitch = 2.2f;

    /// <summary>Plays every event in order, yielding delayBetweenEvents between each. Safe to call with an empty/null list.</summary>
    public IEnumerator PlaySequence(List<ScoreEvent> events)
    {
        if (events == null) yield break;

        float pitch = basePitch;
        foreach (var ev in events)
        {
            SpawnFloatingText(ev);
            PlaySound(pitch);
            pitch = Mathf.Min(pitch + pitchStepPerEvent, maxPitch);
            yield return new WaitForSeconds(delayBetweenEvents);
        }
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

        text.Play(FormatLabel(ev), ColorFor(ev.kind));
    }

    private string FormatLabel(ScoreEvent ev)
    {
        switch (ev.kind)
        {
            case ScoreEvent.Kind.Points: return $"+{ev.amount:0.#}";
            case ScoreEvent.Kind.Mult:   return $"+{ev.amount:0.#} Mult";
            case ScoreEvent.Kind.XMult:  return $"x{ev.amount:0.#}";
            default: return ev.amount.ToString("0.#");
        }
    }

    private Color ColorFor(ScoreEvent.Kind kind)
    {
        return kind == ScoreEvent.Kind.Points ? pointsColor : multColor;
    }

    private void PlaySound(float pitch)
    {
        if (audioSource == null || scoreClip == null) return;
        // PlayOneShot bakes in the source's pitch AT CALL TIME, so rapid successive
        // calls with different pitches layer correctly even if they overlap.
        audioSource.pitch = pitch;
        audioSource.PlayOneShot(scoreClip);
    }
}
