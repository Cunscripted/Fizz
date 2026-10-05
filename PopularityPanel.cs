using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Shows every flavor's popularity (see PopularityManager). Horizontal layout (default): one
/// column per flavor, side by side - status on top, a bar filling UP toward "Overdone" (with a
/// tick where it starts going stale), the flavor name at the bottom. Vertical layout: one row per
/// flavor with the bar filling to the right. Status reads e.g. "Trending x1.25" or "Stale x0.8".
///
/// Rows are BUILT AUTOMATICALLY into rowContainer - no row prefab needed. Just give this a
/// LerpPanel and an empty RectTransform to fill (a VerticalLayoutGroup is added to it if it
/// doesn't have one). Styling is on this component. Updates live whenever popularity changes.
///
/// Designed to sit on a page of the TabletMenu, but works standalone too.
/// </summary>
public class PopularityPanel : MonoBehaviour
{
    public LerpPanel panel;
    [Tooltip("Empty RectTransform the flavor rows are generated into.")]
    public RectTransform rowContainer;
    [Tooltip("Colors the flavor names/swatches. Optional - names are white without it.")]
    public FlavorPalette palette;
    [Tooltip("Optional - a short explanation shown above/beside the rows.")]
    public TMP_Text hintText;
    [TextArea] public string hintMessage =
        "Customers get bored of flavors you serve over and over. Let a flavor rest and it'll start Trending!";

    public enum Layout
    {
        Horizontal, // flavors side by side as columns, each with a bar filling UP toward Overdone
        Vertical    // flavors stacked as rows, each with a bar filling RIGHT toward Overdone
    }

    [Header("Layout")]
    [Tooltip("Horizontal = flavors side by side (bars fill upward). Vertical = flavors stacked as rows (bars fill rightward).")]
    public Layout layout = Layout.Horizontal;

    [Header("Column Style (Horizontal layout)")]
    public float columnSpacing = 10f;
    [Tooltip("Share of each column's width taken by the bar.")]
    [Range(0.1f, 1f)] public float columnBarWidth = 0.5f;
    [Tooltip("Share of each column's height for the flavor name at the bottom.")]
    [Range(0.05f, 0.4f)] public float columnNameHeight = 0.14f;
    [Tooltip("Share of each column's height for the status text at the top (e.g. \"Trending / x1.25\").")]
    [Range(0.05f, 0.4f)] public float columnStatusHeight = 0.18f;
    [Tooltip("Smallest font the column labels may shrink to when the columns are narrow.")]
    public float minFontSize = 10f;

    [Header("Row Style (Vertical layout)")]
    public float rowHeight = 44f;
    public float rowSpacing = 6f;
    public float fontSize = 24f;
    [Tooltip("Share of the row width used by the flavor name / the bar / the status text.")]
    [Range(0.1f, 0.5f)] public float nameWidth = 0.28f;
    [Range(0.2f, 0.7f)] public float barWidth = 0.44f;
    public Color barBackgroundColor = new Color(0f, 0f, 0f, 0.35f);
    public Color freshBarColor = new Color(0.45f, 0.85f, 0.5f);
    public Color staleBarColor = new Color(1f, 0.7f, 0.3f);
    public Color overdoneBarColor = new Color(0.9f, 0.3f, 0.3f);
    public Color staleTickColor = new Color(1f, 1f, 1f, 0.6f);

    [Header("Status Text Colors")]
    public Color trendingTextColor = new Color(0.78f, 0.55f, 1f);
    public Color freshTextColor = new Color(0.85f, 0.85f, 0.85f);
    public Color staleTextColor = new Color(1f, 0.7f, 0.3f);
    public Color overdoneTextColor = new Color(0.95f, 0.4f, 0.4f);

    [Header("Filter")]
    [Tooltip("Hide flavors listed in PopularityManager.exemptFlavors (they never change).")]
    public bool hideExemptFlavors = true;

    private class Row
    {
        public FlavorType flavor;
        public GameObject root;
        public RectTransform fill;
        public Image fillImage;
        public RectTransform staleTick;
        public TMP_Text statusText;
        public bool column; // true = bar fills upward (Horizontal layout)
    }

    private readonly List<Row> _rows = new List<Row>();

    public bool IsShown => panel != null && panel.IsShown;

    [Tooltip("Set automatically by TabletMenu. While set, Toggle() just opens the Popularity tab instead of closing it.")]
    public TabletMenu tablet;

    private void OnEnable()
    {
        PopularityManager.OnPopularityChanged += Refresh;
    }

    private void OnDisable()
    {
        PopularityManager.OnPopularityChanged -= Refresh;
    }

    public void Show()
    {
        if (panel == null)
        {
            Debug.LogError($"[PopularityPanel] '{name}' has no panel (LerpPanel) assigned.", this);
            return;
        }
        panel.Show();
        if (hintText != null) hintText.text = hintMessage;
        if (_rows.Count == 0) BuildRows();
        Refresh();
    }

    public void Hide()
    {
        if (!IsShown) return;
        panel.Hide();
    }

    public void Toggle()
    {
        if (tablet != null) { tablet.ShowTab(TabletMenu.Tab.Popularity); return; }
        if (IsShown) Hide(); else Show();
    }

    /// <summary>Updates every row's bar and status from PopularityManager.</summary>
    public void Refresh()
    {
        var pm = PopularityManager.Instance;
        if (pm == null || _rows.Count == 0) return;

        foreach (var row in _rows)
        {
            if (row.root == null) continue;
            bool exempt = pm.IsExempt(row.flavor);
            row.root.SetActive(!(exempt && hideExemptFlavors));

            float progress = pm.GetOverdoneProgress(row.flavor);
            row.fill.anchorMax = row.column ? new Vector2(1f, progress) : new Vector2(progress, 1f);

            var status = pm.GetStatus(row.flavor);
            row.fillImage.color = status == PopularityManager.Status.Overdone ? overdoneBarColor
                                : status == PopularityManager.Status.Stale ? staleBarColor
                                : freshBarColor;

            float tick = pm.overdoneAt > 0f ? Mathf.Clamp01(pm.staleStartsAt / pm.overdoneAt) : 0f;
            row.staleTick.anchorMin = row.column ? new Vector2(0f, tick) : new Vector2(tick, 0f);
            row.staleTick.anchorMax = row.column ? new Vector2(1f, tick) : new Vector2(tick, 1f);

            string description = exempt ? "-" : pm.DescribeFlavor(row.flavor);
            // Columns are narrow - put "Trending" and "x1.25" on separate lines.
            row.statusText.text = row.column ? description.Replace(" x", "\nx") : description;
            row.statusText.color = status switch
            {
                PopularityManager.Status.Trending => trendingTextColor,
                PopularityManager.Status.Stale => staleTextColor,
                PopularityManager.Status.Overdone => overdoneTextColor,
                _ => freshTextColor
            };
        }
    }

    // ---------- Row construction ----------

    private void BuildRows()
    {
        if (rowContainer == null)
        {
            Debug.LogError($"[PopularityPanel] '{name}' has no rowContainer assigned - nowhere to put the flavor rows.", this);
            return;
        }

        bool columns = layout == Layout.Horizontal;

        // Only one layout group can live on an object - swap out the other kind if it's there.
        HorizontalOrVerticalLayoutGroup group;
        if (columns)
        {
            var wrong = rowContainer.GetComponent<VerticalLayoutGroup>();
            if (wrong != null) DestroyImmediate(wrong);
            group = rowContainer.GetComponent<HorizontalLayoutGroup>();
            if (group == null) group = rowContainer.gameObject.AddComponent<HorizontalLayoutGroup>();
            group.childForceExpandHeight = true; // columns use the container's full height
            group.spacing = columnSpacing;
        }
        else
        {
            var wrong = rowContainer.GetComponent<HorizontalLayoutGroup>();
            if (wrong != null) DestroyImmediate(wrong);
            group = rowContainer.GetComponent<VerticalLayoutGroup>();
            if (group == null) group = rowContainer.gameObject.AddComponent<VerticalLayoutGroup>();
            group.childForceExpandHeight = false;
            group.spacing = rowSpacing;
        }
        group.childControlWidth = true;
        group.childControlHeight = true;
        group.childForceExpandWidth = true;

        foreach (var flavor in PopularityManager.AllFlavors)
            _rows.Add(columns ? BuildColumn(flavor) : BuildRow(flavor));

        LayoutRebuilder.ForceRebuildLayoutImmediate(rowContainer);
    }

    private Row BuildRow(FlavorType flavor)
    {
        var root = NewRect($"Row_{flavor}", rowContainer);
        var le = root.gameObject.AddComponent<LayoutElement>();
        le.preferredHeight = rowHeight;
        le.minHeight = rowHeight;

        Color flavorColor = palette != null ? palette.GetColor(flavor) : Color.white;

        // Name (with a small color swatch in front of it)
        var swatch = NewRect("Swatch", root);
        Stretch(swatch, 0f, 0.2f, 0f, 0.8f);
        swatch.pivot = new Vector2(0f, 0.5f);
        swatch.sizeDelta = new Vector2(rowHeight * 0.5f, 0f);
        swatch.anchoredPosition = Vector2.zero;
        var swatchImg = swatch.gameObject.AddComponent<Image>();
        swatchImg.color = flavorColor;
        swatchImg.raycastTarget = false;

        var nameText = NewText("Name", root, flavor.ToString(), TextAlignmentOptions.MidlineLeft);
        Stretch(nameText.rectTransform, 0f, 0f, nameWidth, 1f);
        nameText.rectTransform.offsetMin = new Vector2(rowHeight * 0.5f + 8f, 0f);
        nameText.color = Color.Lerp(flavorColor, Color.white, 0.25f);

        // Bar background + fill + stale tick
        float barStart = nameWidth + 0.02f;
        float barEnd = Mathf.Min(0.98f, barStart + barWidth);
        var barBg = NewRect("Bar", root);
        Stretch(barBg, barStart, 0.25f, barEnd, 0.75f);
        var bgImg = barBg.gameObject.AddComponent<Image>();
        bgImg.color = barBackgroundColor;
        bgImg.raycastTarget = false;

        var fill = NewRect("Fill", barBg);
        Stretch(fill, 0f, 0f, 0f, 1f);
        var fillImg = fill.gameObject.AddComponent<Image>();
        fillImg.raycastTarget = false;

        var tick = NewRect("StaleTick", barBg);
        Stretch(tick, 0.5f, 0f, 0.5f, 1f);
        tick.sizeDelta = new Vector2(2f, 0f);
        var tickImg = tick.gameObject.AddComponent<Image>();
        tickImg.color = staleTickColor;
        tickImg.raycastTarget = false;

        // Status text
        var status = NewText("Status", root, "", TextAlignmentOptions.MidlineRight);
        Stretch(status.rectTransform, barEnd + 0.02f, 0f, 1f, 1f);

        return new Row { flavor = flavor, root = root.gameObject, fill = fill, fillImage = fillImg, staleTick = tick, statusText = status };
    }

    /// <summary>One flavor as a column: status on top, a bar filling upward, the flavor name at the bottom.</summary>
    private Row BuildColumn(FlavorType flavor)
    {
        var root = NewRect($"Column_{flavor}", rowContainer);
        var le = root.gameObject.AddComponent<LayoutElement>();
        le.flexibleWidth = 1f;  // every column gets an equal share of the width
        le.flexibleHeight = 1f;
        le.minWidth = 0f;

        Color flavorColor = palette != null ? palette.GetColor(flavor) : Color.white;

        // Status at the top
        var status = NewText("Status", root, "", TextAlignmentOptions.Center);
        Stretch(status.rectTransform, 0f, 1f - columnStatusHeight, 1f, 1f);
        MakeAutoSized(status);

        // Name at the bottom, in the flavor's color
        var nameText = NewText("Name", root, flavor.ToString(), TextAlignmentOptions.Center);
        Stretch(nameText.rectTransform, 0f, 0f, 1f, columnNameHeight);
        nameText.color = Color.Lerp(flavorColor, Color.white, 0.25f);
        MakeAutoSized(nameText);

        // Bar in the middle
        float half = columnBarWidth * 0.5f;
        float barBottom = columnNameHeight + 0.03f;
        float barTop = 1f - columnStatusHeight - 0.02f;
        var barBg = NewRect("Bar", root);
        Stretch(barBg, 0.5f - half, barBottom, 0.5f + half, barTop);
        var bgImg = barBg.gameObject.AddComponent<Image>();
        bgImg.color = barBackgroundColor;
        bgImg.raycastTarget = false;

        // Thin flavor-colored strip under the bar, so each column reads as its flavor at a glance
        var swatch = NewRect("Swatch", root);
        Stretch(swatch, 0.5f - half, columnNameHeight, 0.5f + half, barBottom - 0.01f);
        var swatchImg = swatch.gameObject.AddComponent<Image>();
        swatchImg.color = flavorColor;
        swatchImg.raycastTarget = false;

        var fill = NewRect("Fill", barBg);
        Stretch(fill, 0f, 0f, 1f, 0f);
        var fillImg = fill.gameObject.AddComponent<Image>();
        fillImg.raycastTarget = false;

        var tick = NewRect("StaleTick", barBg);
        Stretch(tick, 0f, 0.5f, 1f, 0.5f);
        tick.sizeDelta = new Vector2(0f, 2f);
        var tickImg = tick.gameObject.AddComponent<Image>();
        tickImg.color = staleTickColor;
        tickImg.raycastTarget = false;

        return new Row { flavor = flavor, root = root.gameObject, fill = fill, fillImage = fillImg, staleTick = tick, statusText = status, column = true };
    }

    private void MakeAutoSized(TMP_Text text)
    {
        text.enableAutoSizing = true;
        text.fontSizeMin = Mathf.Min(minFontSize, fontSize);
        text.fontSizeMax = fontSize;
    }

    private static RectTransform NewRect(string objName, Transform parent)
    {
        var go = new GameObject(objName, typeof(RectTransform));
        var rect = (RectTransform)go.transform;
        rect.SetParent(parent, false);
        return rect;
    }

    private static void Stretch(RectTransform rect, float minX, float minY, float maxX, float maxY)
    {
        rect.anchorMin = new Vector2(minX, minY);
        rect.anchorMax = new Vector2(maxX, maxY);
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    private TMP_Text NewText(string objName, Transform parent, string text, TextAlignmentOptions align)
    {
        var rect = NewRect(objName, parent);
        var tmp = rect.gameObject.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = fontSize;
        tmp.alignment = align;
        tmp.raycastTarget = false;
        tmp.overflowMode = TextOverflowModes.Ellipsis;
        return tmp;
    }
}