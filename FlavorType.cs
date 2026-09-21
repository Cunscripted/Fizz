using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The flavor "families" of the game. Every Additive has 1+ of these, and all
/// per-flavor scaling/synergy mechanics (belt counting, syrup buffs, combos)
/// key off this enum. Add new families here and register a color in FlavorPalette.
/// </summary>
public enum FlavorType
{
    Sour,
    Sweet,
    Savory,
    Smooth,
    Fizzy,
    Aftertaste,
    Concentrates,
    Waste
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

    public List<FlavorColorEntry> entries = new List<FlavorColorEntry>
    {
        new FlavorColorEntry { flavor = FlavorType.Sour,       color = new Color(0.78f, 1f, 0.2f) },
        new FlavorColorEntry { flavor = FlavorType.Sweet,      color = new Color(1f, 0.55f, 0.75f) },
        new FlavorColorEntry { flavor = FlavorType.Savory,     color = new Color(0.6f, 0.35f, 0.15f) },
        new FlavorColorEntry { flavor = FlavorType.Smooth,     color = new Color(0.88f, 0.9f, 0.95f) },
        new FlavorColorEntry { flavor = FlavorType.Fizzy,      color = new Color(0.55f, 0.95f, 1f) },
        new FlavorColorEntry { flavor = FlavorType.Aftertaste, color = new Color(0.4f, 0.12f, 0.5f) },
        new FlavorColorEntry { flavor = FlavorType.Concentrates, color = new Color(0.95f, 0.6f, 0.05f) },
        new FlavorColorEntry { flavor = FlavorType.Waste,        color = new Color (0.2f, 0.2f, 0.2f)}
    };

    private Dictionary<FlavorType, Color> _lookup;

    public Color GetColor(FlavorType flavor)
    {
        if (_lookup == null)
        {
            _lookup = new Dictionary<FlavorType, Color>();
            foreach (var e in entries) _lookup[e.flavor] = e.color;
        }
        return _lookup.TryGetValue(flavor, out var c) ? c : Color.white;
    }
}