using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Flavor "popularity" - customers get tired of a flavor you keep serving, and get
/// excited about one you haven't served in a while.
///
/// Every flavor has a HEAT value. After each brew:
///   - each flavor in the soda gains heatPerAdditive for every additive of that flavor in it
///     (so 4 Sour additives in one soda = +4 Sour heat),
///   - every flavor NOT in the soda cools off by coolPerBrew.
/// Serving a flavor soda after soda therefore stacks heat up quickly; skipping it lets it
/// recover.
///
/// Heat drives each flavor's EFFECTIVENESS - a multiplier on the points / +mult / xMult
/// payouts of additives with that flavor (see AdditiveInstance.ApplyEffect):
///   - heat at or below staleStartsAt           -> 1x (normal)
///   - heat between staleStartsAt and overdoneAt -> slides from 1x down to overdoneEffectiveness
///   - heat at or above overdoneAt               -> overdoneEffectiveness ("Overdone")
///   - no heat AND unused for trendingAfterUnusedBrews brews in a row -> trendingEffectiveness ("Trending")
/// An additive with several flavors uses the AVERAGE of its flavors' effectiveness.
///
/// Put one of these in the gameplay scene and drag it into RoundManager.popularity.
/// </summary>
public class PopularityManager : MonoBehaviour
{
    public enum Status { Trending, Fresh, Stale, Overdone }

    public static PopularityManager Instance { get; private set; }

    /// <summary>Fires whenever any flavor's heat/status changes - cards, panels, etc. refresh off this.</summary>
    public static event Action OnPopularityChanged;

    [Header("Heat")]
    [Tooltip("Heat a flavor gains per additive of that flavor in a brewed soda.")]
    public float heatPerAdditive = 1f;
    [Tooltip("Heat a flavor loses for every brew it ISN'T in.")]
    public float coolPerBrew = 1.5f;
    [Tooltip("Heat never goes above this - caps how long an overdone flavor takes to recover.")]
    public float maxHeat = 10f;

    [Header("Overdone")]
    [Tooltip("Above this heat the flavor starts losing effectiveness (\"Stale\").")]
    public float staleStartsAt = 3f;
    [Tooltip("At this heat the flavor is fully \"Overdone\" and pays out at Overdone Effectiveness.")]
    public float overdoneAt = 7f;
    [Tooltip("Payout multiplier for a fully overdone flavor. 0.5 = half points/mult.")]
    [Range(0f, 1f)] public float overdoneEffectiveness = 0.5f;

    [Header("Trending")]
    [Tooltip("A flavor with no heat that hasn't been served for this many brews in a row becomes \"Trending\".")]
    public int trendingAfterUnusedBrews = 2;
    [Tooltip("Payout multiplier for a trending flavor. 1.25 = +25% points/mult.")]
    [Min(1f)] public float trendingEffectiveness = 1.25f;

    [Header("Exemptions")]
    [Tooltip("Flavors that never gain heat or trend - always 1x (e.g. Waste, if it shouldn't take part).")]
    public List<FlavorType> exemptFlavors = new List<FlavorType>();
    [Tooltip("Specific additives that ignore popularity entirely: they always pay out 1x, never glow, and brewing " +
             "them doesn't heat their flavors. (Individual additives can also opt out with the Popularity toggles on " +
             "the additive itself - either way works.)")]
    public List<AdditiveData> exemptAdditives = new List<AdditiveData>();

    [Header("Round End")]
    [Tooltip("Heat every flavor loses when a round is won - lets overused flavors recover a little between rounds.")]
    public float coolOnRoundEnd = 1.5f;
    [Tooltip("Optional extra cooling as a share of each flavor's CURRENT heat when a round is won (0.25 = lose a " +
             "quarter). Applied after the flat amount above. 0 = off.")]
    [Range(0f, 1f)] public float coolOnRoundEndPercent = 0f;

    private readonly Dictionary<FlavorType, float> _heat = new Dictionary<FlavorType, float>();
    private readonly Dictionary<FlavorType, int> _brewsSinceUsed = new Dictionary<FlavorType, int>();

    /// <summary>Every flavor in the game, in enum order - handy for building UI rows.</summary>
    public static IReadOnlyList<FlavorType> AllFlavors => _allFlavors;
    private static readonly FlavorType[] _allFlavors = (FlavorType[])Enum.GetValues(typeof(FlavorType));

    private void Awake()
    {
        if (Instance != null && Instance != this)
            Debug.LogWarning($"[PopularityManager] More than one PopularityManager in the scene - '{name}' is replacing '{Instance.name}'.", this);
        Instance = this;
        ResetAll();
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    /// <summary>Clears all heat, e.g. at the start of a new run.</summary>
    public void ResetAll()
    {
        _bonusRoundEndPercent = 0f;
        foreach (var f in _allFlavors)
        {
            _heat[f] = 0f;
            _brewsSinceUsed[f] = 0; // nothing trends on brew 1 - it has to be earned by skipping a flavor
        }
        OnPopularityChanged?.Invoke();
    }

    // ---------- Queries ----------

    /// <summary>True if this card ignores popularity (its Affected By Popularity toggle is off, or it is in Exempt Additives) - always pays out 1x.</summary>
    public static bool IsCardExempt(AdditiveInstance additive)
    {
        var t = additive?.template;
        if (t == null) return false;
        if (!t.affectedByPopularity) return true;
        var pm = Instance;
        return pm != null && pm.exemptAdditives != null && pm.exemptAdditives.Contains(t);
    }

    /// <summary>True if brewing this additive should add heat to its flavors.</summary>
    public bool AddsHeat(AdditiveInstance additive)
    {
        var t = additive?.template;
        if (t == null) return true;
        if (!t.addsPopularityHeat) return false;
        return exemptAdditives == null || !exemptAdditives.Contains(t);
    }

    public bool IsExempt(FlavorType flavor) => exemptFlavors != null && exemptFlavors.Contains(flavor);

    public float GetHeat(FlavorType flavor) => _heat.TryGetValue(flavor, out var h) ? h : 0f;

    /// <summary>0 = no heat, 1 = fully overdone. What the popularity panel's bars show.</summary>
    public float GetOverdoneProgress(FlavorType flavor) => overdoneAt > 0f ? Mathf.Clamp01(GetHeat(flavor) / overdoneAt) : 0f;

    public int GetBrewsSinceUsed(FlavorType flavor) => _brewsSinceUsed.TryGetValue(flavor, out var b) ? b : 0;

    public Status GetStatus(FlavorType flavor)
    {
        if (IsExempt(flavor)) return Status.Fresh;
        float heat = GetHeat(flavor);
        if (heat >= overdoneAt) return Status.Overdone;
        if (heat > staleStartsAt) return Status.Stale;
        if (heat <= 0.001f && GetBrewsSinceUsed(flavor) >= trendingAfterUnusedBrews) return Status.Trending;
        return Status.Fresh;
    }

    /// <summary>Payout multiplier for one flavor right now.</summary>
    public float GetFlavorEffectiveness(FlavorType flavor)
    {
        switch (GetStatus(flavor))
        {
            case Status.Trending: return trendingEffectiveness;
            case Status.Overdone: return overdoneEffectiveness;
            case Status.Stale:
            {
                float t = Mathf.InverseLerp(staleStartsAt, overdoneAt, GetHeat(flavor));
                return Mathf.Lerp(1f, overdoneEffectiveness, t);
            }
            default: return 1f;
        }
    }

    /// <summary>Payout multiplier for an additive - the average of its flavors'. Flavorless additives are always 1x.</summary>
    public float GetEffectiveness(AdditiveInstance additive)
    {
        if (IsCardExempt(additive)) return 1f;
        var flavors = additive?.Flavors;
        if (flavors == null || flavors.Count == 0) return 1f;

        float sum = 0f;
        foreach (var f in flavors) sum += GetFlavorEffectiveness(f);
        return sum / flavors.Count;
    }

    /// <summary>The "most notable" status among an additive's flavors, for card cues: Overdone > Stale > Trending > Fresh.</summary>
    public Status GetDominantStatus(AdditiveInstance additive)
    {
        if (IsCardExempt(additive)) return Status.Fresh; // no glow on exempt cards
        var flavors = additive?.Flavors;
        if (flavors == null || flavors.Count == 0) return Status.Fresh;

        // Judge by the additive's actual combined effectiveness, so a Trending+Overdone card
        // that nets out below 1x reads as a penalty, and one that nets above 1x reads as a bonus.
        float eff = GetEffectiveness(additive);
        if (eff > 1.001f) return Status.Trending;
        if (eff < 0.999f)
        {
            foreach (var f in flavors)
                if (GetStatus(f) == Status.Overdone) return Status.Overdone;
            return Status.Stale;
        }
        return Status.Fresh;
    }

    // ---------- Updating ----------

    /// <summary>
    /// Call once per brew with the soda that was just scored (RoundManager does this automatically).
    /// Heats up every flavor in it, cools down every flavor that wasn't.
    /// </summary>
    public void RecordBrew(List<AdditiveInstance> cup)
    {
        var counts = new Dictionary<FlavorType, int>();
        if (cup != null)
        {
            foreach (var additive in cup)
            {
                if (!AddsHeat(additive)) continue; // exempt card - doesn't heat its flavors
                if (additive?.Flavors == null) continue;
                foreach (var f in additive.Flavors)
                {
                    counts.TryGetValue(f, out int c);
                    counts[f] = c + 1;
                }
            }
        }

        foreach (var f in _allFlavors)
        {
            if (IsExempt(f)) { _heat[f] = 0f; _brewsSinceUsed[f] = 0; continue; }

            if (counts.TryGetValue(f, out int n) && n > 0)
            {
                _heat[f] = Mathf.Min(maxHeat, GetHeat(f) + n * heatPerAdditive);
                _brewsSinceUsed[f] = 0;
            }
            else
            {
                _heat[f] = Mathf.Max(0f, GetHeat(f) - coolPerBrew);
                _brewsSinceUsed[f] = GetBrewsSinceUsed(f) + 1;
            }
        }

        OnPopularityChanged?.Invoke();
    }

    /// <summary>
    /// Call when a round is won (RoundManager does this automatically): every flavor cools off a
    /// little - coolOnRoundEnd flat, then coolOnRoundEndPercent of what's left - so overused
    /// flavors recover somewhat between rounds. Doesn't touch Trending progress.
    /// </summary>
    public void OnRoundEnded()
    {
        float percent = CombinedRoundEndPercent;
        if (coolOnRoundEnd <= 0f && percent <= 0f) return;
        foreach (var f in _allFlavors)
        {
            float h = Mathf.Max(0f, GetHeat(f) - Mathf.Max(0f, coolOnRoundEnd));
            h *= 1f - percent;
            _heat[f] = h;
        }
        OnPopularityChanged?.Invoke();
    }

    // Extra round-end cooling granted by syrups (Reduce Heat Percent with Repeat Every Round on).
    private float _bonusRoundEndPercent;

    /// <summary>Round-end percent cooling: the Inspector value combined with any syrup bonuses (they multiply, never exceed 100%).</summary>
    public float CombinedRoundEndPercent =>
        1f - (1f - Mathf.Clamp01(coolOnRoundEndPercent)) * (1f - Mathf.Clamp01(_bonusRoundEndPercent));

    /// <summary>Adds more percent cooling at every round end for the rest of the run (stacks multiplicatively).</summary>
    public void AddRoundEndCoolingPercent(float fraction)
    {
        fraction = Mathf.Clamp01(fraction);
        _bonusRoundEndPercent = 1f - (1f - _bonusRoundEndPercent) * (1f - fraction);
    }

    /// <summary>
    /// Cuts every flavor's heat by a fraction right now: 0.25 = lose a quarter, 1 = full reset to 0.
    /// Used by Retrigger All additives, combos and syrups with a heat-reduction effect.
    /// </summary>
    public void ReduceAllHeat(float fraction)
    {
        fraction = Mathf.Clamp01(fraction);
        if (fraction <= 0f) return;
        foreach (var f in _allFlavors)
            _heat[f] = fraction >= 1f ? 0f : GetHeat(f) * (1f - fraction);
        OnPopularityChanged?.Invoke();
    }

    /// <summary>Short label for UI, e.g. "Trending x1.25" or "Overdone x0.5".</summary>
    public string DescribeFlavor(FlavorType flavor)
    {
        var status = GetStatus(flavor);
        float eff = GetFlavorEffectiveness(flavor);
        return status == Status.Fresh ? "Fresh" : $"{status} x{eff:0.##}";
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (overdoneAt < staleStartsAt) overdoneAt = staleStartsAt;
        if (maxHeat < overdoneAt) maxHeat = overdoneAt;
    }
#endif
}