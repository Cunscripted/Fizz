using System.Collections;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Generic animated show/hide panel: lerps a RectTransform's anchored position
/// (slide) and/or scale, plus a CanvasGroup fade, between hidden and shown states.
/// Drop this on any menu panel - shop, syrup offers, pause, deck stats, a round-start
/// banner, whatever - and call Show()/Hide()/Toggle(), or wire buttons to them directly
/// in the Inspector.
///
/// Animates on UNSCALED time, so menus (including the pause menu) keep animating
/// smoothly even if you freeze Time.timeScale while paused.
/// </summary>
[RequireComponent(typeof(RectTransform))]
public class LerpPanel : MonoBehaviour
{
    public enum Motion { SlideFromBottom, SlideFromTop, SlideFromLeft, SlideFromRight, ScaleOnly, None }

    [Header("Motion")]
    public Motion motion = Motion.ScaleOnly;
    [Tooltip("How far offscreen (in anchored units) the panel starts from when sliding in.")]
    public float slideDistance = 400f;
    [Tooltip("Scale the panel sits at when fully hidden (1 = no scale change, only relevant with slide/fade).")]
    public float hiddenScale = 0.85f;

    [Header("Timing")]
    public float duration = 0.35f;
    public AnimationCurve easeCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Header("Behavior")]
    [Tooltip("Deactivates the GameObject once fully hidden (saves overdraw/raycasts). If off, it's " +
             "left active but invisible/unclickable via CanvasGroup instead.")]
    public bool deactivateWhenHidden = true;
    public bool startHidden = true;

    public UnityEvent OnShown;
    public UnityEvent OnHidden;

    public bool IsShown { get; private set; }

    private RectTransform _rect;
    private CanvasGroup _canvasGroup;
    private Vector2 _shownPos;
    private Coroutine _routine;

    private void Awake()
    {
        _rect = GetComponent<RectTransform>();
        _canvasGroup = GetComponent<CanvasGroup>();
        if (_canvasGroup == null) _canvasGroup = gameObject.AddComponent<CanvasGroup>();
        _shownPos = _rect.anchoredPosition; // wherever you've placed it in the editor = the "shown" position
    }

    private void Start()
    {
        SetImmediate(!startHidden);
    }

    public void Show()
    {
        if (_routine != null) StopCoroutine(_routine);
        gameObject.SetActive(true);
        if (!gameObject.activeInHierarchy)
        {
            Debug.LogWarning($"[LerpPanel] '{name}' Show() called but activeInHierarchy is still false after " +
                              "SetActive(true) - a PARENT object is disabled, which will silently prevent this " +
                              "panel (and its animation coroutine) from actually running.", this);
        }
        _routine = StartCoroutine(Animate(true));
    }

    public void Hide()
    {
        if (_routine != null) StopCoroutine(_routine);
        _routine = StartCoroutine(Animate(false));
    }

    public void Toggle()
    {
        if (IsShown) Hide(); else Show();
    }

    private void SetImmediate(bool shown)
    {
        IsShown = shown;
        ApplyState(shown ? 1f : 0f);
        if (!shown && deactivateWhenHidden) gameObject.SetActive(false);
    }

    private IEnumerator Animate(bool show)
    {
        gameObject.SetActive(true); // must be active to animate, even while hiding

        // Start from wherever it currently visually is (its current alpha), not
        // always 0/1, so interrupting an in-progress animation with the opposite
        // call reverses smoothly instead of snapping.
        float from = _canvasGroup.alpha;
        float to = show ? 1f : 0f;
        float t = 0f;

        while (t < duration)
        {
            t += Time.unscaledDeltaTime;
            float p = Mathf.Clamp01(t / duration);
            float eased = easeCurve.Evaluate(Mathf.Lerp(from, to, p));
            ApplyState(eased);
            yield return null;
        }

        ApplyState(to);
        IsShown = show;
        if (!show && deactivateWhenHidden) gameObject.SetActive(false);

        if (show) OnShown?.Invoke(); else OnHidden?.Invoke();
        _routine = null;
    }

    /// <summary>t: 0 = fully hidden, 1 = fully shown.</summary>
    private void ApplyState(float t)
    {
        _canvasGroup.alpha = t;
        _canvasGroup.blocksRaycasts = t > 0.99f;
        _canvasGroup.interactable = t > 0.99f;

        Vector2 offset = motion switch
        {
            Motion.SlideFromBottom => Vector2.down * slideDistance,
            Motion.SlideFromTop => Vector2.up * slideDistance,
            Motion.SlideFromLeft => Vector2.left * slideDistance,
            Motion.SlideFromRight => Vector2.right * slideDistance,
            _ => Vector2.zero
        };

        _rect.anchoredPosition = Vector2.Lerp(_shownPos + offset, _shownPos, t);

        float scale = Mathf.Lerp(hiddenScale, 1f, t);
        _rect.localScale = new Vector3(scale, scale, 1f);
    }
}