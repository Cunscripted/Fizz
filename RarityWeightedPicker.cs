using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Shared weighted-by-rarity random selection, used by both ShopManager (additive
/// offers) and ModifierManager (syrup offers) so the two don't duplicate the same
/// "commoner tiers show up more, skewed toward rarer tiers by some multiplier" math.
/// skew multiplies every NON-Common tier's weight - Common is left alone so it can
/// never become vanishingly rare even at a huge skew, keeping some baseline variety
/// in every offer set no matter how far the skew has climbed.
/// </summary>
public static class RarityWeightedPicker
{
    public struct Weights
    {
        public float common;
        public float uncommon;
        public float rare;
        public float legendary;
    }

    public static float BaseWeight(Rarity rarity, Weights weights)
    {
        switch (rarity)
        {
            case Rarity.Common: return weights.common;
            case Rarity.Uncommon: return weights.uncommon;
            case Rarity.Rare: return weights.rare;
            case Rarity.Legendary: return weights.legendary;
            default: return 1f;
        }
    }

    /// <summary>
    /// Picks up to `count` items from `items` (no duplicates within one call, and
    /// null entries in `items` are skipped - a convenience for Inspector lists that
    /// may have empty slots), weighted by each item's rarity via getRarity. skew
    /// multiplies every non-Common tier's weight, so a higher skew makes
    /// Uncommon/Rare/Legendary progressively more likely relative to Common without
    /// ever fully ruling Common out.
    /// </summary>
    public static List<T> PickMany<T>(List<T> items, Func<T, Rarity> getRarity, Weights weights, float skew, int count) where T : class
    {
        var pool = new List<(T item, float weight)>();
        if (items != null)
        {
            foreach (var item in items)
            {
                if (item == null) continue;
                var rarity = getRarity(item);
                float w = BaseWeight(rarity, weights);
                if (rarity != Rarity.Common) w *= skew;
                pool.Add((item, w));
            }
        }

        var results = new List<T>();
        for (int i = 0; i < count && pool.Count > 0; i++)
        {
            int pickedIndex = WeightedPickIndex(pool);
            results.Add(pool[pickedIndex].item);
            pool.RemoveAt(pickedIndex); // no duplicates within one offer set
        }
        return results;
    }

    private static int WeightedPickIndex<T>(List<(T item, float weight)> pool)
    {
        float total = 0f;
        foreach (var p in pool) total += p.weight;

        float roll = UnityEngine.Random.value * total;
        float cumulative = 0f;
        for (int i = 0; i < pool.Count; i++)
        {
            cumulative += pool[i].weight;
            if (roll <= cumulative) return i;
        }
        return pool.Count - 1;
    }
}
