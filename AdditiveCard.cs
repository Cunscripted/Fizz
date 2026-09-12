using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

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

    public bool IsDragging { get; private set; }
    public RectTransform Rect { get; private set; }

    public static event System.Action<AdditiveData> OnCardHoverStart;
    public static event System.Action OnCardHoverEnd;

    public event System.Action<AdditiveCard> OnCardClicked;

    private ConveyorBelt _belt;
    private SodaBottle _bottle;
    private Canvas _rootCanvas;
    private Vector3 _baseScale;
    private float _currentTiltZ;
    private Coroutine _hoverRoutine;
    private bool _hoverFired;

    private void Awake()
    {
        Rect = GetComponent<RectTransform>();
        _baseScale = Rect.localScale;
        var canvas = GetComponentInParent<Canvas>();
        _rootCanvas = canvas != null ? canvas.rootCanvas : null;
    }

    private void Start()
    {
        _belt = FindObjectOfType<ConveyorBelt>();
        _bottle = FindObjectOfType<SodaBottle>();
        if (draggable) _belt?.AddCard(this);
        ApplyIcon(); 

        Rect.localScale = _baseScale;
    }

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
        Rect.localScale = _baseScale * dragScale;
        _currentTiltZ = 0f;

        if (_bottle != null && _bottle.Contains(this))
            _bottle.RemoveFromBottle(this);

        var layer = dragLayer != null ? dragLayer : (_rootCanvas != null ? _rootCanvas.transform as RectTransform : null);
        if (layer != null) Rect.SetParent(layer, worldPositionStays: true);
        Rect.SetAsLastSibling(); 

        CancelHover();
        _hoverFired = true;
        int subs = OnCardHoverStart?.GetInvocationList().Length ?? 0;
        Debug.Log($"[AdditiveCard] OnBeginDrag firing OnCardHoverStart for '{data?.additiveName}' ({subs} subscriber(s)).", this);
        OnCardHoverStart?.Invoke(data);
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
        float targetTilt = Mathf.Clamp(-eventData.delta.x * dragTiltSensitivity, -maxDragTiltAngle, maxDragTiltAngle);
        _currentTiltZ = Mathf.LerpAngle(_currentTiltZ, targetTilt, Time.deltaTime * tiltSmoothing);
        Rect.localRotation = Quaternion.Euler(0f, 0f, _currentTiltZ);
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        if (!draggable) return;

        IsDragging = false;
        Rect.localScale = _baseScale;
        Rect.localRotation = Quaternion.identity;
        _currentTiltZ = 0f;
        CancelHover();

        bool dropped = _bottle != null
            && _bottle.IsScreenPointInside(eventData.position, eventData.pressEventCamera)
            && _bottle.AcceptAdditive(this);

        if (dropped)
        {
            _belt?.RemoveCard(this);
        }
        else
        {
            _belt?.AddCard(this);
        }
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        Debug.Log($"[AdditiveCard] OnPointerEnter on '{name}' (data={(data != null ? data.additiveName : "NULL")}).", this);
        if (IsDragging) return;
        _hoverRoutine = StartCoroutine(HoverTimer());
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (IsDragging) return;
        CancelHover();
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (draggable) return;
        OnCardClicked?.Invoke(this);
    }

    private IEnumerator HoverTimer()
    {
        yield return new WaitForSeconds(hoverDelay);
        _hoverFired = true;
        int subs = OnCardHoverStart?.GetInvocationList().Length ?? 0;
        Debug.Log($"[AdditiveCard] HoverTimer firing OnCardHoverStart for '{data?.additiveName}' ({subs} subscriber(s)).", this);
        OnCardHoverStart?.Invoke(data);
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