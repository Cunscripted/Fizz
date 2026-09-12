using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Attach to any UI Button. The button will gently float in place and
/// expand with a drop shadow on hover.
///
/// Requirements:
///   - The Button's RectTransform is the target of all transforms.
///   - Add a Shadow or drop-shadow component, OR let this script create
///     one automatically (see _autoAddShadow).
///   - Works in Screen Space - Overlay and Screen Space - Camera canvases.
/// </summary>
[RequireComponent(typeof(RectTransform))]
public class FloatingButton : MonoBehaviour,
    IPointerEnterHandler, IPointerExitHandler
{
    // ─────────────────────────────────────────────
    // Inspector
    // ─────────────────────────────────────────────

    [Header("Float")]
    [Tooltip("Peak vertical drift in pixels.")]
    public float floatAmplitudeY = 8f;

    [Tooltip("Peak horizontal drift (subtle lemniscate feel).")]
    public float floatAmplitudeX = 3f;

    [Tooltip("Gentle tilt in degrees at the float extremes.")]
    public float floatTiltDeg = 1.5f;

    [Tooltip("Seconds for one full up-and-down cycle.")]
    public float floatPeriod = 2.4f;

    [Tooltip("Starting phase offset (0-1). Set different values per button so they don't all move in sync.")]
    [Range(0f, 1f)]
    public float phaseOffset = 0f;


    [Header("Hover")]
    [Tooltip("Uniform scale when fully hovered.")]
    public float hoverScale = 1.08f;

    [Tooltip("How quickly the button springs toward hover / idle scale.")]
    public float hoverLerpSpeed = 12f;


    [Header("Shadow")]
    [Tooltip("If true, a Shadow component is added automatically if none exists.")]
    public bool autoAddShadow = true;

    [Tooltip("Shadow colour at idle.")]
    public Color shadowColorIdle = new Color(0f, 0f, 0f, 0.25f);

    [Tooltip("Shadow colour at full hover (tinted, stronger).")]
    public Color shadowColorHover = new Color(0f, 0f, 0f, 0.45f);

    [Tooltip("Shadow offset at idle.")]
    public Vector2 shadowOffsetIdle = new Vector2(4f, -8f);

    [Tooltip("Shadow offset at full hover.")]
    public Vector2 shadowOffsetHover = new Vector2(6f, -14f);

    // ─────────────────────────────────────────────
    // Private state
    // ─────────────────────────────────────────────

    RectTransform _rt;
    Shadow        _shadow;

    Vector3 _basePosition;   // anchored position at Start
    float   _timeOffset;     // per-instance phase in seconds

    bool  _hovered      = false;
    float _hoverT       = 0f;  // 0 = idle, 1 = fully hovered
    float _currentScale = 1f;

    // ─────────────────────────────────────────────
    // Unity lifecycle
    // ─────────────────────────────────────────────

    void Awake()
    {
        _rt = GetComponent<RectTransform>();
    }

    void Start()
    {
        _basePosition = _rt.anchoredPosition3D;
        _timeOffset   = phaseOffset * floatPeriod;

        // Shadow setup
        _shadow = GetComponent<Shadow>();
        if (_shadow == null && autoAddShadow)
            _shadow = gameObject.AddComponent<Shadow>();

        if (_shadow != null)
        {
            _shadow.effectColor    = shadowColorIdle;
            _shadow.effectDistance = shadowOffsetIdle;
        }
    }

    void Update()
    {
        UpdateFloat();
        UpdateHover();
    }

    // ─────────────────────────────────────────────
    // Hover events
    // ─────────────────────────────────────────────

    public void OnPointerEnter(PointerEventData _) => _hovered = true;
    public void OnPointerExit(PointerEventData _)  => _hovered = false;

    // ─────────────────────────────────────────────
    // Float
    // ─────────────────────────────────────────────

    void UpdateFloat()
    {
        float t = (Time.time + _timeOffset) / floatPeriod;

        // Primary vertical sine
        float y = Mathf.Sin(t * Mathf.PI * 2f) * floatAmplitudeY;

        // Secondary horizontal using a slightly irrational frequency
        // ratio so it never quite repeats — gives a lemniscate-like drift
        float x = Mathf.Cos(t * Mathf.PI * 2f * 0.61f) * floatAmplitudeX;

        // Tilt follows the vertical velocity (derivative of the sine)
        float tilt = Mathf.Cos(t * Mathf.PI * 2f) * floatTiltDeg;

        // While hovered, blend the float offset toward zero so the button
        // feels anchored under the cursor
        float floatBlend = 1f - _hoverT;

        _rt.anchoredPosition3D = _basePosition
            + new Vector3(x * floatBlend, y * floatBlend, 0f);

        _rt.localRotation = Quaternion.Euler(
            0f, 0f, tilt * floatBlend);

        // Sync shadow depth with float height (deeper when "higher")
        if (_shadow != null)
        {
            float shadowBlend = Mathf.InverseLerp(
                -floatAmplitudeY, floatAmplitudeY, y);

            _shadow.effectDistance = Vector2.Lerp(
                shadowOffsetIdle * 0.6f,
                shadowOffsetIdle * 1.4f,
                shadowBlend * floatBlend);
        }
    }

    // ─────────────────────────────────────────────
    // Hover scale + shadow
    // ─────────────────────────────────────────────

    void UpdateHover()
    {
        float targetHoverT = _hovered ? 1f : 0f;
        _hoverT = Mathf.Lerp(_hoverT, targetHoverT,
            Time.deltaTime * hoverLerpSpeed);

        // Spring scale: overshoot slightly on enter using AnimationCurve-
        // style math — lerp toward target + a tiny eased overshoot
        float targetScale = Mathf.Lerp(1f, hoverScale, _hoverT);

        // Soft spring: approach faster when far away
        _currentScale = Mathf.Lerp(
            _currentScale, targetScale,
            Time.deltaTime * hoverLerpSpeed * 1.2f);

        _rt.localScale = Vector3.one * _currentScale;

        // Shadow colour + size
        if (_shadow != null)
        {
            _shadow.effectColor = Color.Lerp(
                shadowColorIdle, shadowColorHover, _hoverT);

            _shadow.effectDistance = Vector2.Lerp(
                shadowOffsetIdle, shadowOffsetHover, _hoverT);
        }
    }
}