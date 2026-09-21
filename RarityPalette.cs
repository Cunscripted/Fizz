using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Central place to look up the shine color + shininess amount for each Rarity tier.
/// Mirrors FlavorPalette's pattern - keeping this as a ScriptableObject singleton means
/// RarityShineController (and anything else that cares) pulls from one tunable source
/// of truth instead of every card hardcoding its own rarity colors.
/// </summary>
[CreateAssetMenu(fileName = "RarityPalette", menuName = "Soda/Rarity Palette")]
public class RarityPalette : ScriptableObject
{
    [System.Serializable]
    public struct RarityEntry
    {
        public Rarity rarity;
        [Tooltip("Tint/sweep/rim-glow color the RarityShine shader uses for this tier.")]
        public Color color;
        [Tooltip("0 = plain, no shine at all. 1 = maximum tint strength + foil sweep + edge glow.")]
        [Range(0f, 1f)] public float shininess;
    }

    public List<RarityEntry> entries = new List<RarityEntry>
    {
        new RarityEntry { rarity = Rarity.Common,    color = new Color(0.85f, 0.85f, 0.85f, 1f), shininess = 0.05f },
        new RarityEntry { rarity = Rarity.Uncommon,  color = new Color(0.35f, 0.9f,  0.45f, 1f), shininess = 0.35f },
        new RarityEntry { rarity = Rarity.Rare,      color = new Color(0.3f,  0.55f, 1f,    1f), shininess = 0.65f },
        new RarityEntry { rarity = Rarity.Legendary, color = new Color(1f,    0.8f,  0.2f,  1f), shininess = 1f },
    };

    private Dictionary<Rarity, RarityEntry> _lookup;

    private void BuildLookup()
    {
        if (_lookup != null) return;
        _lookup = new Dictionary<Rarity, RarityEntry>();
        foreach (var e in entries) _lookup[e.rarity] = e;
    }

    public Color GetColor(Rarity rarity)
    {
        BuildLookup();
        return _lookup.TryGetValue(rarity, out var e) ? e.color : Color.white;
    }

    public float GetShininess(Rarity rarity)
    {
        BuildLookup();
        return _lookup.TryGetValue(rarity, out var e) ? e.shininess : 0f;
    }
}
