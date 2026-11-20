using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// One step in a tutorial sequence - what to highlight, what text to show, and where
/// the textbox sits for this step. Set textBoxAnchoredPosition using the Tutorial
/// Editor tool's "drag & save" workflow (drag TutorialController.textBox around in
/// Play mode, click Save) rather than typing coordinates by hand.
/// </summary>
[Serializable]
public class TutorialStep
{
    public string stepName = "New Step";
    [TextArea(3, 6)] public string bodyText = "";

    [Tooltip("The UI element to spotlight for this step. Leave unset for no highlight (whole screen stays dimmed).")]
    public RectTransform highlightTarget;
    [Tooltip("Extra padding (pixels) around the highlight target's bounds when cutting the spotlight hole.")]
    public float highlightPadding = 8f;

    [Tooltip("Anchored position (relative to TutorialController.overlayRoot) where the textbox sits for this " +
             "step. Set via the Tutorial Editor tool rather than typing coordinates by hand.")]
    public Vector2 textBoxAnchoredPosition;
}

/// <summary>
/// Drives a step-by-step tutorial over a real (or mock) gameplay scene: dims everything
/// except a rectangular "spotlight" around each step's highlightTarget, and glides both
/// the spotlight hole and a movable textbox smoothly between steps. Advances ONLY when
/// nextButton is clicked - never automatically.
///
/// Setup: build an overlayRoot RectTransform covering the whole screen (top of your
/// Canvas hierarchy so it renders over everything), with four child RectTransforms
/// (spotlightTop/Bottom/Left/Right - solid dark Images) each using a fixed-point CENTER
/// anchor and CENTER pivot (so anchoredPosition/sizeDelta behave as simple center-offset
/// + size, which is what the spotlight math below assumes), plus a textBox child with a
/// TMP_Text label, and Next/Previous/Skip buttons.
/// </summary>
public class TutorialController : MonoBehaviour
{
    [Header("Steps")]
    public List<TutorialStep> steps = new List<TutorialStep>();

    [Header("Overlay")]
    [Tooltip("Full-screen parent of the whole tutorial overlay - toggled active/inactive to show/hide it.")]
    public RectTransform overlayRoot;
    public RectTransform textBox;
    public TMP_Text textBoxLabel;

    [Header("Spotlight Panels (center anchor + pivot required - see class comment)")]
    public RectTransform spotlightTop;
    public RectTransform spotlightBottom;
    public RectTransform spotlightLeft;
    public RectTransform spotlightRight;

    [Header("Buttons")]
    public Button nextButton;
    public Button previousButton;
    public Button skipButton;

    [Header("Timing")]
    public float transitionDuration = 0.4f;

    public int CurrentStepIndex { get; private set; } = -1;
    public bool IsActive => overlayRoot != null && overlayRoot.gameObject.activeSelf;

    private Rect _currentHole; // tracks the spotlight hole's current rect, as the starting point for the next glide
    private Coroutine _glideRoutine;

    private void Awake()
    {
        if (nextButton != null) nextButton.onClick.AddListener(Next);
        if (previousButton != null) previousButton.onClick.AddListener(Previous);
        if (skipButton != null) skipButton.onClick.AddListener(EndTutorial);
    }

    public void StartTutorial()
    {
        if (steps.Count == 0)
        {
            Debug.LogWarning($"[TutorialController] '{name}' has no steps configured.", this);
            return;
        }
        if (overlayRoot != null) overlayRoot.gameObject.SetActive(true);
        _currentHole = new Rect(0f, 0f, 0f, 0f); // start fully dimmed, no hole - first step reveals into its spotlight
        CurrentStepIndex = -1;
        Next();
    }

    public void Next()
    {
        if (CurrentStepIndex + 1 >= steps.Count)
        {
            EndTutorial();
            return;
        }
        GoToStep(CurrentStepIndex + 1);
    }

    public void Previous()
    {
        if (CurrentStepIndex <= 0) return;
        GoToStep(CurrentStepIndex - 1);
    }

    public void EndTutorial()
    {
        if (_glideRoutine != null) StopCoroutine(_glideRoutine);
        if (overlayRoot != null) overlayRoot.gameObject.SetActive(false);
        CurrentStepIndex = -1;
    }

    /// <summary>Editor-only helper: jumps straight to a step with no glide, for previewing in the Tutorial Editor tool.</summary>
    public void EditorJumpToStep(int index)
    {
        if (index < 0 || index >= steps.Count) return;
        if (overlayRoot != null) overlayRoot.gameObject.SetActive(true);
        if (_glideRoutine != null) StopCoroutine(_glideRoutine);

        var step = steps[index];
        CurrentStepIndex = index;
        if (textBoxLabel != null) textBoxLabel.text = step.bodyText;
        if (textBox != null) textBox.anchoredPosition = step.textBoxAnchoredPosition;

        _currentHole = ComputeHoleRect(step.highlightTarget, step.highlightPadding);
        ApplySpotlightRect(_currentHole);
    }

    private void GoToStep(int index)
    {
        CurrentStepIndex = index;
        var step = steps[index];
        if (textBoxLabel != null) textBoxLabel.text = step.bodyText;

        if (_glideRoutine != null) StopCoroutine(_glideRoutine);
        _glideRoutine = StartCoroutine(GlideToStep(step));
    }

    private IEnumerator GlideToStep(TutorialStep step)
    {
        Vector2 textStart = textBox != null ? textBox.anchoredPosition : Vector2.zero;
        Vector2 textEnd = step.textBoxAnchoredPosition;

        Rect holeStart = _currentHole;
        Rect holeEnd = ComputeHoleRect(step.highlightTarget, step.highlightPadding);

        float t = 0f;
        while (t < transitionDuration)
        {
            t += Time.unscaledDeltaTime;
            float p = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / transitionDuration));

            if (textBox != null) textBox.anchoredPosition = Vector2.Lerp(textStart, textEnd, p);
            ApplySpotlightRect(RectLerp(holeStart, holeEnd, p));

            yield return null;
        }

        if (textBox != null) textBox.anchoredPosition = textEnd;
        ApplySpotlightRect(holeEnd);
        _currentHole = holeEnd;
        _glideRoutine = null;
    }

    /// <summary>
    /// Converts a highlight target's world-space bounds into a rect in overlayRoot's own
    /// local (anchored-position) space - i.e. centered on (0,0), which is what the
    /// 4-panel spotlight math below assumes. Returns a zero-size rect at center for a
    /// null target (whole screen stays dimmed, no cutout).
    /// </summary>
    private Rect ComputeHoleRect(RectTransform target, float padding)
    {
        if (target == null || overlayRoot == null) return new Rect(0f, 0f, 0f, 0f);

        Vector3[] worldCorners = new Vector3[4]; // bottom-left, top-left, top-right, bottom-right
        target.GetWorldCorners(worldCorners);

        Vector2 min = overlayRoot.InverseTransformPoint(worldCorners[0]);
        Vector2 max = overlayRoot.InverseTransformPoint(worldCorners[2]);

        min -= Vector2.one * padding;
        max += Vector2.one * padding;

        return new Rect(min.x, min.y, max.x - min.x, max.y - min.y); // Rect(x=xMin, y=yMin, width, height)
    }

    /// <summary>
    /// Positions/sizes the four spotlight panels to cover overlayRoot's full area except
    /// the given hole rect (in the same center-origin local space as ComputeHoleRect).
    /// Requires each panel to use a center anchor + center pivot.
    /// </summary>
    private void ApplySpotlightRect(Rect hole)
    {
        if (overlayRoot == null) return;

        float halfW = overlayRoot.rect.width / 2f;
        float halfH = overlayRoot.rect.height / 2f;

        // Top: full width, spans from the hole's top edge up to the screen's top edge.
        SetPanel(spotlightTop,
            center: new Vector2(0f, (hole.yMax + halfH) / 2f),
            size: new Vector2(overlayRoot.rect.width, halfH - hole.yMax));

        // Bottom: full width, spans from the screen's bottom edge up to the hole's bottom edge.
        SetPanel(spotlightBottom,
            center: new Vector2(0f, (-halfH + hole.yMin) / 2f),
            size: new Vector2(overlayRoot.rect.width, hole.yMin + halfH));

        // Left: spans the hole's vertical extent, from the screen's left edge to the hole's left edge.
        SetPanel(spotlightLeft,
            center: new Vector2((-halfW + hole.xMin) / 2f, (hole.yMin + hole.yMax) / 2f),
            size: new Vector2(hole.xMin + halfW, hole.yMax - hole.yMin));

        // Right: spans the hole's vertical extent, from the hole's right edge to the screen's right edge.
        SetPanel(spotlightRight,
            center: new Vector2((hole.xMax + halfW) / 2f, (hole.yMin + hole.yMax) / 2f),
            size: new Vector2(halfW - hole.xMax, hole.yMax - hole.yMin));
    }

    private static void SetPanel(RectTransform panel, Vector2 center, Vector2 size)
    {
        if (panel == null) return;
        panel.anchoredPosition = center;
        panel.sizeDelta = new Vector2(Mathf.Max(0f, size.x), Mathf.Max(0f, size.y));
    }

    private static Rect RectLerp(Rect a, Rect b, float t)
    {
        return new Rect(
            Mathf.Lerp(a.x, b.x, t),
            Mathf.Lerp(a.y, b.y, t),
            Mathf.Lerp(a.width, b.width, t),
            Mathf.Lerp(a.height, b.height, t));
    }
}
