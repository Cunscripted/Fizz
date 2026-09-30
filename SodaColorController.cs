using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Reads the additives currently in the cup, computes a weighted color blend
/// and an "intensity" score, and pushes both to the soda liquid material.
/// Also drives the liquid's FILL LEVEL: the SodaLiquid shader only shows the bottom
/// part of the image, and this sets how much of it (additives in the cup / cup capacity).
///
/// Works on either:
///  - a world-space Renderer (SpriteRenderer/MeshRenderer), via a MaterialPropertyBlock, or
///  - a Canvas UI Graphic (e.g. Image using the SodaLiquid material), via a unique
///    material instance - UI Graphics don't support MaterialPropertyBlock, so for
///    the UI path this component clones the assigned material once at startup so
///    it never edits the shared material asset.
/// Whichever is found on this GameObject at Awake is used automatically.
///
/// Color and fill both ease toward their targets every frame in Update(), so changes
/// animate smoothly instead of jumping.
/// </summary>
public class SodaColorController : MonoBehaviour
{
    public FlavorPalette palette;
    [Tooltip("How quickly the visible color eases toward the target when additives change.")]
    public float colorLerpSpeed = 3f;
    [Tooltip("How opaque/visible the bubbles render over the liquid color (0 = invisible, 1 = fully " +
             "opaque bubble color at each bubble, >1 overshoots for a brighter/foamier look).")]
    [Range(0f, 2f)]
    public float bubbleOpacity = 0.6f;
    [Tooltip("Per-bubble radius, independent of how many bubbles there are. Raise this (and/or lower " +
             "Bubble Density on the material) for fewer, larger bubbles instead of lots of small ones.")]
    [Range(0.3f, 3f)]
    public float bubbleSize = 1.6f;
    [Tooltip("Saturation/vividness of the liquid color. 1 = unchanged, 0 = grayscale, >1 = oversaturated.")]
    [Range(0f, 3f)]
    public float colorIntensity = 1f;

    [Header("Fill Level")]
    [Tooltip("If on, the liquid fills up from the bottom as additives go in: fill = additives in cup / cup " +
             "capacity (so 3 of 5 shows the bottom 60% of the image). Off = always show the whole image.")]
    public bool driveFillFromCup = true;
    [Tooltip("How quickly the liquid level eases toward its new height when additives are added/removed.")]
    public float fillLerpSpeed = 4f;
    [Tooltip("Fill shown when the cup is EMPTY (0 = nothing visible). Useful if you want a little liquid " +
             "to always sit at the bottom.")]
    [Range(0f, 1f)]
    public float emptyFill = 0f;
    [Tooltip("Fill shown when the cup is FULL. Lower than 1 to leave some headroom at the top of the glass.")]
    [Range(0f, 1f)]
    public float fullFill = 1f;

    private Renderer _renderer;
    private SpriteRenderer _spriteRenderer;
    private Graphic _graphic;
    private MaterialPropertyBlock _mpb;
    private Material _instancedMaterial; // only used for the Graphic (UI) path

    private Color _currentColor = Color.white;
    private Color _targetColor = Color.white;
    private float _bubbleSpeed = 0.05f;
    private float _bubbleIntensity;
    private float _currentFill;
    private float _targetFill;
    private int _lastCapacity = 5;

    private static readonly int ColorID = Shader.PropertyToID("_SodaColor");
    private static readonly int BubbleSpeedID = Shader.PropertyToID("_BubbleSpeed");
    private static readonly int BubbleIntensityID = Shader.PropertyToID("_BubbleIntensity");
    private static readonly int BubbleOpacityID = Shader.PropertyToID("_BubbleOpacity");
    private static readonly int BubbleSizeID = Shader.PropertyToID("_BubbleSize");
    private static readonly int ColorIntensityID = Shader.PropertyToID("_ColorIntensity");
    private static readonly int FillAmountID = Shader.PropertyToID("_FillAmount");
    private static readonly int FillUVRangeID = Shader.PropertyToID("_FillUVRange");

    /// <summary>The fill level (0-1) currently being shown, after easing.</summary>
    public float CurrentFill => _currentFill;

    private void Awake()
    {
        _renderer = GetComponent<Renderer>();
        if (_renderer != null)
        {
            _mpb = new MaterialPropertyBlock();
            _spriteRenderer = _renderer as SpriteRenderer;
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
                Debug.LogWarning($"{nameof(SodaColorController)} on '{name}' found neither a Renderer nor a " +
                                  "Graphic with a material assigned - the soda liquid won't visually update.");
            }
        }

        _targetFill = driveFillFromCup ? emptyFill : 1f;
        _currentFill = _targetFill;
    }

    private void Update()
    {
        _currentColor = Color.Lerp(_currentColor, _targetColor, Time.deltaTime * colorLerpSpeed);
        _currentFill = Mathf.Lerp(_currentFill, _targetFill, Time.deltaTime * fillLerpSpeed);
        if (Mathf.Abs(_currentFill - _targetFill) < 0.001f) _currentFill = _targetFill;
        ApplyToMaterial();
    }

    /// <summary>
    /// Call this whenever an additive is added/removed from the cup (SodaBottle does this
    /// automatically). capacity is the bottle's current max - fill = cup.Count / capacity.
    /// </summary>
    public void UpdateCup(List<AdditiveInstance> cup, int capacity)
    {
        _lastCapacity = Mathf.Max(1, capacity);
        UpdateCup(cup);
    }

    /// <summary>Same as above, reusing the last capacity it was given.</summary>
    public void UpdateCup(List<AdditiveInstance> cup)
    {
        int count = cup != null ? cup.Count : 0;
        float percent = Mathf.Clamp01((float)count / Mathf.Max(1, _lastCapacity));
        _targetFill = driveFillFromCup
            ? (count == 0 ? emptyFill : Mathf.Lerp(emptyFill, fullFill, percent))
            : 1f;

        if (count == 0)
        {
            _targetColor = Color.white;
            _bubbleSpeed = 0.05f;
            _bubbleIntensity = 0f;
            return;
        }

        // Weighted average color: additives with a bigger point/mult "footprint"
        // pull the color harder, so a big high-value additive dominates over a tiny one.
        Color blended = Color.black;
        float totalWeight = 0f;
        float intensity = 0f;

        foreach (var additive in cup)
        {
            float weight = Mathf.Max(additive.template.amount, 1f);
            foreach (var flavor in additive.Flavors)
            {
                blended += palette.GetColor(flavor) * weight;
                totalWeight += weight;
            }
            intensity += weight + additive.TotalRetriggers * 5f;
        }

        if (totalWeight > 0f) blended /= totalWeight;
        else blended = Color.white; // only flavorless additives in the cup
        blended.a = 1f;

        _targetColor = blended;
        // Bubble speed scales with intensity but is clamped so it doesn't go absurd.
        _bubbleSpeed = Mathf.Clamp(0.3f + intensity * 0.02f, 0.3f, 3f);
        _bubbleIntensity = Mathf.Clamp01(0.1f + count * 0.08f);
    }

    /// <summary>
    /// The vertical UV range the liquid image occupies in its texture (x = bottom v, y = top v).
    /// Normally 0-1, but a sprite packed into an atlas only covers part of the texture, so the
    /// fill percentage has to be measured against that sprite's own rect instead.
    /// </summary>
    private Vector4 GetFillUVRange()
    {
        Sprite sprite = null;
        if (_spriteRenderer != null) sprite = _spriteRenderer.sprite;
        else if (_graphic is Image image) sprite = image.overrideSprite != null ? image.overrideSprite : image.sprite;

        if (sprite == null) return new Vector4(0f, 1f, 0f, 0f);
        Vector4 outer = UnityEngine.Sprites.DataUtility.GetOuterUV(sprite); // (uMin, vMin, uMax, vMax)
        return new Vector4(outer.y, outer.w, 0f, 0f);
    }

    private void ApplyToMaterial()
    {
        Vector4 uvRange = GetFillUVRange();

        if (_renderer != null)
        {
            _renderer.GetPropertyBlock(_mpb);
            _mpb.SetColor(ColorID, _currentColor);
            _mpb.SetFloat(BubbleSpeedID, _bubbleSpeed);
            _mpb.SetFloat(BubbleIntensityID, _bubbleIntensity);
            _mpb.SetFloat(BubbleOpacityID, bubbleOpacity);
            _mpb.SetFloat(BubbleSizeID, bubbleSize);
            _mpb.SetFloat(ColorIntensityID, colorIntensity);
            _mpb.SetFloat(FillAmountID, _currentFill);
            _mpb.SetVector(FillUVRangeID, uvRange);
            _renderer.SetPropertyBlock(_mpb);
        }
        else if (_instancedMaterial != null)
        {
            _instancedMaterial.SetColor(ColorID, _currentColor);
            _instancedMaterial.SetFloat(BubbleSpeedID, _bubbleSpeed);
            _instancedMaterial.SetFloat(BubbleIntensityID, _bubbleIntensity);
            _instancedMaterial.SetFloat(BubbleOpacityID, bubbleOpacity);
            _instancedMaterial.SetFloat(BubbleSizeID, bubbleSize);
            _instancedMaterial.SetFloat(ColorIntensityID, colorIntensity);
            _instancedMaterial.SetFloat(FillAmountID, _currentFill);
            _instancedMaterial.SetVector(FillUVRangeID, uvRange);
        }
    }
}