using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// A single floating popup ("+10", "+2 Mult", "x1.5", or an added/removed additive's
/// icon + name) that rises and fades out over a short duration, then destroys itself.
/// Put this on a small UI prefab with a RectTransform, a TMP_Text (TextMeshProUGUI),
/// an optional Image for the icon variant, and a CanvasGroup for the fade.
/// </summary>
[RequireComponent(typeof(RectTransform))]
public class FloatingScoreText : MonoBehaviour
{
    public TMP_Text label;
    public CanvasGroup canvasGroup;
    [Tooltip("Optional - shown only when Play() is given a non-null icon (e.g. an added/removed additive's sprite).")]
    public Image iconImage;

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

    /// <summary>Call right after instantiating and positioning this popup. icon is optional - leave null for plain text popups.</summary>
    public void Play(string text, Color color, Sprite icon = null)
    {
        if (label != null)
        {
            label.text = text;
            label.color = color;
        }
        if (iconImage != null)
        {
            iconImage.enabled = icon != null;
            if (icon != null) iconImage.sprite = icon;
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