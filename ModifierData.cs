using System.Collections.Generic;
using UnityEngine;

public enum ModifierEffectType
{
    BoostComboRule,        // permanently increases a specific FlavorComboRule's payout
    RetriggerComboRule,    // makes a specific FlavorComboRule fire additional times per soda
    GlobalMult,            // flat mult added to every soda scored from now on
    RetriggerForFlavor,    // cup additives with the target flavor gain extra fires
    BoostFlavorPoints,     // cup additives with the target flavor gain flat bonus points
    BoostFlavorMult,       // cup additives with the target flavor gain flat bonus mult
    ScalePerFlavorOnBelt,  // + amount per additive of the target flavor sitting on the belt when scored
    AddAdditiveToDeck,     // immediately adds a specific additive to the owned collection
    AddRandomAdditiveToDeck, // immediately adds one random additive from randomAdditivePool
    RemoveAdditiveFromDeck,       // removes one owned additive matching additiveToRemove's template
    RemoveRandomAdditiveFromDeck, // removes one random owned additive, optionally filtered by targetFlavor (via filterByFlavor)
    AddAttempt,            // permanently grants extra attempts per round for the rest of the run
    AddBeltSize,           // permanently increases how many additives are drawn onto the belt each round
}

/// <summary>
/// A permanent "syrup" upgrade, offered every 5th round. Create via
/// Assets > Create > Soda > Modifier, or the Soda > Additive & Modifier Editor window.
/// Applying one goes through ModifierManager.ApplyModifier so nothing writes
/// directly onto shared ScriptableObject asset fields at runtime.
/// </summary>
[CreateAssetMenu(fileName = "New Modifier", menuName = "Soda/Modifier")]
public class ModifierData : ScriptableObject
{
    [Header("Identity")]
    public string modifierName;
    [TextArea] public string description;

    [Header("Rarity")]
    [Tooltip("Drives base weight in the modifier-choice screen's weighted random pool, same mechanism as " +
             "AdditiveData.rarity does for shop offers - skewed further by shop/modifier skip streaks (see " +
             "RoundManager.SkipStreak).")]
    public Rarity rarity = Rarity.Common;

    [Header("Effect")]
    public ModifierEffectType effectType;

    [Header("Combo Rule Target (BoostComboRule / RetriggerComboRule)")]
    public FlavorComboRule targetComboRule;

    [Header("Flavor Target (RetriggerForFlavor / BoostFlavorPoints / BoostFlavorMult)")]
    public FlavorType targetFlavor;

    [Tooltip("Meaning depends on effectType: bonus amount, extra trigger count, retrigger count, " +
             "or per-belt-match amount. See the tooltip context in the editor window.")]
    public float amount = 1f;

    [Header("Scale Per Flavor On Belt (ScalePerFlavorOnBelt)")]
    public bool scaleIsMult = false;
    [Tooltip("Counts belt additives matching ANY of these flavors (each qualifying belt additive still " +
             "counts once per flavor it matches - a card with two listed flavors adds to the count twice).")]
    public List<FlavorType> scalingFlavors = new List<FlavorType>();

    [Header("Add Additive To Deck")]
    public AdditiveData additiveToAdd;

    [Header("Add Random Additive To Deck")]
    [Tooltip("Candidate pool to pick randomly from. Leave empty if you only want to filter by flavor (below, " +
             "using the shared Flavor Target field) - in that case it searches every additive obtainable in " +
             "the game instead of a curated list.")]
    public List<AdditiveData> randomAdditivePool = new List<AdditiveData>();
    [Tooltip("If true, only additives matching Flavor Target above are eligible - either filtering the pool, " +
             "or (if the pool is empty) searching every additive in the game for that flavor. Also used by " +
             "Remove Random Additive From Deck below, to restrict which owned additives are eligible for removal.")]
    public bool filterByFlavor = false;

    [Header("Remove Additive From Deck")]
    [Tooltip("Removes one owned additive matching this template, if you have one.")]
    public AdditiveData additiveToRemove;

    [Header("Add Attempt")]
    public int attemptBonus = 1;

    [Header("Add Belt Size")]
    [Tooltip("Permanently increases how many additives are drawn onto the belt each round, for the rest of the run.")]
    public int beltSizeBonus = 1;
}