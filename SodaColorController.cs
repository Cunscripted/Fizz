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
    public enum BlendWeighting
    {
        Equal,          // every additive pulls the color equally, however big its numbers are
        AmountSoftened, // bigger amounts pull a bit harder, but only logarithmically (50 vs 2 is ~2x, not 25x)
        Amount          // old behavior: pull is proportional to amount - big point cards dominate
    }

    public FlavorPalette palette;

    [Header("Color Blending")]
    [Tooltip("How much each additive pulls the color. Equal = every card counts the same. Amount = the old " +
             "behavior, where a 50-point card outweighs a 2-mult card 25 to 1 (why one flavor could take over).")]
    public BlendWeighting weighting = BlendWeighting.Equal;
    [Tooltip("If on, colors are mixed in a perceptual color space (Oklab), so a mix looks evenly in-between " +
             "to the eye instead of muddy or leaning toward whichever color is brightest. Off = plain RGB average.")]
    public bool perceptualBlend = true;
    [Tooltip("Mixing very different colors drifts toward gray. This restores some of the vividness the " +
             "flavors had (0 = none, 1 = as vivid as the average flavor). Only used with Perceptual Blend.")]
    [Range(0f, 1f)]
    public float keepVividness = 0.5f;
    [Tooltip("How much the cup's MOST COMMON flavor drives the color (ties go to whichever was added first). " +
             "0 = a pure average of every flavor - which can land on a color none of them are, e.g. red + purple " +
             "= pink. 0.5 = the leading flavor sets the hue and the rest tint it. 1 = only the leading flavor.")]
    [Range(0f, 1f)]
    public float leadFlavorStrength = 0.5f;
    [Tooltip("How strongly flavors with more cards in the cup outweigh ones with fewer. 1 = proportional " +
             "(3 Sour vs 1 Sweet = 3:1), 2 = squared (9:1). Higher keeps a cup mostly of one flavor that flavor's color.")]
    [Range(1f, 4f)]
    public float dominanceSharpness = 2f;
    [Tooltip("Extra pull for an additive's Primary Flavor (if it has one), on top of its normal share. " +
             "0 = no extra, 1 = the primary flavor counts double.")]
    [Min(0f)]
    public float primaryFlavorBonus = 0.5f;

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

        // Each additive gets ONE share of the color (scaled by `weighting`), split evenly across
        // its flavors - so a card with three flavors doesn't count three times as much as a
        // single-flavor card. A primary flavor gets a little extra on top (primaryFlavorBonus).
        // Totals per flavor, kept in the order each flavor first appeared (used to break ties).
        var order = new List<FlavorType>();
        var totals = new Dictionary<FlavorType, float>();
        void AddWeight(FlavorType f, float w)
        {
            if (!totals.ContainsKey(f)) { totals[f] = 0f; order.Add(f); }
            totals[f] += w;
        }
        float intensity = 0f;

        foreach (var additive in cup)
        {
            if (additive?.template == null) continue;
            float amount = Mathf.Abs(additive.template.amount);
            float cardWeight = weighting switch
            {
                BlendWeighting.Amount => Mathf.Max(amount, 1f),
                BlendWeighting.AmountSoftened => 1f + Mathf.Log10(1f + amount),
                _ => 1f
            };
            intensity += Mathf.Max(amount, 1f) + additive.TotalRetriggers * 5f;

            var flavors = additive.Flavors;
            bool hasPrimary = additive.template.hasPrimaryFlavor;
            int flavorCount = flavors != null ? flavors.Count : 0;
            if (flavorCount == 0 && !hasPrimary) continue; // flavorless - doesn't affect the color

            float share = flavorCount > 0 ? cardWeight / flavorCount : 0f;
            if (flavors != null)
                foreach (var flavor in flavors)
                    AddWeight(flavor, share);
            if (hasPrimary)
                AddWeight(additive.template.primaryFlavor, flavorCount > 0 ? cardWeight * primaryFlavorBonus : cardWeight);
        }

        Color blended = order.Count == 0
            ? Color.white // only flavorless additives in the cup
            : BlendFlavors(order, totals);
        blended.a = 1f;

        _targetColor = blended;
        // Bubble speed scales with intensity but is clamped so it doesn't go absurd.
        _bubbleSpeed = Mathf.Clamp(0.3f + intensity * 0.02f, 0.3f, 3f);
        _bubbleIntensity = Mathf.Clamp01(0.1f + count * 0.08f);
    }

    private Color FlavorColor(FlavorType f) => palette != null ? palette.GetColor(f) : FlavorPalette.DefaultColor(f);

    /// <summary>
    /// Mixes every flavor (weighted, then sharpened by dominanceSharpness), and then pulls that mix
    /// toward the leading flavor's own color by leadFlavorStrength. The pull is what stops two
    /// unrelated flavors averaging into a third color nobody put in the cup.
    /// </summary>
    private Color BlendFlavors(List<FlavorType> order, Dictionary<FlavorType, float> totals)
    {
        var samples = new List<(Color color, float weight)>();
        FlavorType lead = order[0];
        foreach (var f in order)
        {
            samples.Add((FlavorColor(f), Mathf.Pow(Mathf.Max(0f, totals[f]), dominanceSharpness)));
            if (totals[f] > totals[lead] + 1e-4f) lead = f; // strictly greater, so ties keep the first-added flavor
        }
        Color leadColor = FlavorColor(lead);

        if (!perceptualBlend)
            return Color.Lerp(BlendRgb(samples), leadColor, leadFlavorStrength);

        Vector3 mix = ToOklab(BlendOklab(samples, keepVividness));
        return FromOklab(Vector3.Lerp(mix, ToOklab(leadColor), leadFlavorStrength));
    }

    private static Color BlendRgb(List<(Color color, float weight)> samples)
    {
        Color sum = Color.black;
        float total = 0f;
        foreach (var (c, w) in samples) { sum += c * w; total += w; }
        return total > 0f ? sum / total : Color.white;
    }

    /// <summary>
    /// Weighted average in Oklab, a color space built so equal steps LOOK equal - mixing there
    /// gives an even in-between instead of RGB's muddy or brightness-biased result. Because
    /// averaging opposite hues pulls toward gray, the result's chroma (vividness) is pushed back
    /// toward the average chroma of the inputs by keepVividness (less so when the hues disagree).
    /// </summary>
    private static Color BlendOklab(List<(Color color, float weight)> samples, float keepVividness)
    {
        Vector3 sum = Vector3.zero;
        float chromaSum = 0f, total = 0f;
        foreach (var (c, w) in samples)
        {
            if (w <= 0f) continue;
            Vector3 lab = ToOklab(c);
            sum += lab * w;
            chromaSum += new Vector2(lab.y, lab.z).magnitude * w;
            total += w;
        }
        if (total <= 0f) return Color.white;

        Vector3 avg = sum / total;
        float avgChroma = chromaSum / total;
        float chroma = new Vector2(avg.y, avg.z).magnitude;
        if (chroma > 1e-4f && keepVividness > 0f && avgChroma > 1e-4f)
        {
            // Scaled by how much the hues agree (1 = all the same hue, 0 = they cancel out). When they
            // mostly cancel, the small leftover hue is an accident of the averaging - boosting it is
            // exactly what made random mixes turn vivid pink.
            float agreement = Mathf.Clamp01(chroma / avgChroma);
            float target = Mathf.Lerp(chroma, avgChroma, keepVividness * agreement);
            avg.y *= target / chroma;
            avg.z *= target / chroma;
        }
        return FromOklab(avg);
    }

    // sRGB <-> Oklab (Björn Ottosson's reference conversion). Palette colors are sRGB, so they're
    // linearized first and re-encoded at the end.
    private static Vector3 ToOklab(Color c)
    {
        float r = ToLinear(c.r), g = ToLinear(c.g), b = ToLinear(c.b);
        float l = Cbrt(0.4122214708f * r + 0.5363325363f * g + 0.0514459929f * b);
        float m = Cbrt(0.2119034982f * r + 0.6806995451f * g + 0.1073969566f * b);
        float s = Cbrt(0.0883024619f * r + 0.2817188376f * g + 0.6299787005f * b);
        return new Vector3(
            0.2104542553f * l + 0.7936177850f * m - 0.0040720468f * s,
            1.9779984951f * l - 2.4285922050f * m + 0.4505937099f * s,
            0.0259040371f * l + 0.7827717662f * m - 0.8086757660f * s);
    }

    private static Color FromOklab(Vector3 lab)
    {
        float l = lab.x + 0.3963377774f * lab.y + 0.2158037573f * lab.z;
        float m = lab.x - 0.1055613458f * lab.y - 0.0638541728f * lab.z;
        float s = lab.x - 0.0894841775f * lab.y - 1.2914855480f * lab.z;
        l = l * l * l; m = m * m * m; s = s * s * s;
        return new Color(
            ToGamma(4.0767416621f * l - 3.3077115913f * m + 0.2309699292f * s),
            ToGamma(-1.2684380046f * l + 2.6097574011f * m - 0.3413193965f * s),
            ToGamma(-0.0041960863f * l - 0.7034186147f * m + 1.7076147010f * s),
            1f);
    }

    private static float Cbrt(float x) => x < 0f ? -Mathf.Pow(-x, 1f / 3f) : Mathf.Pow(x, 1f / 3f);
    private static float ToLinear(float v) => v <= 0.04045f ? v / 12.92f : Mathf.Pow((v + 0.055f) / 1.055f, 2.4f);
    private static float ToGamma(float v)
    {
        v = Mathf.Max(0f, v);
        return Mathf.Clamp01(v <= 0.0031308f ? v * 12.92f : 1.055f * Mathf.Pow(v, 1f / 2.4f) - 0.055f);
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