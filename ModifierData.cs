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
    AddAttempt,            // permanently grants extra attempts per round for the rest of the run
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

    [Header("Effect")]
    public ModifierEffectType effectType;

    [Header("Combo Rule Target (BoostComboRule / RetriggerComboRule)")]
    public FlavorComboRule targetComboRule;

    [Header("Flavor Target (RetriggerForFlavor / BoostFlavorPoints / BoostFlavorMult / ScalePerFlavorOnBelt)")]
    public FlavorType targetFlavor;

    [Tooltip("Meaning depends on effectType: bonus amount, extra trigger count, retrigger count, " +
             "or per-belt-match amount. See the tooltip context in the editor window.")]
    public float amount = 1f;

    [Header("Scale Per Flavor On Belt")]
    public bool scaleIsMult = false;

    [Header("Add Additive To Deck")]
    public AdditiveData additiveToAdd;

    [Header("Add Attempt")]
    public int attemptBonus = 1;
}
