using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Offers a weighted-random selection of additives at the end of a round.
/// The 'efficiency' passed in (0 = used all attempts, 1 = succeeded on the
/// first attempt) skews the odds toward rarer additives - this is the
/// "doing it in fewer attempts increases pick rate of better additives" hook.
/// </summary>
public class ShopManager : MonoBehaviour
{
    [Header("Full pool of obtainable additives")]
    public List<AdditiveData> additivePool = new List<AdditiveData>();
    public int offerCount = 4;

    [Header("Base weights (before efficiency skew)")]
    public float commonWeight = 100f;
    public float uncommonWeight = 40f;
    public float rareWeight = 12f;
    public float legendaryWeight = 3f;

    [Tooltip("How hard efficiency pulls odds toward rarer tiers. 0 = no effect.")]
    public float efficiencySkewStrength = 3f;

    public event Action<List<AdditiveData>> OnOffersReady;

    public List<AdditiveData> GenerateOffers(float efficiency01)
    {
        efficiency01 = Mathf.Clamp01(efficiency01);
        float skew = 1f + efficiency01 * efficiencySkewStrength;

        var pool = new List<(AdditiveData data, float weight)>();
        foreach (var a in additivePool)
        {
            float w = BaseWeight(a.rarity);
            if (a.rarity != Rarity.Common) w *= skew; // only rarer tiers benefit from the skew
            pool.Add((a, w));
        }

        var results = new List<AdditiveData>();
        for (int i = 0; i < offerCount && pool.Count > 0; i++)
        {
            int pickedIndex = WeightedPickIndex(pool);
            results.Add(pool[pickedIndex].data);
            pool.RemoveAt(pickedIndex); // no duplicates within one offer
        }

        OnOffersReady?.Invoke(results);
        return results;
    }

    private float BaseWeight(Rarity r)
    {
        switch (r)
        {
            case Rarity.Common: return commonWeight;
            case Rarity.Uncommon: return uncommonWeight;
            case Rarity.Rare: return rareWeight;
            case Rarity.Legendary: return legendaryWeight;
            default: return 1f;
        }
    }

    private int WeightedPickIndex(List<(AdditiveData data, float weight)> pool)
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
