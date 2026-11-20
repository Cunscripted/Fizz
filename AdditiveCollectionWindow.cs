#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Soda > Additive Collection. A Balatro-style "collection" grid of every AdditiveData asset in
/// the project, drawn as cards that gently float (same motion as FloatingButton: vertical bob,
/// slight sideways drift, tilt, and a shadow that deepens as the card rises). Hovering a card
/// stops its float, scales it up and shows a details box beside it.
///
/// - Click a card to select it in the Project window. Double-click to open it in the
///   Additive & Modifier Editor.
/// - Filter by name, rarity or flavor; sort by rarity, name or flavor.
/// - Card border = rarity color (RarityPalette), dots along the bottom = flavors (FlavorPalette).
///   Both palettes are found automatically; without them, built-in default colors are used.
/// </summary>
public class AdditiveCollectionWindow : EditorWindow
{
    private enum SortMode { Rarity, Name, Flavor }

    // ---------- Settings (shown in the toolbar) ----------
    private string _search = "";
    private int _rarityFilter;   // 0 = all, otherwise (Rarity)(index - 1)
    private int _flavorFilter;   // 0 = all, otherwise (FlavorType)(index - 1)
    private SortMode _sort = SortMode.Rarity;
    private float _cardWidth = 120f;
    private bool _animate = true;

    // ---------- Float / hover tuning (matches FloatingButton's defaults) ----------
    private const float FloatAmplitudeY = 4f;
    private const float FloatAmplitudeX = 1.5f;
    private const float FloatTiltDeg = 1.5f;
    private const float FloatPeriod = 2.4f;
    private const float HoverScale = 1.08f;
    private const float HoverLerpSpeed = 12f;

    // ---------- Layout ----------
    private const float CardAspect = 1.4f;   // height = width * this
    private const float Gap = 18f;
    private const float ToolbarHeight = 44f;
    private const float DetailWidth = 280f;

    private static readonly Color DefaultCardColor = new Color(0.16f, 0.16f, 0.19f);
    private static readonly Color ShadowColorIdle = new Color(0f, 0f, 0f, 0.25f);
    private static readonly Color ShadowColorHover = new Color(0f, 0f, 0f, 0.45f);
    private static readonly Vector2 ShadowOffsetIdle = new Vector2(3f, 6f);
    private static readonly Vector2 ShadowOffsetHover = new Vector2(5f, 10f);

    // ---------- State ----------
    private List<AdditiveData> _all = new List<AdditiveData>();
    private List<AdditiveData> _shown = new List<AdditiveData>();
    private readonly Dictionary<AdditiveData, float> _hoverT = new Dictionary<AdditiveData, float>();
    private FlavorPalette _flavorPalette;
    private RarityPalette _rarityPalette;
    private Vector2 _scroll;
    private AdditiveData _hovered;
    private Rect _hoveredRect;
    private double _lastTime;
    private double _lastRepaint;

    private GUIStyle _nameStyle, _detailStyle, _emptyStyle;

    private static readonly string[] RarityOptions =
        new[] { "All Rarities" }.Concat(Enum.GetNames(typeof(Rarity))).ToArray();
    private static readonly string[] FlavorOptions =
        new[] { "All Flavors" }.Concat(Enum.GetNames(typeof(FlavorType))).ToArray();

    [MenuItem("Soda/Additive Collection")]
    public static void Open()
    {
        var win = GetWindow<AdditiveCollectionWindow>("Additive Collection");
        win.minSize = new Vector2(520, 400);
    }

    private void OnEnable()
    {
        wantsMouseMove = true;
        wantsMouseEnterLeaveWindow = true;
        _lastTime = EditorApplication.timeSinceStartup;
        EditorApplication.update += Tick;
        Reload();
    }

    private void OnDisable() => EditorApplication.update -= Tick;
    private void OnFocus() => Reload();
    private void OnProjectChange() { Reload(); Repaint(); }

    /// <summary>Repaints ~60 times a second so the cards keep floating, even without mouse movement.</summary>
    private void Tick()
    {
        // Keep repainting while floating, or while the mouse is over the window (hover scale still eases).
        if (!_animate && mouseOverWindow != this) return;
        double now = EditorApplication.timeSinceStartup;
        if (now - _lastRepaint < 1.0 / 60.0) return;
        _lastRepaint = now;
        Repaint();
    }

    // ---------- Data ----------

    private void Reload()
    {
        _all = AssetDatabase.FindAssets("t:AdditiveData")
            .Select(g => AssetDatabase.LoadAssetAtPath<AdditiveData>(AssetDatabase.GUIDToAssetPath(g)))
            .Where(a => a != null)
            .ToList();

        if (_flavorPalette == null) _flavorPalette = FindFirstAsset<FlavorPalette>();
        if (_rarityPalette == null) _rarityPalette = FindFirstAsset<RarityPalette>();
        ApplyFilters();
    }

    private void ApplyFilters()
    {
        IEnumerable<AdditiveData> q = _all;

        if (!string.IsNullOrWhiteSpace(_search))
        {
            string s = _search.Trim();
            q = q.Where(a => DisplayName(a).IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0);
        }
        if (_rarityFilter > 0)
        {
            var r = (Rarity)(_rarityFilter - 1);
            q = q.Where(a => a.rarity == r);
        }
        if (_flavorFilter > 0)
        {
            var f = (FlavorType)(_flavorFilter - 1);
            q = q.Where(a => a.HasFlavor(f) || (a.hasPrimaryFlavor && a.primaryFlavor == f));
        }

        switch (_sort)
        {
            case SortMode.Rarity:
                q = q.OrderBy(a => a.rarity).ThenBy(DisplayName);
                break;
            case SortMode.Name:
                q = q.OrderBy(DisplayName);
                break;
            case SortMode.Flavor:
                q = q.OrderBy(a => MainFlavor(a) ?? (FlavorType)int.MaxValue).ThenBy(a => a.rarity).ThenBy(DisplayName);
                break;
        }

        _shown = q.ToList();
    }

    private static string DisplayName(AdditiveData a) =>
        string.IsNullOrEmpty(a.additiveName) ? a.name : a.additiveName;

    private static FlavorType? MainFlavor(AdditiveData a)
    {
        if (a.hasPrimaryFlavor) return a.primaryFlavor;
        if (a.flavors != null && a.flavors.Count > 0) return a.flavors[0];
        return null;
    }

    // ---------- GUI ----------

    private void OnGUI()
    {
        BuildStyles();

        if (Event.current.type == EventType.MouseLeaveWindow)
        {
            _hovered = null;
            Repaint();
        }

        double now = EditorApplication.timeSinceStartup;
        float dt = Mathf.Clamp((float)(now - _lastTime), 0f, 0.1f);
        if (Event.current.type == EventType.Repaint) _lastTime = now;

        DrawToolbar();

        var viewRect = new Rect(0, ToolbarHeight, position.width, position.height - ToolbarHeight);
        EditorGUI.DrawRect(viewRect, new Color(0.11f, 0.11f, 0.13f));

        if (_shown.Count == 0)
        {
            GUI.Label(viewRect, _all.Count == 0
                ? "No AdditiveData assets found in the project."
                : "No additives match these filters.", _emptyStyle);
            return;
        }

        // Grid layout
        float cardW = _cardWidth;
        float cardH = cardW * CardAspect;
        float usableW = viewRect.width - 16f; // leave room for the scrollbar
        int columns = Mathf.Max(1, Mathf.FloorToInt((usableW - Gap) / (cardW + Gap)));
        float gridW = columns * cardW + (columns - 1) * Gap;
        float startX = Mathf.Max(Gap, (usableW - gridW) / 2f);
        int rows = Mathf.CeilToInt(_shown.Count / (float)columns);
        float contentH = Gap + rows * (cardH + Gap) + Gap;
        var contentRect = new Rect(0, 0, usableW, contentH);

        _scroll = GUI.BeginScrollView(viewRect, _scroll, contentRect);

        Vector2 mouse = Event.current.mousePosition;
        bool mouseInView = mouseOverWindow == this &&
                           viewRect.Contains(mouse + new Vector2(viewRect.x, viewRect.y) - _scroll);
        AdditiveData newHovered = null;
        Rect newHoveredRect = default;

        for (int i = 0; i < _shown.Count; i++)
        {
            var data = _shown[i];
            int col = i % columns, row = i / columns;
            var baseRect = new Rect(startX + col * (cardW + Gap), Gap + row * (cardH + Gap), cardW, cardH);

            // Hit test the un-animated rect so a floating card doesn't flicker under the cursor.
            bool hovered = mouseInView && baseRect.Contains(mouse);
            if (hovered) { newHovered = data; newHoveredRect = baseRect; }

            if (mouseInView) HandleClick(baseRect, data);

            if (Event.current.type == EventType.Repaint)
                DrawCard(data, baseRect, i, hovered, now, dt);
        }

        GUI.EndScrollView();

        if (Event.current.type == EventType.Repaint || Event.current.type == EventType.MouseMove)
        {
            _hovered = newHovered;
            // Convert from scroll-content coordinates to window coordinates for the details box.
            _hoveredRect = new Rect(newHoveredRect.x - _scroll.x + viewRect.x,
                                    newHoveredRect.y - _scroll.y + viewRect.y,
                                    newHoveredRect.width, newHoveredRect.height);
        }

        // Details box goes last so it draws on top of every card.
        if (_hovered != null && Event.current.type == EventType.Repaint)
            DrawDetails(_hovered, _hoveredRect, viewRect);
    }

    private void DrawToolbar()
    {
        GUILayout.BeginArea(new Rect(6, 4, position.width - 12, ToolbarHeight - 6));
        EditorGUI.BeginChangeCheck();

        EditorGUILayout.BeginHorizontal();
        _search = EditorGUILayout.TextField(_search, EditorStyles.toolbarSearchField, GUILayout.MinWidth(120));
        _rarityFilter = EditorGUILayout.Popup(_rarityFilter, RarityOptions, GUILayout.Width(110));
        _flavorFilter = EditorGUILayout.Popup(_flavorFilter, FlavorOptions, GUILayout.Width(110));
        GUILayout.Label("Sort", GUILayout.Width(30));
        _sort = (SortMode)EditorGUILayout.EnumPopup(_sort, GUILayout.Width(70));
        if (GUILayout.Button("Refresh", GUILayout.Width(60))) Reload();
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();
        GUILayout.Label($"{_shown.Count} of {_all.Count} additives", EditorStyles.miniLabel, GUILayout.Width(130));
        GUILayout.FlexibleSpace();
        _animate = GUILayout.Toggle(_animate, "Float", GUILayout.Width(50));
        GUILayout.Label("Card Size", EditorStyles.miniLabel, GUILayout.Width(52));
        _cardWidth = GUILayout.HorizontalSlider(_cardWidth, 80f, 200f, GUILayout.Width(110));
        EditorGUILayout.EndHorizontal();

        if (EditorGUI.EndChangeCheck()) ApplyFilters();
        GUILayout.EndArea();
    }

    private void HandleClick(Rect rect, AdditiveData data)
    {
        var e = Event.current;
        if (e.type != EventType.MouseDown || e.button != 0 || !rect.Contains(e.mousePosition)) return;

        Selection.activeObject = data;
        EditorGUIUtility.PingObject(data);
        if (e.clickCount == 2) SodaEditorWindow.OpenAndSelect(data);
        e.Use();
    }

    // ---------- Card drawing ----------

    private void DrawCard(AdditiveData data, Rect baseRect, int index, bool hovered, double now, float dt)
    {
        // Hover blend (0 = idle, 1 = fully hovered), eased like FloatingButton.UpdateHover.
        _hoverT.TryGetValue(data, out float hoverT);
        hoverT = Mathf.Lerp(hoverT, hovered ? 1f : 0f, dt * HoverLerpSpeed);
        _hoverT[data] = hoverT;

        // Float, same shape as FloatingButton.UpdateFloat: vertical sine, slower sideways cosine,
        // tilt following vertical speed. Each card gets its own phase so they don't bob in sync.
        float phase = Mathf.Repeat(index * 0.618f, 1f) * FloatPeriod;
        float t = _animate ? (float)((now + phase) / FloatPeriod) : 0f;
        float floatBlend = (_animate ? 1f : 0f) * (1f - hoverT);
        float y = Mathf.Sin(t * Mathf.PI * 2f) * FloatAmplitudeY * floatBlend;
        float x = Mathf.Cos(t * Mathf.PI * 2f * 0.61f) * FloatAmplitudeX * floatBlend;
        float tilt = Mathf.Cos(t * Mathf.PI * 2f) * FloatTiltDeg * floatBlend;

        float scale = Mathf.Lerp(1f, HoverScale, hoverT);
        var rect = ScaleAround(baseRect, scale);
        rect.position += new Vector2(x, -y); // GUI y points down, so "up" is negative

        // Shadow: deeper when the card is higher, and when hovered.
        float height01 = Mathf.InverseLerp(-FloatAmplitudeY, FloatAmplitudeY, y / Mathf.Max(floatBlend, 0.0001f));
        Vector2 shadowOffset = Vector2.Lerp(
            Vector2.Lerp(ShadowOffsetIdle * 0.6f, ShadowOffsetIdle * 1.4f, height01 * floatBlend),
            ShadowOffsetHover, hoverT);
        Color shadowColor = Color.Lerp(ShadowColorIdle, ShadowColorHover, hoverT);

        Matrix4x4 oldMatrix = GUI.matrix;
        GUIUtility.RotateAroundPivot(tilt, rect.center);

        float radius = rect.width * 0.08f;
        var shadowRect = new Rect(rect.position + shadowOffset, rect.size);
        GUI.DrawTexture(shadowRect, EditorGUIUtility.whiteTexture, ScaleMode.StretchToFill, true, 0, shadowColor, 0, radius);

        Color rarityColor = RarityColor(data.rarity);
        Color bg = Color.Lerp(DefaultCardColor, rarityColor, 0.12f);
        GUI.DrawTexture(rect, EditorGUIUtility.whiteTexture, ScaleMode.StretchToFill, true, 0, bg, 0, radius);
        GUI.DrawTexture(rect, EditorGUIUtility.whiteTexture, ScaleMode.StretchToFill, true, 0,
                        Color.Lerp(rarityColor, Color.white, hoverT * 0.3f), Mathf.Max(2f, rect.width * 0.025f), radius);

        // Icon
        float pad = rect.width * 0.1f;
        var iconRect = new Rect(rect.x + pad, rect.y + pad, rect.width - pad * 2f, rect.height * 0.58f);
        DrawSprite(data.icon, iconRect);

        // Name
        var nameRect = new Rect(rect.x + 4f, iconRect.yMax + 2f, rect.width - 8f, rect.height * 0.2f);
        _nameStyle.fontSize = Mathf.RoundToInt(Mathf.Clamp(rect.width * 0.1f, 9f, 16f));
        GUI.Label(nameRect, DisplayName(data), _nameStyle);

        // Flavor dots - primary flavor first and a little bigger
        DrawFlavorDots(data, new Rect(rect.x, rect.yMax - rect.height * 0.12f, rect.width, rect.height * 0.08f));

        GUI.matrix = oldMatrix;
    }

    private void DrawFlavorDots(AdditiveData data, Rect area)
    {
        var dots = new List<(FlavorType flavor, bool primary)>();
        if (data.hasPrimaryFlavor) dots.Add((data.primaryFlavor, true));
        if (data.flavors != null)
            foreach (var f in data.flavors)
                if (!(data.hasPrimaryFlavor && f == data.primaryFlavor)) dots.Add((f, false));
        if (dots.Count == 0) return;

        float size = Mathf.Clamp(area.height, 6f, 14f);
        float spacing = size * 0.45f;
        float total = 0f;
        foreach (var d in dots) total += (d.primary ? size * 1.35f : size) + spacing;
        total -= spacing;

        float cx = area.center.x - total / 2f;
        foreach (var d in dots)
        {
            float s = d.primary ? size * 1.35f : size;
            var r = new Rect(cx, area.center.y - s / 2f, s, s);
            GUI.DrawTexture(r, EditorGUIUtility.whiteTexture, ScaleMode.StretchToFill, true, 0, FlavorColor(d.flavor), 0, s / 2f);
            if (d.primary)
                GUI.DrawTexture(r, EditorGUIUtility.whiteTexture, ScaleMode.StretchToFill, true, 0, new Color(1f, 1f, 1f, 0.85f), 1.5f, s / 2f);
            cx += s + spacing;
        }
    }

    private void DrawSprite(Sprite sprite, Rect area)
    {
        if (sprite == null || sprite.texture == null)
        {
            GUI.DrawTexture(area, EditorGUIUtility.whiteTexture, ScaleMode.StretchToFill, true, 0, new Color(1f, 1f, 1f, 0.05f), 0, 6f);
            GUI.Label(area, "?", _emptyStyle);
            return;
        }

        // Works for sprites packed in a sheet/atlas too - only draws this sprite's part of the texture.
        Rect tr = sprite.textureRect;
        var tex = sprite.texture;
        var uv = new Rect(tr.x / tex.width, tr.y / tex.height, tr.width / tex.width, tr.height / tex.height);

        float aspect = tr.width / tr.height;
        var fit = area;
        if (aspect > area.width / area.height)
        {
            fit.height = area.width / aspect;
            fit.y += (area.height - fit.height) / 2f;
        }
        else
        {
            fit.width = area.height * aspect;
            fit.x += (area.width - fit.width) / 2f;
        }
        GUI.DrawTextureWithTexCoords(fit, tex, uv, true);
    }

    // ---------- Details box ----------

    private void DrawDetails(AdditiveData data, Rect cardRect, Rect viewRect)
    {
        var content = new GUIContent(BuildDetailsText(data));
        float h = _detailStyle.CalcHeight(content, DetailWidth);

        // Prefer the right side of the card; flip to the left if there isn't room.
        float x = cardRect.xMax + 12f;
        if (x + DetailWidth > position.width - 8f) x = cardRect.x - DetailWidth - 12f;
        x = Mathf.Clamp(x, 8f, Mathf.Max(8f, position.width - DetailWidth - 8f));
        float y = Mathf.Clamp(cardRect.y, viewRect.y + 8f, Mathf.Max(viewRect.y + 8f, position.height - h - 8f));
        var box = new Rect(x, y, DetailWidth, h);

        Color rarityColor = RarityColor(data.rarity);
        GUI.DrawTexture(new Rect(box.position + new Vector2(4f, 6f), box.size), EditorGUIUtility.whiteTexture,
                        ScaleMode.StretchToFill, true, 0, new Color(0f, 0f, 0f, 0.4f), 0, 8f);
        GUI.DrawTexture(box, EditorGUIUtility.whiteTexture, ScaleMode.StretchToFill, true, 0, new Color(0.09f, 0.09f, 0.11f, 0.97f), 0, 8f);
        GUI.DrawTexture(box, EditorGUIUtility.whiteTexture, ScaleMode.StretchToFill, true, 0, rarityColor, 2f, 8f);
        GUI.Label(box, content, _detailStyle);
    }

    private string BuildDetailsText(AdditiveData d)
    {
        var sb = new StringBuilder();
        sb.Append($"<size=15><b>{DisplayName(d)}</b></size>\n");
        sb.Append($"<color=#{Hex(RarityColor(d.rarity))}>{d.rarity}</color>\n\n");

        // Flavors: primary first, larger; every flavor in its palette color.
        var parts = new List<string>();
        if (d.hasPrimaryFlavor)
            parts.Add($"<size=14><b><color=#{Hex(FlavorColor(d.primaryFlavor))}>{d.primaryFlavor}</color></b></size>");
        if (d.flavors != null)
            foreach (var f in d.flavors)
                if (!(d.hasPrimaryFlavor && f == d.primaryFlavor))
                    parts.Add($"<color=#{Hex(FlavorColor(f))}>{f}</color>");
        sb.Append(parts.Count > 0 ? string.Join(", ", parts) : "<color=#808080>No flavor</color>").Append("\n\n");

        string effect = AdditiveDetailPanel.FormatEffect(d);
        if (!string.IsNullOrEmpty(effect)) sb.Append(effect).Append('\n');
        if (d.baseRetriggerCount > 0)
            sb.Append($"+{d.baseRetriggerCount} retrigger{(d.baseRetriggerCount > 1 ? "s" : "")}\n");
        if (d.extraPoints != 0f) sb.Append($"<color=#66CCFF>{ScoreFormat.Signed(d.extraPoints)} Points</color>\n");
        if (d.extraMult != 0f) sb.Append($"<color=#FF6666>{ScoreFormat.Signed(d.extraMult)} Mult</color>\n");
        if (d.extraXMult != 0f && !Mathf.Approximately(d.extraXMult, 1f))
            sb.Append($"<color=#FF6666>x{d.extraXMult:0.##} Mult</color>\n");

        if (d.deleteSelfAfterUse)
            sb.Append($"<color=#FF9966>Deletes itself {(d.deleteSelfOnlyOnce ? "after first use" : "after use")}" +
                      $"{(d.deleteSelfOnlyWhenInCup ? " (only when played)" : "")}</color>\n");
        if (d.amountChangePerUse > 0f) sb.Append($"<color=#88DD88>Grows +{d.amountChangePerUse:0.#} per use</color>\n");
        else if (d.amountChangePerUse < 0f) sb.Append($"<color=#FF9966>Shrinks {d.amountChangePerUse:0.#} per use</color>\n");

        if (d.startsWithTimelessSeal)
            sb.Append("<color=#E8D9A8>Starts with a Timeless Seal</color>\n");
        if (!d.affectedByPopularity || !d.addsPopularityHeat)
            sb.Append("<color=#A0A0A0>" + (!d.affectedByPopularity && !d.addsPopularityHeat ? "Ignores popularity"
                     : !d.affectedByPopularity ? "Unaffected by popularity" : "Doesn't add heat") + "</color>\n");

        if (!string.IsNullOrWhiteSpace(d.description))
            sb.Append($"\n<i><color=#B0B0B0>{d.description.Trim()}</color></i>\n");

        sb.Append($"\n<size=9><color=#707070>{d.effectType}  ·  double-click to edit</color></size>");
        return sb.ToString();
    }

    // ---------- Helpers ----------

    private void BuildStyles()
    {
        if (_nameStyle != null) return;
        _nameStyle = new GUIStyle(EditorStyles.boldLabel)
        {
            alignment = TextAnchor.MiddleCenter,
            wordWrap = true,
            clipping = TextClipping.Clip,
            normal = { textColor = new Color(0.95f, 0.95f, 0.95f) }
        };
        _detailStyle = new GUIStyle(EditorStyles.label)
        {
            richText = true,
            wordWrap = true,
            fontSize = 12,
            padding = new RectOffset(12, 12, 10, 10),
            normal = { textColor = new Color(0.92f, 0.92f, 0.92f) }
        };
        _emptyStyle = new GUIStyle(EditorStyles.centeredGreyMiniLabel) { fontSize = 13 };
    }

    private Color FlavorColor(FlavorType f) =>
        _flavorPalette != null ? _flavorPalette.GetColor(f) : FlavorPalette.DefaultColor(f);

    private Color RarityColor(Rarity r)
    {
        if (_rarityPalette != null) return _rarityPalette.GetColor(r);
        switch (r)
        {
            case Rarity.Uncommon:  return new Color(0.35f, 0.9f, 0.45f);
            case Rarity.Rare:      return new Color(0.3f, 0.55f, 1f);
            case Rarity.Legendary: return new Color(1f, 0.8f, 0.2f);
            default:               return new Color(0.85f, 0.85f, 0.85f);
        }
    }

    private static string Hex(Color c) => ColorUtility.ToHtmlStringRGB(c);

    private static Rect ScaleAround(Rect r, float scale)
    {
        var size = r.size * scale;
        return new Rect(r.center - size / 2f, size);
    }

    private static T FindFirstAsset<T>() where T : UnityEngine.Object
    {
        var guid = AssetDatabase.FindAssets("t:" + typeof(T).Name).FirstOrDefault();
        return guid == null ? null : AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(guid));
    }
}
#endif