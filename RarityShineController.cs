using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Applies a per-rarity tint + shine (animated foil sweep + edge glow, via the
/// RarityShine shader) to whichever Renderer or UI Graphic sits on this GameObject -
/// same dual-path/material-cloning pattern as SodaColorController and
/// ConveyorBeltShaderController. Higher rarities read as both more strongly colored
/// AND visibly shinier, driven entirely by RarityPalette so the mapping lives in one
/// tunable asset instead of being hardcoded per card.
///
/// Typically placed on the same GameObject as (or wired to) an AdditiveCard's artwork
/// Image (or a dedicated shine-overlay Image, if you'd rather leave the icon itself
/// untouched) - assign it to AdditiveCard.rarityShine and it's applied automatically
/// every time that card's `instance` is (re)assigned. Leave AdditiveCard.rarityShine
/// unset on prefabs that shouldn't get the effect (e.g. plain belt/bottle cards) -
/// wiring it only on ShopMenuController's offerCardPrefab is what scopes this to the
/// shop specifically.
///
/// Requires the target Graphic's material to actually use the RarityShine shader -
/// this component only pushes property values, it doesn't assign the shader itself.
/// </summary>
public class RarityShineController : MonoBehaviour
{
    public RarityPalette palette;

    private Renderer _renderer;
    private Graphic _graphic;
    private MaterialPropertyBlock _mpb;
    private Material _instancedMaterial; // only used for the Graphic (UI) path

    private static readonly int RarityColorID = Shader.PropertyToID("_RarityColor");
    private static readonly int ShininessID = Shader.PropertyToID("_Shininess");

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
                Debug.LogWarning($"{nameof(RarityShineController)} on '{name}' found neither a Renderer nor a " +
                                  "Graphic with a material assigned - rarity shine won't visually apply. Make " +
                                  "sure the Graphic's material uses the RarityShine shader.");
            }
        }
    }

    /// <summary>Call whenever the card's additive (and therefore its rarity) is (re)assigned.</summary>
    public void Apply(Rarity rarity)
    {
        if (palette == null)
        {
            Debug.LogWarning($"{nameof(RarityShineController)} on '{name}' has no palette assigned - can't look up a color/shininess for {rarity}.", this);
            return;
        }

        Color color = palette.GetColor(rarity);
        float shininess = palette.GetShininess(rarity);

        if (_renderer != null)
        {
            _renderer.GetPropertyBlock(_mpb);
            _mpb.SetColor(RarityColorID, color);
            _mpb.SetFloat(ShininessID, shininess);
            _renderer.SetPropertyBlock(_mpb);
        }
        else if (_instancedMaterial != null)
        {
            _instancedMaterial.SetColor(RarityColorID, color);
            _instancedMaterial.SetFloat(ShininessID, shininess);
        }
    }
}
