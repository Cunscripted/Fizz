using System.Collections;
using UnityEngine;
using TMPro;

/// <summary>
/// A single floating score popup ("+10", "+2 Mult", "x1.5") that rises and fades
/// out over a short duration, then destroys itself. Put this on a small UI prefab
/// with a RectTransform, a TMP_Text (TextMeshProUGUI) component, and a CanvasGroup
/// for the fade.
/// </summary>
[RequireComponent(typeof(RectTransform))]
public class FloatingScoreText : MonoBehaviour
{
    public TMP_Text label;
    public CanvasGroup canvasGroup;

    [Header("Motion")]
    public float riseDistance = 60f;
    public float duration = 0.8f;
    public AnimationCurve riseCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
    public AnimationCurve fadeCurve = AnimationCurve.Linear(0f, 1f, 1f, 0f);
    [Tooltip("Small random horizontal offset so overlapping popups don't sit exactly on top of each other.")]
    public float horizontalJitter = 12f;

    public RectTransform Rect { get; private set; }

    private void Awake()
    {
        Rect = GetComponent<RectTransform>();
        if (canvasGroup == null) canvasGroup = GetComponent<CanvasGroup>();
    }

    /// <summary>Call right after instantiating and positioning this popup.</summary>
    public void Play(string text, Color color)
    {
        if (label != null)
        {
            label.text = text;
            label.color = color;
        }
        StartCoroutine(Animate());
    }

    private IEnumerator Animate()
    {
        Vector2 start = Rect.anchoredPosition + new Vector2(Random.Range(-horizontalJitter, horizontalJitter), 0f);
        Rect.anchoredPosition = start;
        Vector2 end = start + Vector2.up * riseDistance;
        float t = 0f;

        while (t < duration)
        {
            t += Time.deltaTime;
            float p = Mathf.Clamp01(t / duration);
            Rect.anchoredPosition = Vector2.LerpUnclamped(start, end, riseCurve.Evaluate(p));
            if (canvasGroup != null) canvasGroup.alpha = fadeCurve.Evaluate(p);
            yield return null;
        }

        Destroy(gameObject);
    }
}