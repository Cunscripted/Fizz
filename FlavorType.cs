using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The flavor "families" of the game. Every Additive has 1+ of these, and all
/// per-flavor scaling/synergy mechanics (belt counting, syrup buffs, combos)
/// key off this enum. Add new families here and register a color in FlavorPalette.
///
/// NOTE: new flavors MUST go at the END. Unity saves enums as their number, so inserting
/// one in the middle would silently change the flavor of every existing additive asset
/// that uses a later entry.
/// </summary>
public enum FlavorType
{
    Sour,
    Sweet,
    Savory,
    Smooth,
    Fizzy,
    Aftertaste,
    Salty,
    Concentrates,
    Waste,
    Bitter,
    Fatty,
    Umami,
    Spicy,
    Solid,
    Liquid,
    Fruit,
    Vegetable,
    Conceptual,
    Mommy,
    Garnish
}

/// <summary>
/// Central place to look up the visual color for a flavor.
/// Keeping this as a ScriptableObject singleton means the shader,
/// UI icons, and card art all pull from one source of truth.
/// </summary>
[CreateAssetMenu(fileName = "FlavorPalette", menuName = "Soda/Flavor Palette")]
public class FlavorPalette : ScriptableObject
{
    [System.Serializable]
    public struct FlavorColorEntry
    {
        public FlavorType flavor;
        public Color color;
    }

    // Built-in starting colors. A palette asset you've already made keeps its own list - any flavor
    // missing from it (e.g. one added after the asset was created) falls back to the color here
    // instead of plain white. Add the flavor to the asset's Entries to pick your own color.
    private static readonly Dictionary<FlavorType, Color> Defaults = new Dictionary<FlavorType, Color>
    {
        { FlavorType.Sour,         new Color(0.78f, 1f, 0.2f) },
        { FlavorType.Sweet,        new Color(1f, 0.55f, 0.75f) },
        { FlavorType.Savory,       new Color(0.6f, 0.35f, 0.15f) },
        { FlavorType.Smooth,       new Color(0.88f, 0.9f, 0.95f) },
        { FlavorType.Fizzy,        new Color(0.55f, 0.95f, 1f) },
        { FlavorType.Aftertaste,   new Color(0.4f, 0.12f, 0.5f) },
        { FlavorType.Salty,        new Color(0.5f, 0.5f, 0.5f) },
        { FlavorType.Concentrates, new Color(0.95f, 0.6f, 0.05f) },
        { FlavorType.Waste,        new Color(0.2f, 0.2f, 0.2f) },
        { FlavorType.Bitter,       new Color(0.35f, 0.45f, 0.2f) },  // dark olive
        { FlavorType.Umami,        new Color(0.85f, 0.25f, 0.3f) },  // deep red
        { FlavorType.Solid,        new Color(0.25f, 0.25f, 0.25f) },  // stone tan
        { FlavorType.Liquid,       new Color(0.2f, 0.45f, 0.95f) },  // blue
        { FlavorType.Fruit,        new Color(1f, 0.25f, 0.0f) },     // coral
        { FlavorType.Spicy,        new Color(1f, 0.45f, 0.0f) },
        { FlavorType.Vegetable,    new Color(0.25f, 0.75f, 0.3f) },  // leaf green
        { FlavorType.Fatty,        new Color(0.85f, 0.75f, 0.13f) }, //fatass yello
        { FlavorType.Conceptual,   new Color(0.65f, 0.5f, 1f) },     // lavender
        { FlavorType.Mommy,        new Color(1f, 1f, 1f) },
        { FlavorType.Garnish,      new Color(.83f, .69f, .22f) },
    };

    public List<FlavorColorEntry> entries = BuildDefaultEntries();

    private static List<FlavorColorEntry> BuildDefaultEntries()
    {
        var list = new List<FlavorColorEntry>();
        foreach (var kv in Defaults) list.Add(new FlavorColorEntry { flavor = kv.Key, color = kv.Value });
        return list;
    }

    private Dictionary<FlavorType, Color> _lookup;

    public Color GetColor(FlavorType flavor)
    {
        if (_lookup == null)
        {
            _lookup = new Dictionary<FlavorType, Color>();
            foreach (var e in entries) _lookup[e.flavor] = e.color;
        }
        return _lookup.TryGetValue(flavor, out var c) ? c : DefaultColor(flavor);
    }

    /// <summary>The built-in color for a flavor - used when no palette asset is assigned, or it has no entry for this flavor.</summary>
    public static Color DefaultColor(FlavorType flavor) => Defaults.TryGetValue(flavor, out var c) ? c : Color.white;

    // Rebuild the lookup when the asset is edited, so color changes show up without restarting play mode.
    private void OnValidate() => _lookup = null;
}