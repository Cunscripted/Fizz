using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Shared "pick a random additive" logic for both additive effects
/// (AddRandomAdditiveToDeck) and syrup effects (ModifierEffectType.AddRandomAdditiveToDeck).
/// Supports three modes, checked in this order:
///   1. A curated pool, optionally further filtered by flavor.
///   2. If the pool is empty (or unset) and a flavor filter is on, falls back to
///      searching EVERY additive in the game for that flavor - "globalFallbackPool"
///      is meant to be ShopManager.additivePool, the canonical "everything obtainable" list.
///   3. Returns null if nothing matches either way (caller should just no-op).
/// </summary>
public static class RandomAdditivePicker
{
    public static AdditiveData Pick(List<AdditiveData> curatedPool, bool filterByFlavor, FlavorType flavor,
                                     List<AdditiveData> globalFallbackPool)
    {
        List<AdditiveData> candidates = (curatedPool != null && curatedPool.Count > 0)
            ? new List<AdditiveData>(curatedPool)
            : null;

        if (candidates != null && filterByFlavor)
            candidates = candidates.Where(a => a != null && a.HasFlavor(flavor)).ToList();

        if ((candidates == null || candidates.Count == 0) && filterByFlavor && globalFallbackPool != null)
            candidates = globalFallbackPool.Where(a => a != null && a.HasFlavor(flavor)).ToList();

        if (candidates == null || candidates.Count == 0) return null;
        return candidates[Random.Range(0, candidates.Count)];
    }
}
