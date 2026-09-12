using System.Collections.Generic;
using UnityEngine;

public enum Rarity
{
    Common,
    Uncommon,
    Rare,
    Legendary
}

public enum RetriggerPayloadType
{
    Points,
    Mult
}

public enum EffectType
{
    // --- Simple, self-contained effects ---
    FlatPoints,     // + points, once per trigger
    FlatMult,       // + mult, once per trigger
    XMult,          // multiply current mult (applied after all flat effects, like Balatro's x-mult jokers)
    RetriggerSelf,  // fires 1 + baseRetriggerCount times, applying `amount` as points or mult EACH fire (see retriggerPayload)

    // --- Effects that reach outside this one card ---
    BuffRandomAdditivePoints,   // permanently adds `amount` points to a random OTHER additive you own
    BuffRandomAdditiveMult,     // permanently adds `amount` mult to a random OTHER additive you own
    PointsPerFlavorOnBelt,      // + amount * (count of `scalingFlavor` additives currently on the conveyor belt)
    MultPerFlavorOnBelt,        // same, but adds to mult
    AddAdditiveToDeck,          // permanently adds `additiveToAdd` to your owned collection when scored
}

/// <summary>
/// One "card" in the deck. Create instances via Assets > Create > Soda > Additive,
/// or use the Soda > Additive & Modifier Editor window for a guided form that only
/// shows the fields relevant to the chosen EffectType.
/// </summary>
[CreateAssetMenu(fileName = "New Additive", menuName = "Soda/Additive")]
public class AdditiveData : ScriptableObject
{
    [Header("Identity")]
    public string additiveName;
    [TextArea] public string description;
    public Sprite icon;

    [Header("Flavors")]
    public List<FlavorType> flavors = new List<FlavorType>();

    [Header("Rarity")]
    [Tooltip("Drives base weight in the round-end shop's weighted random pool.")]
    public Rarity rarity = Rarity.Common;

    [Header("Base Effect")]
    public EffectType effectType = EffectType.FlatPoints;
    [Tooltip("Meaning depends on effectType: flat points/mult amount, x-mult multiplier, " +
             "buff-random amount, or per-belt-match amount.")]
    public float amount = 10f;

    [Header("Flavor Scaling (PointsPerFlavorOnBelt / MultPerFlavorOnBelt)")]
    public FlavorType scalingFlavor;

    [Header("Add To Deck (AddAdditiveToDeck)")]
    public AdditiveData additiveToAdd;
    [Tooltip("If true, this only adds the additive the first time it scores, not every round.")]
    public bool addToDeckOnlyOnce = true;

    [Header("Retrigger")]
    [Tooltip("Extra times this additive's own effect fires per score, before combo/syrup rules are checked. " +
             "Works with ANY effect type above, not just RetriggerSelf - e.g. a FlatPoints additive with " +
             "baseRetriggerCount 1 fires its points twice.")]
    public int baseRetriggerCount = 0;

    [Header("Retrigger Payload (RetriggerSelf)")]
    [Tooltip("Whether the `amount` above is applied as points or mult each time this additive fires.")]
    public RetriggerPayloadType retriggerPayload = RetriggerPayloadType.Points;

    public bool HasFlavor(FlavorType f) => flavors.Contains(f);
}