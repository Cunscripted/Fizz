using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Visual cue on an AdditiveCard showing its flavor popularity (see PopularityManager):
///   - Trending  -> a soft glowing border that shimmers between two purples
///   - Stale     -> a faint warm border
///   - Overdone  -> a dull grey border and the card art dimmed/greyed out
///   - Fresh     -> nothing
///
/// AdditiveCard adds this automatically (see AdditiveCard.showPopularityCue), so no setup
/// is needed. It builds its own glow-border overlay (a generated soft-edged 9-sliced
/// sprite) on top of the card. To use your own border art instead, add this component to
/// the card prefab yourself and drag an Image into Border Image - it'll tint that instead.
/// </summary>
[RequireComponent(typeof(AdditiveCard))]
public class PopularityCardCue : MonoBehaviour
{
    [Tooltip("Optional - your own border/overlay Image. Left empty, a soft glow border is generated at runtime.")]
    public Image borderImage;
    [Tooltip("How far the generated border extends past the card's edges, in UI units.")]
    public float borderOutset = 6f;

    [Header("Trending (shimmering purple)")]
    public Color trendingColorA = new Color(0.62f, 0.3f, 1f, 1f);
    public Color trendingColorB = new Color(0.9f, 0.6f, 1f, 1f);
    [Tooltip("Shimmer cycles per second.")]
    public float shimmerSpeed = 1.6f;
    [Range(0f, 1f)] public float trendingMinAlpha = 0.45f;
    [Range(0f, 1f)] public float trendingMaxAlpha = 1f;

    [Header("Stale")]
    public Color staleColor = new Color(1f, 0.7f, 0.35f, 0.45f);

    [Header("Overdone")]
    public Color overdoneBorderColor = new Color(0.45f, 0.45f, 0.45f, 0.8f);
    [Tooltip("Tint multiplied onto the card's artwork while overdone.")]
    public Color overdoneArtTint = new Color(0.6f, 0.6f, 0.6f, 1f);

    private AdditiveCard _card;
    private PopularityManager.Status _status = PopularityManager.Status.Fresh;
    private Color _artBaseColor = Color.white;
    private bool _artTinted;
    private float _phaseOffset;

    private static Sprite _glowSprite;

    private void Awake()
    {
        _card = GetComponent<AdditiveCard>();
        _phaseOffset = Random.value * 10f; // so a row of trending cards doesn't pulse in lockstep
        if (borderImage == null) borderImage = CreateBorder();
        borderImage.enabled = false;
    }

    private void OnEnable()
    {
        PopularityManager.OnPopularityChanged += Refresh;
        Refresh();
    }

    private void OnDisable()
    {
        PopularityManager.OnPopularityChanged -= Refresh;
    }

    /// <summary>Re-reads this card's popularity status. Called automatically; also call it after changing the card's instance.</summary>
    public void Refresh()
    {
        var pm = PopularityManager.Instance;
        _status = (pm != null && _card != null && _card.instance != null)
            ? pm.GetDominantStatus(_card.instance)
            : PopularityManager.Status.Fresh;

        if (borderImage != null)
        {
            borderImage.enabled = _status != PopularityManager.Status.Fresh;
            // Keep the border drawn above the card's other children (art, labels) even if
            // something else got added after it.
            if (borderImage.transform.parent == transform) borderImage.transform.SetAsLastSibling();
        }

        ApplyArtTint(_status == PopularityManager.Status.Overdone);
        ApplyStaticColor();
    }

    private void Update()
    {
        if (_status != PopularityManager.Status.Trending || borderImage == null) return;

        // Shimmer: color drifts between the two purples while alpha breathes in and out.
        float t = (Mathf.Sin((Time.unscaledTime + _phaseOffset) * shimmerSpeed * Mathf.PI * 2f) + 1f) * 0.5f;
        Color c = Color.Lerp(trendingColorA, trendingColorB, t);
        c.a = Mathf.Lerp(trendingMinAlpha, trendingMaxAlpha, t);
        borderImage.color = c;
    }

    private void ApplyStaticColor()
    {
        if (borderImage == null) return;
        switch (_status)
        {
            case PopularityManager.Status.Stale: borderImage.color = staleColor; break;
            case PopularityManager.Status.Overdone: borderImage.color = overdoneBorderColor; break;
            case PopularityManager.Status.Trending: borderImage.color = trendingColorA; break;
        }
    }

    private void ApplyArtTint(bool tint)
    {
        var art = _card != null ? _card.artworkImage : null;
        if (art == null) return;

        if (tint && !_artTinted)
        {
            _artBaseColor = art.color;
            art.color = _artBaseColor * overdoneArtTint;
            _artTinted = true;
        }
        else if (!tint && _artTinted)
        {
            art.color = _artBaseColor;
            _artTinted = false;
        }
    }

    private Image CreateBorder()
    {
        var go = new GameObject("PopularityBorder (auto)", typeof(RectTransform), typeof(Image));
        var rect = (RectTransform)go.transform;
        rect.SetParent(transform, false);
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(-borderOutset, -borderOutset);
        rect.offsetMax = new Vector2(borderOutset, borderOutset);
        rect.SetAsLastSibling();

        var img = go.GetComponent<Image>();
        img.sprite = GetGlowSprite();
        img.type = Image.Type.Sliced;
        img.raycastTarget = false; // never steal drags/clicks from the card
        return img;
    }

    /// <summary>
    /// Generates (once, shared by every card) a white 64x64 soft frame: opaque at the edge,
    /// fading to transparent inward, 9-sliced so it stretches to any card size without the
    /// glow getting fatter. Tinted per status via Image.color.
    /// </summary>
    private static Sprite GetGlowSprite()
    {
        if (_glowSprite != null) return _glowSprite;

        const int size = 64;
        const float glowWidth = 14f;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float distToEdge = Mathf.Min(Mathf.Min(x, size - 1 - x), Mathf.Min(y, size - 1 - y));
                float a = 1f - Mathf.Clamp01(distToEdge / glowWidth);
                a = a * a; // soft falloff toward the middle
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        }
        tex.Apply();

        const float border = 20f;
        _glowSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0,
                                    SpriteMeshType.FullRect, new Vector4(border, border, border, border));
        _glowSprite.name = "PopularityGlow (generated)";
        return _glowSprite;
    }
}
