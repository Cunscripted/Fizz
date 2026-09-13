using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Canvas UI version of the draggable additive card. Uses Unity UI's event
/// system (IPointerDownHandler / IBeginDragHandler / IDragHandler / IEndDragHandler /
/// IPointerEnterHandler / IPointerExitHandler) instead of physics-based OnMouseDown
/// etc., so drag/hover work correctly regardless of screen size or camera resizing -
/// everything here moves in RectTransform space, not world space.
///
/// Requires: a Graphic (e.g. Image) with Raycast Target ON somewhere on this
/// GameObject so pointer events actually hit it, this GameObject living under a
/// Canvas, and a GraphicRaycaster + EventSystem present in the scene (Unity adds
/// both automatically via GameObject > UI > ... menu items).
/// </summary>
[RequireComponent(typeof(RectTransform))]
public class AdditiveCard : MonoBehaviour,
    IPointerDownHandler, IBeginDragHandler, IDragHandler, IEndDragHandler,
    IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
{
    [Header("Data")]
    [Tooltip("Runtime instance this card represents. Setting this also refreshes the artwork image below.")]
    public AdditiveInstance instance
    {
        get => _instance;
        set
        {
            _instance = value;
            ApplyIcon();
        }
    }
    public AdditiveData data => _instance?.template;
    private AdditiveInstance _instance;

    [Header("Visuals")]
    [Tooltip("Image whose sprite is set to the additive's icon whenever this card's instance is assigned.")]
    public Image artworkImage;
    [Tooltip("Baseline resting scale, FORCED on spawn regardless of whatever localScale this card happens " +
             "to inherit from its prefab/parent at instantiation time - this is what fixes cards spawning " +
             "at the wrong size and only correcting themselves after the first drag. Prefer resizing via " +
             "Width/Height (sizeDelta) over changing this if you want a genuinely different card size.")]
    public float restScale = 1f;

    [Header("Drag Feel")]
    public float dragScale = 1.1f;

    [Header("Drag Tilt")]
    [Tooltip("Max rotation (degrees) applied while dragging quickly, either direction.")]
    public float maxDragTiltAngle = 12f;
    [Tooltip("Degrees of tilt per pixel of horizontal pointer movement per frame.")]
    public float dragTiltSensitivity = 1.5f;
    [Tooltip("How quickly the tilt eases toward the current movement-based target. Higher = snappier/twitchier.")]
    public float tiltSmoothing = 12f;

    [Header("Hover")]
    public float hoverDelay = 0.5f;

    [Header("Interaction")]
    [Tooltip("Disable for shop/modifier offer cards that should show hover details but not be draggable onto the belt/bottle.")]
    public bool draggable = true;

    [Header("Drag Layer")]
    [Tooltip("Reparent here while dragging so the card renders above the belt/bottle " +
             "(e.g. a top-level 'DragLayer' RectTransform). Falls back to the root Canvas if unset.")]
    public RectTransform dragLayer;

    [Header("Score Reaction")]
    [Tooltip("How high (anchored units) this card hops when ScoreFXPlayer scores it.")]
    public float scoreJumpHeight = 20f;
    public float scoreJumpDuration = 0.25f;

    public bool IsDragging { get; private set; }

    /// <summary>
    /// Lazily fetched rather than only ever set in Awake() - if this card's GameObject
    /// (or its prefab) starts inactive, Unity defers Awake() until it's activated, so
    /// anything accessing .Rect immediately after Instantiate() (before that happens)
    /// would otherwise hit a null reference. This falls back to GetComponent on demand
    /// instead, so it always works regardless of when/whether Awake() has run yet.
    /// </summary>
    public RectTransform Rect
    {
        get
        {
            if (_rect == null) _rect = GetComponent<RectTransform>();
            return _rect;
        }
    }
    private RectTransform _rect;

    /// <summary>Fired when the hover delay elapses / when hover ends, so a UI panel can subscribe.</summary>
    public static event System.Action<AdditiveInstance> OnCardHoverStart;
    public static event System.Action OnCardHoverEnd;

    /// <summary>Fired on click for non-draggable cards (shop/syrup offers, deck stats) - draggable cards ignore clicks.</summary>
    public event System.Action<AdditiveCard> OnCardClicked;

    private ConveyorBelt _belt;
    private SodaBottle _bottle;
    private Canvas _rootCanvas;
    private float _currentTiltZ;
    private Coroutine _hoverRoutine;
    private Coroutine _jumpRoutine;
    private Vector2 _jumpBasePos;
    private bool _isJumping;
    private bool _hoverFired;

    private void Awake()
    {
        // Forced explicitly rather than read from whatever Rect.localScale happens to be
        // at this instant - inheriting it was the cause of cards spawning at inconsistent
        // sizes depending on prefab/parent scale quirks, only "correcting" themselves once
        // OnBeginDrag/OnEndDrag explicitly set localScale for the first time.
        Rect.localScale = Vector3.one * restScale;
        var canvas = GetComponentInParent<Canvas>();
        _rootCanvas = canvas != null ? canvas.rootCanvas : null;
    }

    private void Start()
    {
        _belt = FindObjectOfType<ConveyorBelt>();
        _bottle = FindObjectOfType<SodaBottle>();
        if (draggable) _belt?.AddCard(this);
        ApplyIcon(); // safety net in case instance was assigned before Awake ran
    }

    /// <summary>Sets the artwork Image's sprite to the current additive's icon, if both are set.</summary>
    private void ApplyIcon()
    {
        if (data == null) return;

        if (artworkImage == null)
        {
            Debug.LogWarning($"[AdditiveCard] '{name}' has no artworkImage assigned - drag the card's " +
                              "icon Image component into that field on the prefab.", this);
            return;
        }

        if (data.icon == null)
        {
            Debug.LogWarning($"[AdditiveCard] Additive '{data.additiveName}' has no icon sprite assigned.", this);
            return;
        }

        artworkImage.sprite = data.icon;
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (!draggable) return;
        CancelHover();
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        if (!draggable) return;

        IsDragging = true;
        // Relative multiply, not an absolute reset - this way the "pop bigger while
        // dragging" boost is applied on top of whatever the card's CURRENT (correctly
        // ancestor-scale-adjusted) resting size already is, rather than overwriting it
        // with a frozen value that ignores which parent's scale it's currently under.
        Rect.localScale *= dragScale;
        _currentTiltZ = 0f;

        // If this card was already sitting in the bottle, detach it the moment it's
        // picked up - not just if the drag ends elsewhere - so the freed-up slot is
        // immediately available for a different card, even mid-drag.
        if (_bottle != null && _bottle.Contains(this))
            _bottle.RemoveFromBottle(this);

        var layer = dragLayer != null ? dragLayer : (_rootCanvas != null ? _rootCanvas.transform as RectTransform : null);
        if (layer != null) Rect.SetParent(layer, worldPositionStays: true);
        Rect.SetAsLastSibling(); // render above every other card while dragging

        // Show the detail panel immediately while held, rather than waiting for the hover delay.
        CancelHover();
        _hoverFired = true;
        int pickupSubs = OnCardHoverStart?.GetInvocationList().Length ?? 0;
        Debug.Log($"[AdditiveCard] PICKUP (OnBeginDrag) on '{name}' - firing OnCardHoverStart for " +
                  $"'{data?.additiveName}' ({pickupSubs} subscriber(s)).", this);
        OnCardHoverStart?.Invoke(instance);
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (!draggable || !IsDragging) return;

        var parentRect = Rect.parent as RectTransform;
        if (parentRect == null) return;

        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                parentRect, eventData.position, eventData.pressEventCamera, out var localPoint))
        {
            Rect.localPosition = localPoint;
        }

        // Tilt toward the direction of movement, like a card being slid across a table.
        // eventData.delta is the pointer's screen-space movement since the last event this frame.
        float targetTilt = Mathf.Clamp(-eventData.delta.x * dragTiltSensitivity, -maxDragTiltAngle, maxDragTiltAngle);
        _currentTiltZ = Mathf.LerpAngle(_currentTiltZ, targetTilt, Time.deltaTime * tiltSmoothing);
        Rect.localRotation = Quaternion.Euler(0f, 0f, _currentTiltZ);
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        if (!draggable) return;

        IsDragging = false;
        // Undo the exact multiply from OnBeginDrag, BEFORE any reparenting happens below -
        // this correctly cancels out under the CURRENT parent (still the drag layer at this
        // point), so whatever reparenting follows (AddCard/AcceptAdditive, both using
        // worldPositionStays: true) carries the truly-restored size into its new ancestor
        // scale context, rather than an absolute value that ignores it.
        Rect.localScale /= dragScale;
        Rect.localRotation = Quaternion.identity;
        _currentTiltZ = 0f;
        CancelHover(); // hides the detail panel that was shown for the duration of the drag

        bool dropped = _bottle != null
            && _bottle.IsScreenPointInside(eventData.position, eventData.pressEventCamera)
            && _bottle.AcceptAdditive(this);

        if (dropped)
        {
            _belt?.RemoveCard(this);
        }
        else
        {
            // Either not over the bottle, or the bottle is full (max 5) - rejoin the belt.
            // The belt's own Update() eases it back into formation from wherever it was dropped,
            // and AddCard() reparents it back onto the belt's RectTransform.
            _belt?.AddCard(this);
        }
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (IsDragging) return;
        _hoverRoutine = StartCoroutine(HoverTimer());
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        // Reparenting onto the drag layer in OnBeginDrag can trigger a spurious
        // PointerExit from Unity's EventSystem the same frame. Ignore exits while
        // dragging - only OnEndDrag is allowed to close the detail panel it opened.
        if (IsDragging) return;
        CancelHover();
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        // Draggable cards (belt/bottle gameplay cards) use drag, not click, to avoid
        // ambiguity between "tap to pick" and "pick up to drag". Only non-draggable
        // cards - shop offers, syrup offers, deck stats entries - respond to clicks.
        if (draggable) return;
        OnCardClicked?.Invoke(this);
    }

    private IEnumerator HoverTimer()
    {
        yield return new WaitForSeconds(hoverDelay);
        _hoverFired = true;
        int hoverSubs = OnCardHoverStart?.GetInvocationList().Length ?? 0;
        Debug.Log($"[AdditiveCard] HOVER (HoverTimer elapsed) on '{name}' - firing OnCardHoverStart for " +
                  $"'{data?.additiveName}' ({hoverSubs} subscriber(s)).", this);
        OnCardHoverStart?.Invoke(instance);
    }

    /// <summary>Called by ScoreFXPlayer when a ScoreEvent anchored to this card plays back - a quick hop to sell "this card just scored".</summary>
    public void PlayScoreJump()
    {
        // Only capture a fresh baseline if we're not already mid-jump, so rapid
        // retrigger fires restarting this don't drift the card upward over time.
        if (!_isJumping) _jumpBasePos = Rect.anchoredPosition;
        if (_jumpRoutine != null) StopCoroutine(_jumpRoutine);
        _jumpRoutine = StartCoroutine(JumpRoutine());
    }

    private IEnumerator JumpRoutine()
    {
        _isJumping = true;
        float t = 0f;
        while (t < scoreJumpDuration)
        {
            t += Time.deltaTime;
            float p = Mathf.Clamp01(t / scoreJumpDuration);
            float height = Mathf.Sin(p * Mathf.PI) * scoreJumpHeight; // smooth up-and-back-down arc
            Rect.anchoredPosition = _jumpBasePos + Vector2.up * height;
            yield return null;
        }
        Rect.anchoredPosition = _jumpBasePos;
        _isJumping = false;
        _jumpRoutine = null;
    }

    private void CancelHover()
    {
        if (_hoverRoutine != null)
        {
            StopCoroutine(_hoverRoutine);
            _hoverRoutine = null;
        }
        if (_hoverFired)
        {
            _hoverFired = false;
            OnCardHoverEnd?.Invoke();
        }
    }
}