#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Soda > Flavor Distribution. Shows how many additives carry each flavor as colored bars
/// (FlavorPalette colors), so you can check every flavor is fairly represented.
///
/// - Source: every AdditiveData asset in the project, the ShopManager pool in the open
///   scene, or the RoundManager's starting deck.
/// - Rarity filters, and an option to weight each additive by its shop rarity weight
///   (how often it actually shows up in offers rather than just whether it exists).
/// - A dashed line marks the average; counts well below/above it are flagged.
/// - Click a flavor to expand the list of additives that carry it (click one to select it).
/// An additive with several flavors counts once for EACH of its flavors.
/// </summary>
public class FlavorDistributionWindow : EditorWindow
{
    private enum Source { AllAssetsInProject, ShopPoolInScene, StartingDeckInScene }

    private Source _source = Source.AllAssetsInProject;
    private FlavorPalette _palette;
    private bool[] _rarityEnabled = { true, true, true, true };
    private bool _weightByShopOdds;
    private bool _excludeWasteFromAverage = true;
    private float _tolerance = 0.35f;

    private readonly Dictionary<FlavorType, List<AdditiveData>> _byFlavor = new Dictionary<FlavorType, List<AdditiveData>>();
    private readonly Dictionary<FlavorType, float> _values = new Dictionary<FlavorType, float>();
    private readonly HashSet<FlavorType> _expanded = new HashSet<FlavorType>();
    private List<AdditiveData> _noFlavor = new List<AdditiveData>();
    private int _totalAdditives;
    private int _multiFlavor;
    private string _sourceNote = "";
    private Vector2 _scroll;

    private static readonly FlavorType[] Flavors = (FlavorType[])Enum.GetValues(typeof(FlavorType));

    [MenuItem("Soda/Flavor Distribution")]
    public static void Open()
    {
        var win = GetWindow<FlavorDistributionWindow>("Flavor Distribution");
        win.minSize = new Vector2(460, 360);
    }

    private void OnEnable()
    {
        if (_palette == null) _palette = FindFirstAsset<FlavorPalette>();
        Recalculate();
    }

    private void OnFocus() => Recalculate();
    private void OnProjectChange() { Recalculate(); Repaint(); }

    private void OnGUI()
    {
        DrawOptions();
        EditorGUILayout.Space(6);
        DrawSummary();
        EditorGUILayout.Space(6);

        _scroll = EditorGUILayout.BeginScrollView(_scroll);
        DrawBars();
        EditorGUILayout.EndScrollView();
    }

    // ---------- UI ----------

    private void DrawOptions()
    {
        EditorGUI.BeginChangeCheck();

        EditorGUILayout.BeginHorizontal();
        _source = (Source)EditorGUILayout.EnumPopup("Source", _source);
        if (GUILayout.Button("Refresh", GUILayout.Width(70))) Recalculate();
        EditorGUILayout.EndHorizontal();

        _palette = (FlavorPalette)EditorGUILayout.ObjectField("Flavor Palette", _palette, typeof(FlavorPalette), false);

        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.PrefixLabel("Rarities");
        var rarities = (Rarity[])Enum.GetValues(typeof(Rarity));
        for (int i = 0; i < rarities.Length && i < _rarityEnabled.Length; i++)
            _rarityEnabled[i] = GUILayout.Toggle(_rarityEnabled[i], rarities[i].ToString(), "Button");
        EditorGUILayout.EndHorizontal();

        _weightByShopOdds = EditorGUILayout.ToggleLeft(
            new GUIContent("Weight by shop rarity odds",
                "Counts each additive by its rarity's base shop weight (from the ShopManager in the open scene, or " +
                "the default 100/40/12/3) - shows how often each flavor is actually OFFERED, not just how many exist."),
            _weightByShopOdds);
        _excludeWasteFromAverage = EditorGUILayout.ToggleLeft(
            new GUIContent("Leave Waste out of the average", "Waste is usually meant to be rare - don't let it drag the target line down."),
            _excludeWasteFromAverage);
        _tolerance = EditorGUILayout.Slider(new GUIContent("Flag if off average by", "Bars more than this share above/below the average are flagged."),
                                            _tolerance, 0.1f, 0.9f);

        if (EditorGUI.EndChangeCheck()) Recalculate();
    }

    private void DrawSummary()
    {
        if (!string.IsNullOrEmpty(_sourceNote))
            EditorGUILayout.HelpBox(_sourceNote, MessageType.Info);

        float avg = Average();
        var counted = Flavors.Where(CountsTowardAverage).Select(f => _values[f]).ToList();
        float min = counted.Count > 0 ? counted.Min() : 0f;
        float max = counted.Count > 0 ? counted.Max() : 0f;
        string balance = max > 0f ? $"{min / max:P0}" : "-";

        EditorGUILayout.LabelField(
            $"{_totalAdditives} additives  |  {_multiFlavor} multi-flavor  |  {_noFlavor.Count} with no flavor  |  " +
            $"average {avg:0.#} per flavor  |  balance (smallest / largest) {balance}",
            EditorStyles.wordWrappedMiniLabel);
    }

    private void DrawBars()
    {
        float maxValue = Mathf.Max(1f, _values.Count > 0 ? _values.Values.Max() : 1f);
        float avg = Average();
        const float labelWidth = 110f;
        const float valueWidth = 120f;
        const float barHeight = 22f;

        foreach (var flavor in Flavors)
        {
            float value = _values.TryGetValue(flavor, out var v) ? v : 0f;
            int count = _byFlavor.TryGetValue(flavor, out var list) ? list.Count : 0;
            Color color = _palette != null ? _palette.GetColor(flavor) : Color.gray;

            Rect row = GUILayoutUtility.GetRect(10f, barHeight + 4f, GUILayout.ExpandWidth(true));
            Rect labelRect = new Rect(row.x, row.y + 2f, labelWidth, barHeight);
            Rect barArea = new Rect(row.x + labelWidth, row.y + 2f, row.width - labelWidth - valueWidth - 8f, barHeight);
            Rect valueRect = new Rect(barArea.xMax + 8f, row.y + 2f, valueWidth, barHeight);

            // Flavor name - click to expand/collapse its additive list.
            bool expanded = _expanded.Contains(flavor);
            if (GUI.Button(labelRect, $"{(expanded ? "▾" : "▸")} {flavor}", EditorStyles.label))
            {
                if (expanded) _expanded.Remove(flavor); else _expanded.Add(flavor);
            }

            // Bar
            EditorGUI.DrawRect(barArea, new Color(0f, 0f, 0f, 0.25f));
            var fill = new Rect(barArea.x, barArea.y, barArea.width * (value / maxValue), barArea.height);
            EditorGUI.DrawRect(fill, color);

            // Average marker (dashed vertical line)
            if (avg > 0f)
            {
                float x = barArea.x + barArea.width * Mathf.Clamp01(avg / maxValue);
                for (float y = barArea.y; y < barArea.yMax; y += 4f)
                    EditorGUI.DrawRect(new Rect(x - 1f, y, 2f, 2f), new Color(1f, 1f, 1f, 0.85f));
            }

            // Value + flag
            string valueLabel = _weightByShopOdds ? $"{value:0.#} ({count})" : $"{count}";
            string flag = "";
            var style = new GUIStyle(EditorStyles.label);
            if (CountsTowardAverage(flavor) && avg > 0f)
            {
                float ratio = value / avg;
                if (ratio < 1f - _tolerance) { flag = "  ▼ low"; style.normal.textColor = new Color(1f, 0.55f, 0.35f); }
                else if (ratio > 1f + _tolerance) { flag = "  ▲ high"; style.normal.textColor = new Color(1f, 0.85f, 0.3f); }
            }
            GUI.Label(valueRect, valueLabel + flag, style);

            if (expanded) DrawAdditiveList(list);
        }

        if (_noFlavor.Count > 0)
        {
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField($"No flavor ({_noFlavor.Count})", EditorStyles.boldLabel);
            DrawAdditiveList(_noFlavor);
        }
    }

    private void DrawAdditiveList(List<AdditiveData> list)
    {
        if (list == null || list.Count == 0)
        {
            EditorGUILayout.LabelField("    (none)", EditorStyles.miniLabel);
            return;
        }

        EditorGUI.indentLevel += 2;
        foreach (var a in list.OrderBy(a => a.rarity).ThenBy(a => a.name))
        {
            if (a == null) continue;
            string label = $"{(string.IsNullOrEmpty(a.additiveName) ? a.name : a.additiveName)}   [{a.rarity}]" +
                           (a.flavors.Count > 1 ? $"   ({string.Join("/", a.flavors)})" : "");
            if (GUILayout.Button(label, EditorStyles.miniButton))
            {
                Selection.activeObject = a;
                EditorGUIUtility.PingObject(a);
            }
        }
        EditorGUI.indentLevel -= 2;
        EditorGUILayout.Space(4);
    }

    // ---------- Data ----------

    private void Recalculate()
    {
        _byFlavor.Clear();
        _values.Clear();
        foreach (var f in Flavors)
        {
            _byFlavor[f] = new List<AdditiveData>();
            _values[f] = 0f;
        }
        _noFlavor = new List<AdditiveData>();
        _multiFlavor = 0;

        var additives = GatherAdditives().Where(a => a != null && RarityEnabled(a.rarity)).ToList();
        _totalAdditives = additives.Count;
        var weights = GetRarityWeights();

        foreach (var a in additives)
        {
            if (a.flavors == null || a.flavors.Count == 0) { _noFlavor.Add(a); continue; }
            if (a.flavors.Count > 1) _multiFlavor++;

            float w = _weightByShopOdds ? weights[(int)a.rarity] : 1f;
            foreach (var f in a.flavors.Distinct())
            {
                _byFlavor[f].Add(a);
                _values[f] += w;
            }
        }
    }

    private IEnumerable<AdditiveData> GatherAdditives()
    {
        _sourceNote = "";
        switch (_source)
        {
            case Source.ShopPoolInScene:
            {
                var shop = FindInScene<ShopManager>();
                if (shop == null) { _sourceNote = "No ShopManager in the open scene - open the gameplay scene, or switch Source."; return Enumerable.Empty<AdditiveData>(); }
                // Duplicates in the pool are counted - a duplicate genuinely raises that additive's odds.
                return shop.additivePool;
            }
            case Source.StartingDeckInScene:
            {
                var round = FindInScene<RoundManager>();
                if (round == null) { _sourceNote = "No RoundManager in the open scene - open the gameplay scene, or switch Source."; return Enumerable.Empty<AdditiveData>(); }
                return round.AllStartingAdditives();
            }
            default:
                return AssetDatabase.FindAssets("t:AdditiveData")
                    .Select(g => AssetDatabase.LoadAssetAtPath<AdditiveData>(AssetDatabase.GUIDToAssetPath(g)));
        }
    }

    private float[] GetRarityWeights()
    {
        var shop = FindInScene<ShopManager>();
        return shop != null
            ? new[] { shop.commonWeight, shop.uncommonWeight, shop.rareWeight, shop.legendaryWeight }
            : new[] { 100f, 40f, 12f, 3f };
    }

    private bool RarityEnabled(Rarity r) => (int)r >= _rarityEnabled.Length || _rarityEnabled[(int)r];

    private bool CountsTowardAverage(FlavorType f) => !(_excludeWasteFromAverage && f == FlavorType.Waste);

    private float Average()
    {
        var counted = Flavors.Where(CountsTowardAverage).ToList();
        return counted.Count > 0 ? counted.Sum(f => _values.TryGetValue(f, out var v) ? v : 0f) / counted.Count : 0f;
    }

    private static T FindInScene<T>() where T : UnityEngine.Object
    {
        var all = Resources.FindObjectsOfTypeAll<T>();
        foreach (var o in all)
        {
            // Skip prefab assets / hidden objects - only real objects in a loaded scene.
            if (o is Component c && c.gameObject.scene.IsValid()) return o;
        }
        return null;
    }

    private static T FindFirstAsset<T>() where T : UnityEngine.Object
    {
        var guid = AssetDatabase.FindAssets($"t:{typeof(T).Name}").FirstOrDefault();
        return guid != null ? AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(guid)) : null;
    }
}
#endif