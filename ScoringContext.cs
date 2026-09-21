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

    /// <summary>
    /// The actual additive instances currently on the belt (not the cup) - used by
    /// PassiveOnBelt effects, which need to be scored directly rather than just counted
    /// like beltFlavorCounts. A card in the cup is never also in this list, since it's
    /// removed from the belt the moment it's accepted into the bottle.
    /// </summary>
    public List<AdditiveInstance> beltInstances;

    /// <summary>
    /// Every additive obtainable in the game (typically ShopManager.additivePool) - used as the
    /// fallback search space when an AddRandomAdditiveToDeck effect filters by flavor but has no
    /// curated pool of its own.
    /// </summary>
    public List<AdditiveData> allAdditivesPool;

    /// <summary>Invoked by AddAdditiveToDeck effects to permanently grow the owned collection.</summary>
    public Action<AdditiveData> addToDeck;

    /// <summary>Invoked by DeleteCreatorThenSelf to permanently remove a specific instance from the owned collection.</summary>
    public Action<AdditiveInstance> removeFromDeck;

    /// <summary>
    /// Invoked by DeletePlayerChosenThenSelf to queue a request for the PLAYER to choose
    /// which owned additive gets deleted, rather than that pick being made automatically
    /// like the other Delete*ThenSelf effects. RoundManager collects these during scoring
    /// and resolves them one at a time (via DeckStatsPanel.ShowForDeletion) once the
    /// scoring pass and its playback finish. The requesting instance itself is still
    /// removed synchronously here in ApplyEffect like any other Delete*ThenSelf effect -
    /// this only concerns the SEPARATE, player-chosen additional deletion.
    /// </summary>
    public Action<AdditiveInstance> requestPlayerDeletion;

    /// <summary>Invoked by AddBeltSizePermanent to permanently grow how many additives are drawn each round.</summary>
    public Action<int> addBeltSize;

    /// <summary>Maps an additive instance to its on-screen card, for spawning floating score text at the right spot.</summary>
    public Func<AdditiveInstance, RectTransform> resolveCardRect;

    /// <summary>Where floating text spawns for events not tied to one specific card (combos, syrups, global bonuses).</summary>
    public RectTransform fallbackAnchor;
}