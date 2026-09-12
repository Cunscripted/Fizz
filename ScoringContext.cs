using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Everything an additive's effect might need to see beyond its own numbers:
/// the cup being scored, the player's full owned collection (for permanent
/// buffs and deck additions), and how many of each flavor sit on the belt
/// right now (for belt-scaling effects). Built fresh by RoundManager each
/// time a soda is scored.
/// </summary>
public class ScoringContext
{
    public List<AdditiveInstance> cup;
    public List<AdditiveInstance> ownedAdditives;
    public Dictionary<FlavorType, int> beltFlavorCounts;

    /// <summary>Invoked by AddAdditiveToDeck effects to permanently grow the owned collection.</summary>
    public Action<AdditiveData> addToDeck;

    /// <summary>Maps an additive instance to its on-screen card, for spawning floating score text at the right spot.</summary>
    public Func<AdditiveInstance, RectTransform> resolveCardRect;

    /// <summary>Where floating text spawns for events not tied to one specific card (combos, syrups, global bonuses).</summary>
    public RectTransform fallbackAnchor;
}