using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Drives the ConveyorBeltShader's scroll direction/speed. When syncWithBelt is on
/// (default), it auto-syncs with the ConveyorBelt component on THIS SAME GameObject
/// (or an explicitly assigned sourceBelt): scroll speed follows the belt's actual
/// rotationDegreesPerSecond, and the tread direction flips 180 degrees if the belt's
/// rotation is reversed (negative rotationDegreesPerSecond) - so the visual always
/// matches what the cards are actually doing, rather than needing to be tuned by
/// hand to match separately. Turn syncWithBelt off for a belt visual that isn't
/// tied to gameplay (e.g. a purely decorative background) and want full manual control.
///
/// Works on either a world-space Renderer or a Canvas UI Graphic, same dual-path
/// pattern as SodaColorController: Graphics get a cloned material instance at
/// startup so this never edits the shared material asset.
///
/// Draw order: when alwaysRenderAtBack is on, this keeps whatever it's driving at the
/// very back of its respective rendering layer - sortingOrder for the Renderer path
/// (2D sprite-style sorting, forced to rendererBackSortingOrder so it sits below
/// everything else in the same sortingLayer), or sibling index 0 for the Graphic
/// (UI) path (ConveyorBelt itself doesn't manage draw order, so this is the only
/// thing keeping the belt visual - and whatever's on top of it - behind the rest of
/// the UI sharing its parent).
/// </summary>
public class ConveyorBeltShaderController : MonoBehaviour
{
    [Header("Auto-sync")]
    [Tooltip("If true, speed/direction are DERIVED each frame from sourceBelt's rotation instead of set manually.")]
    public bool syncWithBelt = true;
    [Tooltip("Defaults to the ConveyorBelt component on this same GameObject, if any.")]
    public ConveyorBelt sourceBelt;
    [Tooltip("Multiplies the belt's rotationDegreesPerSecond to get the shader's scroll speed - " +
             "these are different units (angular belt rotation vs linear tread scroll), so tune to taste.")]
    public float speedMultiplier = 0.15f;

    [Header("Manual (used when syncWithBelt is off, or as the base direction when synced)")]
    [Tooltip("Degrees, 0 = scrolling right, 90 = scrolling up, etc. When synced, this flips 180 if the belt rotates in reverse.")]
    [Range(0f, 360f)]
    public float direction = 0f;
    [Tooltip("Used directly when syncWithBelt is off.")]
    public float speed = 2f;

    [Header("Draw Order")]
    [Tooltip("Keeps whatever this is driving (Renderer or Graphic) at the very back of its own rendering " +
             "layer. See the class comment above for exactly how the two paths behave.")]
    public bool alwaysRenderAtBack = true;
    [Tooltip("Renderer path only - sortingOrder forced onto this Renderer so it sits below everything else " +
             "sharing its sortingLayer. Leave at the minimum unless something else needs to render even " +
             "further back than this.")]
    public int rendererBackSortingOrder = short.MinValue;

    private Renderer _renderer;
    private Graphic _graphic;
    private MaterialPropertyBlock _mpb;
    private Material _instancedMaterial; // only used for the Graphic (UI) path

    private static readonly int DirectionID = Shader.PropertyToID("_Direction");
    private static readonly int SpeedID = Shader.PropertyToID("_Speed");

    private void Awake()
    {
        if (sourceBelt == null) sourceBelt = GetComponent<ConveyorBelt>();

        _renderer = GetComponent<Renderer>();
        if (_renderer != null)
        {
            _mpb = new MaterialPropertyBlock();
        }
        else
        {
            _graphic = GetComponent<Graphic>();
            if (_graphic != null && _graphic.material != null)
            {
                _instancedMaterial = new Material(_graphic.material);
                _graphic.material = _instancedMaterial;
            }
            else
            {
                Debug.LogWarning($"{nameof(ConveyorBeltShaderController)} on '{name}' found neither a Renderer " +
                                  "nor a Graphic with a material assigned - the belt shader won't visually update.");
            }
        }

        if (syncWithBelt && sourceBelt == null)
            Debug.LogWarning($"{nameof(ConveyorBeltShaderController)} on '{name}' has syncWithBelt enabled but " +
                              "no ConveyorBelt was found on this object and none was assigned to sourceBelt - " +
                              "falling back to the manual direction/speed fields.", this);
    }

    private void Update()
    {
        Apply();
    }

    private void LateUpdate()
    {
        // Re-enforced every frame (cheap - just a value/index check) rather than only
        // once at Start, so this stays at the back even if something else dynamically
        // reorders siblings or sortingOrder later.
        if (alwaysRenderAtBack) EnforceDrawOrder();
    }

    /// <summary>Call directly if you change the manual fields from code and want it to apply immediately.</summary>
    public void Apply()
    {
        float effectiveDirection = direction;
        float effectiveSpeed = speed;

        if (syncWithBelt && sourceBelt != null)
        {
            float beltSpeed = sourceBelt.rotationDegreesPerSecond;
            effectiveSpeed = Mathf.Abs(beltSpeed) * speedMultiplier;
            // Sign of the belt's rotation flips which way the tread marks scroll.
            effectiveDirection = beltSpeed < 0f ? (direction + 180f) % 360f : direction;
        }

        if (_renderer != null)
        {
            _renderer.GetPropertyBlock(_mpb);
            _mpb.SetFloat(DirectionID, effectiveDirection);
            _mpb.SetFloat(SpeedID, effectiveSpeed);
            _renderer.SetPropertyBlock(_mpb);
        }
        else if (_instancedMaterial != null)
        {
            _instancedMaterial.SetFloat(DirectionID, effectiveDirection);
            _instancedMaterial.SetFloat(SpeedID, effectiveSpeed);
        }
    }

    /// <summary>See the "Draw order" section of the class comment above for the full reasoning.</summary>
    private void EnforceDrawOrder()
    {
        if (_renderer != null)
        {
            if (_renderer.sortingOrder != rendererBackSortingOrder)
                _renderer.sortingOrder = rendererBackSortingOrder;
        }
        else if (_graphic != null)
        {
            var t = _graphic.transform;
            if (t.GetSiblingIndex() != 0)
                t.SetAsFirstSibling();
        }
    }
}