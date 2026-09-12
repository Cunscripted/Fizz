using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Reads the additives currently in the cup, computes a weighted color blend
/// and an "intensity" score, and pushes both to the soda liquid material.
///
/// Works on either:
///  - a world-space Renderer (SpriteRenderer/MeshRenderer), via a MaterialPropertyBlock, or
///  - a Canvas UI Graphic (e.g. Image using the SodaLiquid material), via a unique
///    material instance - UI Graphics don't support MaterialPropertyBlock, so for
///    the UI path this component clones the assigned material once at startup so
///    it never edits the shared material asset.
/// Whichever is found on this GameObject at Awake is used automatically.
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

    private Renderer _renderer;
    private Graphic _graphic;
    private MaterialPropertyBlock _mpb;
    private Material _instancedMaterial; // only used for the Graphic (UI) path
    private Color _currentColor = Color.white;

    private static readonly int ColorID = Shader.PropertyToID("_SodaColor");
    private static readonly int BubbleSpeedID = Shader.PropertyToID("_BubbleSpeed");
    private static readonly int BubbleIntensityID = Shader.PropertyToID("_BubbleIntensity");
    private static readonly int BubbleOpacityID = Shader.PropertyToID("_BubbleOpacity");
    private static readonly int BubbleSizeID = Shader.PropertyToID("_BubbleSize");
    private static readonly int ColorIntensityID = Shader.PropertyToID("_ColorIntensity");

    private void Awake()
    {
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
                Debug.LogWarning($"{nameof(SodaColorController)} on '{name}' found neither a Renderer nor a " +
                                  "Graphic with a material assigned - the soda liquid won't visually update.");
            }
        }
    }

    /// <summary>
    /// Call this whenever an additive is added/removed from the cup.
    /// </summary>
    public void UpdateCup(List<AdditiveInstance> cup)
    {
        if (cup.Count == 0)
        {
            ApplyToMaterial(Color.white, 0.05f, 0f);
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
        blended.a = 1f;

        // Bubble speed scales with intensity but is clamped so it doesn't go absurd.
        float bubbleSpeed = Mathf.Clamp(0.3f + intensity * 0.02f, 0.3f, 3f);
        float bubbleIntensity = Mathf.Clamp01(0.1f + cup.Count * 0.08f);

        ApplyToMaterial(blended, bubbleSpeed, bubbleIntensity);
    }

    private void ApplyToMaterial(Color targetColor, float bubbleSpeed, float bubbleIntensity)
    {
        _currentColor = Color.Lerp(_currentColor, targetColor, Time.deltaTime * colorLerpSpeed);

        if (_renderer != null)
        {
            _renderer.GetPropertyBlock(_mpb);
            _mpb.SetColor(ColorID, _currentColor);
            _mpb.SetFloat(BubbleSpeedID, bubbleSpeed);
            _mpb.SetFloat(BubbleIntensityID, bubbleIntensity);
            _mpb.SetFloat(BubbleOpacityID, bubbleOpacity);
            _mpb.SetFloat(BubbleSizeID, bubbleSize);
            _mpb.SetFloat(ColorIntensityID, colorIntensity);
            _renderer.SetPropertyBlock(_mpb);
        }
        else if (_instancedMaterial != null)
        {
            _instancedMaterial.SetColor(ColorID, _currentColor);
            _instancedMaterial.SetFloat(BubbleSpeedID, bubbleSpeed);
            _instancedMaterial.SetFloat(BubbleIntensityID, bubbleIntensity);
            _instancedMaterial.SetFloat(BubbleOpacityID, bubbleOpacity);
            _instancedMaterial.SetFloat(BubbleSizeID, bubbleSize);
            _instancedMaterial.SetFloat(ColorIntensityID, colorIntensity);
        }
    }
}