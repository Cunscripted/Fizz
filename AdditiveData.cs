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

public enum BeltPassivePayloadType
{
    Points,
    Mult,
    XMult
}

public enum DeleteFilterMode
{
    AnyFlavor,      // no restriction - any unmodified owned additive is eligible
    SpecificFlavor, // only additives matching targetDeleteFlavor
    NoFlavor,       // only additives whose OWN flavor list is empty
    FromPool,       // only additives whose template appears in deletePool
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
    BuffRandomAdditiveRetrigger, // permanently adds round(amount) retriggers to a random OTHER additive you own
    BuffRandomBufferTargetCount, // finds a random OTHER owned additive that is itself one of the three Buff* effects above, and permanently makes IT buff round(amount) additional additive(s) every time it fires (e.g. a Buff* additive that normally buffs 1 additive now buffs 2)
    PointsPerFlavorOnBelt,      // + amount * (count of `scalingFlavor` additives currently on the conveyor belt)
    MultPerFlavorOnBelt,        // same, but adds to mult
    XMultPerFlavorOnBelt,       // multiplies mult by (amount ^ count of matching belt additives) - 0 matches = x1 (no-op)
    AddAdditiveToDeck,          // permanently adds `additiveToAdd` to your owned collection when scored
    AddRandomAdditiveToDeck,    // permanently adds one random additive - from randomAdditivePool, filtered by flavor, or both (see RandomAdditivePicker)
    AddRandomClawToDeck,        // permanently adds one random "claw" - weighted-random from clawPool, restricted to entries eligible this fire (owns the entry's manually-set matchFlavor, or the entry is set to always be in the pool) (see ClawPicker)
    DeleteCreatorThenSelf,      // deletes an owned additive that creates targetCreatorFlavor cards, then deletes itself
    DeleteRandomUnmodifiedFlavorThenSelf, // deletes a random UNMODIFIED (no buffs) owned additive - filtered by targetDeleteFlavor if filterByFlavor is on, otherwise ANY owned additive regardless of flavor - then deletes itself
    DeleteSpecificAdditiveThenSelf, // deletes one owned additive matching additiveToDelete's template (any flavor), then deletes itself
    DeletePlayerChosenThenSelf, // opens the deck stats menu and lets the PLAYER click which owned additive to delete, then deletes itself too - see DeckStatsPanel.ShowForDeletion
    AddBeltSizePermanent,       // permanently increases how many additives are drawn onto the belt each round, for the rest of the run
    PassiveOnBelt,              // grants points/mult/x-mult just by sitting on the conveyor belt - no need to be mixed into the soda (see beltPassivePayload)
}

/// <summary>
/// Whether a clawPool entry needs the player to already own a matching flavor to be
/// eligible, or is always available to be picked regardless.
/// </summary>
public enum ClawEligibility
{
    RequiresOwnedFlavor, // only eligible once the player owns at least one additive tagged with matchFlavor
    AlwaysInPool,        // always eligible, no flavor check at all - matchFlavor is ignored for this entry
}

/// <summary>
/// One entry in a clawPool: an additive, how heavily it's weighted relative to the
/// other entries when AddRandomClawToDeck rolls for one, and the eligibility rule that
/// decides whether it's a candidate at all this fire.
///
/// Weight is relative, not a percentage - a 3 next to a 1 is picked 3x as often,
/// regardless of what else is in the pool. Weight 0 (or below) permanently excludes
/// that entry no matter its eligibility.
///
/// matchFlavor is set MANUALLY per entry rather than read off `additive.flavors` -
/// this is deliberate: it decouples "what flavor this claw counts as, for the purpose
/// of the ownership gate" from whatever flavors the additive asset itself happens to
/// be tagged with (which may have none, several, or ones that don't reflect what this
/// specific claw entry is meant to represent).
/// </summary>
[System.Serializable]
public struct WeightedAdditiveEntry
{
    public AdditiveData additive;
    [Min(0f)] public float weight;
    public ClawEligibility eligibility;
    [Tooltip("Only used when Eligibility is Requires Owned Flavor - the flavor THIS ENTRY counts as for the " +
             "ownership check, independent of additive.flavors. Ignored entirely when Eligibility is Always In Pool.")]
    public FlavorType matchFlavor;
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

    [Header("Flavor Scaling (PointsPerFlavorOnBelt / MultPerFlavorOnBelt / XMultPerFlavorOnBelt)")]
    [Tooltip("Counts belt additives matching ANY of these flavors (each qualifying belt additive still only " +
             "counts once per flavor it matches - a card with two listed flavors adds to the count twice).")]
    public List<FlavorType> scalingFlavors = new List<FlavorType>();

    [Header("Add To Deck (AddAdditiveToDeck)")]
    public AdditiveData additiveToAdd;
    [Tooltip("If true, this only adds the additive the first time it scores, not every round.")]
    public bool addToDeckOnlyOnce = true;

    [Header("Add Random Additive To Deck (AddRandomAdditiveToDeck)")]
    [Tooltip("Candidate pool to pick randomly from. Leave empty if you only want to filter by flavor below - " +
             "in that case it searches every additive obtainable in the game instead of a curated list.")]
    public List<AdditiveData> randomAdditivePool = new List<AdditiveData>();
    [Tooltip("If true, only additives of randomFlavorFilter are eligible - either filtering the pool above, " +
             "or (if the pool is empty) searching every additive in the game for that flavor.")]
    public bool filterByFlavor = false;
    public FlavorType randomFlavorFilter;

    [Header("Claw Pool (AddRandomClawToDeck)")]
    [Tooltip("Your own curated set of \"claw\" additives (see WeightedAdditiveEntry). Each entry has its own " +
             "relative weight, an Eligibility mode (Requires Owned Flavor vs Always In Pool), and - when " +
             "Requires Owned Flavor - a manually-set Match Flavor that entry counts as, independent of " +
             "whatever flavors the additive asset itself is tagged with. AddRandomClawToDeck rolls a " +
             "weighted-random pick from whichever entries are currently eligible. If NO entry qualifies " +
             "(empty pool, every eligible entry has weight 0/no additive assigned, or every Requires-Owned-" +
             "Flavor entry's flavor isn't owned yet and nothing is set to Always In Pool), this is a silent " +
             "no-op for that fire, same as the other \"nothing eligible\" effects elsewhere.")]
    public List<WeightedAdditiveEntry> clawPool = new List<WeightedAdditiveEntry>();

    [Header("Delete Creator Then Self (DeleteCreatorThenSelf)")]
    [Tooltip("Targets an owned additive whose AddAdditiveToDeck effect creates cards of this flavor, " +
             "deletes it from your collection, then deletes this additive too.")]
    public FlavorType targetCreatorFlavor;

    [Header("Delete Random Unmodified Then Self (DeleteRandomUnmodifiedFlavorThenSelf)")]
    [Tooltip("How to narrow down which unmodified owned additives are eligible to be deleted.")]
    public DeleteFilterMode deleteFilterMode = DeleteFilterMode.AnyFlavor;
    [Tooltip("Used when Delete Filter Mode is Specific Flavor.")]
    public FlavorType targetDeleteFlavor;
    [Tooltip("Used when Delete Filter Mode is From Pool - only owned additives whose template appears in " +
             "this list are eligible, regardless of their flavor.")]
    public List<AdditiveData> deletePool = new List<AdditiveData>();
    [Tooltip("Weighted, not exclusive - eligible additives whose OWN effect is AddAdditiveToDeck or " +
             "AddRandomAdditiveToDeck (i.e. they create other additives) are this many times more likely to be " +
             "picked than a normal candidate, without ruling out any other eligible additive entirely. 1 = no " +
             "preference (uniform random). Higher values weight harder toward creators; they can still miss.")]
    public float creatorPriorityWeight = 10f;

    [Header("Delete Specific Additive Then Self (DeleteSpecificAdditiveThenSelf)")]
    [Tooltip("Deletes one owned additive matching this exact template (any flavor, buffed or not), then " +
             "deletes this additive too. If you don't own one, only this additive deletes itself.")]
    public AdditiveData additiveToDelete;

    // Delete Player Chosen Then Self (DeletePlayerChosenThenSelf) needs no extra fields here - unlike the
    // other Delete*ThenSelf variants, it doesn't pick a target itself at all. Scoring it opens the deck
    // stats menu and lets the PLAYER click whichever owned additive they want removed (see
    // DeckStatsPanel.ShowForDeletion / RoundManager's pending-deletion queue), then deletes this additive too.

    [Header("Retrigger")]
    [Tooltip("Extra times this additive's own effect fires per score, before combo/syrup rules are checked. " +
             "Works with ANY effect type above, not just RetriggerSelf - e.g. a FlatPoints additive with " +
             "baseRetriggerCount 1 fires its points twice.")]
    public int baseRetriggerCount = 0;

    [Header("Retrigger Payload (RetriggerSelf)")]
    [Tooltip("Whether the `amount` above is applied as points or mult each time this additive fires.")]
    public RetriggerPayloadType retriggerPayload = RetriggerPayloadType.Points;

    [Header("Passive On Belt (PassiveOnBelt)")]
    [Tooltip("This bonus applies automatically while this additive is sitting on the conveyor belt, WITHOUT " +
             "needing to be dragged into the soda. If you put it in the cup anyway, it still applies the same " +
             "way (it's just no longer counted as 'on the belt' at that point, so it works either place).")]
    public BeltPassivePayloadType beltPassivePayload = BeltPassivePayloadType.Points;

    [Header("Downside (optional - stacks with ANY effect type above)")]
    [Tooltip("If true, this additive deletes itself from your collection after it fires - a one-shot drawback " +
             "usable alongside whatever its main effect is.")]
    public bool deleteSelfAfterUse = false;
    [Tooltip("If true (default), deleteSelfAfterUse only triggers the first time this fires. If false, it " +
             "tries every time it fires (harmless once it's already gone - only matters if something keeps " +
             "re-adding a copy of this exact instance, which doesn't normally happen).")]
    public bool deleteSelfOnlyOnce = true;
    [Tooltip("If true, Delete Self After Use only triggers when this additive is actually mixed into the soda " +
             "(scored from the cup) - NOT when it merely fires passively while sitting on the belt (e.g. via " +
             "PassiveOnBelt). Lets a card safely sit on the belt indefinitely without self-destructing, but " +
             "still self-destruct the moment you actually play it. If false (default), it deletes itself " +
             "either way.")]
    public bool deleteSelfOnlyWhenInCup = false;
    [Tooltip("Permanently changes this card's own EFFECTIVE amount by this much each time it fires - positive " +
             "grows it (e.g. a belt-passive card that gets stronger the longer it sits there), negative shrinks " +
             "it. Can go negative overall once it shrinks past 0 - a card that keeps depreciating will " +
             "eventually start SUBTRACTING from the score instead of just contributing nothing. Applies " +
             "whenever this card fires passively from the belt (e.g. PassiveOnBelt). Also applies when actually " +
             "played (mixed into the soda) UNLESS Use Separate Played Amount Change is on below.")]
    public float amountChangePerUse = 0f;
    [Tooltip("If true, Amount Change When Played (below) is used INSTEAD of Amount Change Per Use whenever this " +
             "additive fires from actually being played, rather than sharing the same rate as belt-passive " +
             "firing. E.g. deteriorate -2 per belt-passive fire, but leave Amount Change When Played at 0 so " +
             "actually playing the card never harms the score no matter how worn down it got on the belt.")]
    public bool useSeparatePlayedAmountChange = false;
    [Tooltip("Used instead of Amount Change Per Use when this additive fires from actually being played - only " +
             "takes effect if Use Separate Played Amount Change above is on.")]
    public float amountChangeWhenPlayed = 0f;

    [Header("Also Create Additive After Use (Buff effects only - permanent scaling)")]
    [Tooltip("If true, in addition to its normal buff, this additive ALSO creates/adds an additive to your " +
             "deck after firing - only meaningful on BuffRandomAdditivePoints/Mult/Retrigger.")]
    public bool alsoCreateAdditiveAfterUse = false;
    [Tooltip("Chance (0-1) the creation above actually happens each time this fires - 1 = always (default, " +
             "same as before this existed), lower values make it a coin-flip/rare bonus on top of the " +
             "guaranteed buff rather than a guaranteed creation every time.")]
    [Range(0f, 1f)]
    public float createAdditiveChance = 1f;
    [Tooltip("If true, the created additive is picked randomly (reusing the Add Random Additive To Deck " +
             "settings above - randomAdditivePool / filterByFlavor / randomFlavorFilter). If false, it creates " +
             "the specific additiveToAdd instead (reusing the Add To Deck setting above).")]
    public bool createRandomAdditive = true;

    public bool HasFlavor(FlavorType f) => flavors.Contains(f);
}