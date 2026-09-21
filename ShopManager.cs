using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Offers a weighted-random selection of additives at the end of a round.
/// The 'efficiency' passed in (0 = used all attempts, 1 = succeeded on the
/// first attempt) skews the odds toward rarer additives - this is the
/// "doing it in fewer attempts increases pick rate of better additives" hook.
/// skipStreak (see RoundManager.SkipStreak) adds an additional skew on top -
/// the "skip offers to bank better odds on a future one" hook, shared with
/// ModifierManager's syrup offers since they draw from the same streak.
/// </summary>
public class ShopManager : MonoBehaviour
{
    [Header("Full pool of obtainable additives")]
    public List<AdditiveData> additivePool = new List<AdditiveData>();
    public int offerCount = 4;

    [Header("Base weights (before efficiency/skip skew)")]
    public float commonWeight = 100f;
    public float uncommonWeight = 40f;
    public float rareWeight = 12f;
    public float legendaryWeight = 3f;

    [Tooltip("How hard efficiency pulls odds toward rarer tiers. 0 = no effect.")]
    public float efficiencySkewStrength = 3f;
    [Tooltip("How hard EACH consecutive shop/modifier skip (RoundManager.SkipStreak) pulls odds toward " +
             "rarer tiers, on top of the efficiency skew above. 0 = no effect - skipping does nothing special.")]
    public float skipSkewPerStack = 0.75f;

    public event Action<List<AdditiveData>> OnOffersReady;

    public List<AdditiveData> GenerateOffers(float efficiency01, int skipStreak = 0)
    {
        efficiency01 = Mathf.Clamp01(efficiency01);
        float skew = 1f + efficiency01 * efficiencySkewStrength + Mathf.Max(0, skipStreak) * skipSkewPerStack;

        var weights = new RarityWeightedPicker.Weights
        {
            common = commonWeight,
            uncommon = uncommonWeight,
            rare = rareWeight,
            legendary = legendaryWeight
        };

        var results = RarityWeightedPicker.PickMany(additivePool, a => a.rarity, weights, skew, offerCount);
        OnOffersReady?.Invoke(results);
        return results;
    }
}