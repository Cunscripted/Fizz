using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Weighted-random picker for AdditiveData.clawPool (EffectType.AddRandomClawToDeck).
/// Unlike RandomAdditivePicker (uniform, with an optional single-flavor filter and a
/// global-pool fallback), this always uses each entry's own relative weight AND its own
/// eligibility rule (WeightedAdditiveEntry.eligibility): a RequiresOwnedFlavor entry is
/// only a candidate once the player owns something tagged with that entry's manually-set
/// matchFlavor; an AlwaysInPool entry is always a candidate (weight permitting) and
/// ignores matchFlavor entirely. matchFlavor is intentionally independent of whatever
/// flavors the entry's own AdditiveData asset is tagged with - it's set per-entry in the
/// pool, not auto-derived from the additive.
/// </summary>
public static class ClawPicker
{
    /// <summary>
    /// Picks one AdditiveData from pool, weighted by each entry's `weight`, restricted to
    /// entries that pass their own `eligibility` rule against `owned`'s flavors. Returns
    /// null if pool is null/empty, or nothing in it is currently eligible/weighted - callers
    /// should treat null as a silent no-op, not an error.
    /// </summary>
    public static AdditiveData Pick(List<WeightedAdditiveEntry> pool, List<AdditiveInstance> owned)
    {
        if (pool == null || pool.Count == 0) return null;

        var ownedFlavors = new HashSet<FlavorType>();
        if (owned != null)
        {
            foreach (var instance in owned)
            {
                if (instance?.Flavors == null) continue;
                foreach (var flavor in instance.Flavors)
                    ownedFlavors.Add(flavor);
            }
        }

        float totalWeight = 0f;
        var eligible = new List<(AdditiveData additive, float weight)>();
        foreach (var entry in pool)
        {
            if (entry.additive == null || entry.weight <= 0f) continue;

            bool isEligible = entry.eligibility == ClawEligibility.AlwaysInPool || ownedFlavors.Contains(entry.matchFlavor);
            if (!isEligible) continue;

            eligible.Add((entry.additive, entry.weight));
            totalWeight += entry.weight;
        }

        if (eligible.Count == 0 || totalWeight <= 0f) return null;

        float roll = Random.value * totalWeight;
        float cumulative = 0f;
        foreach (var (additive, weight) in eligible)
        {
            cumulative += weight;
            if (roll <= cumulative) return additive;
        }

        return eligible[eligible.Count - 1].additive; // floating-point fallback - should be unreachable in practice
    }
}