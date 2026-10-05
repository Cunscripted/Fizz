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
    [Tooltip("How hard EACH shop skip this run (RoundManager.ShopSkips - permanent, never resets) pulls odds toward " +
             "rarer tiers, on top of the efficiency skew above. 0 = no effect.")]
    public float skipSkewPerStack = 0.75f;
    [Tooltip("How hard EACH skipped card deletion this run (the Skip button on the deck's choose-a-card-to-delete " +
             "screen, RoundManager.DeletionSkips - permanent, never resets) pulls odds toward rarer tiers. Meant " +
             "to be smaller than skipSkewPerStack - a slight bump. 0 = no effect.")]
    public float deletionSkipSkewPerStack = 0.25f;

    [Header("Skipping")]
    [Tooltip("Every this many shop skips (total, over the run) adds one more offer slot. 0 = never.")]
    public int skipsPerExtraOffer = 5;
    [Tooltip("Offer slots never go above this, however many skips.")]
    public int maxOfferCount = 8;

    public event Action<List<AdditiveData>> OnOffersReady;

    /// <summary>How many random offers the shop shows after this many total skips.</summary>
    public int OfferCountFor(int shopSkips)
    {
        int extra = skipsPerExtraOffer > 0 ? Mathf.Max(0, shopSkips) / skipsPerExtraOffer : 0;
        return Mathf.Min(Mathf.Max(offerCount, maxOfferCount), offerCount + extra);
    }

    /// <summary>
    /// shopSkips = total shops skipped this run (RoundManager.ShopSkips). Each one permanently skews odds
    /// further toward rarer tiers (skipSkewPerStack), and every skipsPerExtraOffer adds an offer slot.
    /// deletionSkips = card deletions skipped this run (RoundManager.DeletionSkips) - each adds a smaller,
    /// equally permanent rarity bump (deletionSkipSkewPerStack), but no extra offer slots.
    /// </summary>
    public List<AdditiveData> GenerateOffers(float efficiency01, int shopSkips = 0, int deletionSkips = 0)
    {
        efficiency01 = Mathf.Clamp01(efficiency01);
        float skew = 1f + efficiency01 * efficiencySkewStrength
                        + Mathf.Max(0, shopSkips) * skipSkewPerStack
                        + Mathf.Max(0, deletionSkips) * deletionSkipSkewPerStack;

        var weights = new RarityWeightedPicker.Weights
        {
            common = commonWeight,
            uncommon = uncommonWeight,
            rare = rareWeight,
            legendary = legendaryWeight
        };

        var results = RarityWeightedPicker.PickMany(additivePool, a => a.rarity, weights, skew, OfferCountFor(shopSkips));
        OnOffersReady?.Invoke(results);
        return results;
    }
}