#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// Draws only the fields relevant to the currently selected EffectType/ModifierEffectType,
/// so the inspector (or the editor window) doesn't show a wall of unrelated fields.
/// Shared between SodaEditorWindow and the CustomEditor classes to avoid duplicating logic.
/// </summary>
public static class SodaEditorFields
{
    public static void DrawAdditive(SerializedObject so)
    {
        EditorGUILayout.LabelField("Identity", EditorStyles.boldLabel);
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.PropertyField(so.FindProperty("additiveName"));
        if (GUILayout.Button("Use Asset Name", GUILayout.Width(110)))
            so.FindProperty("additiveName").stringValue = so.targetObject.name;
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.PropertyField(so.FindProperty("description"));
        EditorGUILayout.PropertyField(so.FindProperty("icon"));

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Flavors & Rarity", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(so.FindProperty("flavors"), true);
        EditorGUILayout.PropertyField(so.FindProperty("rarity"));

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Effect", EditorStyles.boldLabel);
        var effectProp = so.FindProperty("effectType");
        EditorGUILayout.PropertyField(effectProp);
        var effect = (EffectType)effectProp.enumValueIndex;

        switch (effect)
        {
            case EffectType.FlatPoints:
                EditorGUILayout.PropertyField(so.FindProperty("amount"), new GUIContent("Points"));
                break;
            case EffectType.FlatMult:
                EditorGUILayout.PropertyField(so.FindProperty("amount"), new GUIContent("Mult"));
                break;
            case EffectType.XMult:
                EditorGUILayout.PropertyField(so.FindProperty("amount"), new GUIContent("x Mult"));
                break;
            case EffectType.RetriggerSelf:
                var payloadProp = so.FindProperty("retriggerPayload");
                EditorGUILayout.PropertyField(payloadProp);
                var payload = (RetriggerPayloadType)payloadProp.enumValueIndex;
                EditorGUILayout.PropertyField(so.FindProperty("amount"),
                    new GUIContent(payload == RetriggerPayloadType.Mult ? "Mult Per Fire" : "Points Per Fire"));
                EditorGUILayout.HelpBox("Applies this amount every time it fires - the initial trigger AND every extra fire from Base Retrigger Count below.", MessageType.None);
                break;
            case EffectType.BuffRandomAdditivePoints:
                EditorGUILayout.PropertyField(so.FindProperty("amount"), new GUIContent("Points Buff"));
                EditorGUILayout.HelpBox("Each time scored, permanently adds this many points to a random OTHER additive you own.", MessageType.None);
                DrawCreateCompanion(so);
                break;
            case EffectType.BuffRandomAdditiveMult:
                EditorGUILayout.PropertyField(so.FindProperty("amount"), new GUIContent("Mult Buff"));
                EditorGUILayout.HelpBox("Each time scored, permanently adds this much mult to a random OTHER additive you own.", MessageType.None);
                DrawCreateCompanion(so);
                break;
            case EffectType.BuffRandomAdditiveRetrigger:
                EditorGUILayout.PropertyField(so.FindProperty("amount"), new GUIContent("Retrigger Buff"));
                EditorGUILayout.HelpBox("Each time scored, permanently adds this many retriggers (rounded, min 1) to a random OTHER additive you own.", MessageType.None);
                DrawCreateCompanion(so);
                break;
            case EffectType.PointsPerFlavorOnBelt:
                EditorGUILayout.PropertyField(so.FindProperty("amount"), new GUIContent("Points Per Match"));
                EditorGUILayout.PropertyField(so.FindProperty("scalingFlavors"), true);
                EditorGUILayout.HelpBox("Scales with how many additives matching ANY of these flavors sit on the conveyor belt (not the bottle) when scored.", MessageType.None);
                break;
            case EffectType.MultPerFlavorOnBelt:
                EditorGUILayout.PropertyField(so.FindProperty("amount"), new GUIContent("Mult Per Match"));
                EditorGUILayout.PropertyField(so.FindProperty("scalingFlavors"), true);
                EditorGUILayout.HelpBox("Scales with how many additives matching ANY of these flavors sit on the conveyor belt (not the bottle) when scored.", MessageType.None);
                break;
            case EffectType.XMultPerFlavorOnBelt:
                EditorGUILayout.PropertyField(so.FindProperty("amount"), new GUIContent("x Mult Per Match"));
                EditorGUILayout.PropertyField(so.FindProperty("scalingFlavors"), true);
                EditorGUILayout.HelpBox("Multiplies mult by (this value ^ count of matching belt additives) - e.g. 2 with 3 matches = x8 mult. 0 matches means no effect (x1).", MessageType.None);
                break;
            case EffectType.AddAdditiveToDeck:
                EditorGUILayout.PropertyField(so.FindProperty("additiveToAdd"));
                EditorGUILayout.PropertyField(so.FindProperty("addToDeckOnlyOnce"));
                break;
            case EffectType.AddRandomAdditiveToDeck:
            {
                EditorGUILayout.PropertyField(so.FindProperty("randomAdditivePool"), true);
                var filterProp = so.FindProperty("filterByFlavor");
                EditorGUILayout.PropertyField(filterProp, new GUIContent("Filter By Flavor"));
                if (filterProp.boolValue)
                    EditorGUILayout.PropertyField(so.FindProperty("randomFlavorFilter"));
                EditorGUILayout.HelpBox("Picks one additive at random from the pool above (filtered by flavor " +
                                        "if enabled). If the pool is empty and filtering is on, searches every " +
                                        "additive in the game for that flavor instead.", MessageType.None);
                break;
            }
            case EffectType.DeleteCreatorThenSelf:
                EditorGUILayout.PropertyField(so.FindProperty("targetCreatorFlavor"));
                EditorGUILayout.HelpBox("Finds an owned additive whose AddAdditiveToDeck effect creates this " +
                                        "flavor, deletes it, then deletes this additive too.", MessageType.None);
                break;
            case EffectType.DeleteRandomUnmodifiedFlavorThenSelf:
            {
                var modeProp = so.FindProperty("deleteFilterMode");
                EditorGUILayout.PropertyField(modeProp, new GUIContent("Filter Mode"));
                var mode = (DeleteFilterMode)modeProp.enumValueIndex;
                if (mode == DeleteFilterMode.SpecificFlavor)
                    EditorGUILayout.PropertyField(so.FindProperty("targetDeleteFlavor"));
                else if (mode == DeleteFilterMode.FromPool)
                    EditorGUILayout.PropertyField(so.FindProperty("deletePool"), true);

                string modeHelp = mode switch
                {
                    DeleteFilterMode.SpecificFlavor => "Picks a random unmodified owned additive of this flavor.",
                    DeleteFilterMode.NoFlavor => "Picks a random unmodified owned additive that has NO flavor of its own.",
                    DeleteFilterMode.FromPool => "Picks a random unmodified owned additive whose template appears in the pool above.",
                    _ => "Picks a random unmodified owned additive, any flavor, no restriction."
                };
                EditorGUILayout.HelpBox(modeHelp + " Deletes it, then deletes this additive too.", MessageType.None);
                EditorGUILayout.PropertyField(so.FindProperty("creatorPriorityWeight"), new GUIContent("Creator Priority Weight"));
                EditorGUILayout.HelpBox("Eligible additives that themselves create other additives (AddAdditiveToDeck / " +
                                        "AddRandomAdditiveToDeck) are this many times more likely to be picked - weighted, " +
                                        "not exclusive, so anything else eligible can still be chosen. 1 = no preference.", MessageType.None);
                break;
            }
            case EffectType.DeleteSpecificAdditiveThenSelf:
                EditorGUILayout.PropertyField(so.FindProperty("additiveToDelete"));
                EditorGUILayout.HelpBox("Deletes one owned additive matching this exact template (any flavor, " +
                                        "buffed or not), then deletes this additive too.", MessageType.None);
                break;
            case EffectType.AddBeltSizePermanent:
                EditorGUILayout.PropertyField(so.FindProperty("amount"), new GUIContent("Belt Size Increase"));
                EditorGUILayout.HelpBox("Permanently increases how many additives are drawn onto the belt each " +
                                        "round, for the rest of the run. Rounded, minimum 1. To make this a " +
                                        "one-shot consumable, turn on Delete Self After Use in the Downside " +
                                        "section below.", MessageType.None);
                break;
            case EffectType.PassiveOnBelt:
            {
                var beltPayloadProp = so.FindProperty("beltPassivePayload");
                EditorGUILayout.PropertyField(beltPayloadProp, new GUIContent("Payload"));
                var beltPayload = (BeltPassivePayloadType)beltPayloadProp.enumValueIndex;
                EditorGUILayout.PropertyField(so.FindProperty("amount"), new GUIContent(beltPayload switch
                {
                    BeltPassivePayloadType.Points => "Points",
                    BeltPassivePayloadType.Mult => "Mult",
                    _ => "x Mult"
                }));
                EditorGUILayout.HelpBox("Applies automatically while this additive sits on the conveyor belt - " +
                                        "no need to drag it into the soda. Still works the same way if you put " +
                                        "it in the cup anyway.", MessageType.None);
                break;
            }
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Downside / Value Drift (optional, stacks with any effect above)", EditorStyles.boldLabel);
        var deleteSelfProp = so.FindProperty("deleteSelfAfterUse");
        EditorGUILayout.PropertyField(deleteSelfProp, new GUIContent("Delete Self After Use"));
        if (deleteSelfProp.boolValue)
        {
            EditorGUILayout.PropertyField(so.FindProperty("deleteSelfOnlyOnce"));
            EditorGUILayout.PropertyField(so.FindProperty("deleteSelfOnlyWhenInCup"), new GUIContent("Only When Played (Not Belt)"));
            EditorGUILayout.HelpBox("If on, this only deletes itself when actually mixed into the soda - " +
                                    "sitting on the belt and firing passively (e.g. PassiveOnBelt) never " +
                                    "triggers it. If off, it deletes itself either way.", MessageType.None);
        }
        EditorGUILayout.PropertyField(so.FindProperty("amountChangePerUse"), new GUIContent("Amount Change Per Use"));
        EditorGUILayout.HelpBox("Positive grows the card over time - e.g. a belt-passive card that gets " +
                                "stronger the longer it sits on the belt, since it fires again every brew " +
                                "attempt. Negative shrinks it instead (a diminishing-returns drawback), and " +
                                "can push it past 0 into actively subtracting from the score. Applies when " +
                                "played too, unless overridden below.", MessageType.None);
        var separateRateProp = so.FindProperty("useSeparatePlayedAmountChange");
        EditorGUILayout.PropertyField(separateRateProp, new GUIContent("Use Separate Played Amount Change"));
        if (separateRateProp.boolValue)
        {
            EditorGUILayout.PropertyField(so.FindProperty("amountChangeWhenPlayed"), new GUIContent("Amount Change When Played"));
            EditorGUILayout.HelpBox("Used instead of Amount Change Per Use specifically when this additive " +
                                    "fires from actually being played - e.g. leave at 0 so a card that " +
                                    "deteriorates on the belt never harms the score once you actually play it.", MessageType.None);
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Retrigger", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(so.FindProperty("baseRetriggerCount"));
    }

    /// <summary>Shared "also create an additive after use" block, shown under each of the three Buff* effect types.</summary>
    private static void DrawCreateCompanion(SerializedObject so)
    {
        EditorGUILayout.Space();
        var createProp = so.FindProperty("alsoCreateAdditiveAfterUse");
        EditorGUILayout.PropertyField(createProp, new GUIContent("Also Create Additive After Use"));
        if (!createProp.boolValue) return;

        EditorGUILayout.PropertyField(so.FindProperty("createAdditiveChance"), new GUIContent("Chance"));

        var randomProp = so.FindProperty("createRandomAdditive");
        EditorGUILayout.PropertyField(randomProp, new GUIContent("Create Randomly"));
        if (randomProp.boolValue)
        {
            EditorGUILayout.PropertyField(so.FindProperty("randomAdditivePool"), true);
            var filterProp = so.FindProperty("filterByFlavor");
            EditorGUILayout.PropertyField(filterProp, new GUIContent("Filter By Flavor"));
            if (filterProp.boolValue)
                EditorGUILayout.PropertyField(so.FindProperty("randomFlavorFilter"));
        }
        else
        {
            EditorGUILayout.PropertyField(so.FindProperty("additiveToAdd"), new GUIContent("Additive To Create"));
        }
        EditorGUILayout.HelpBox("In addition to buffing a random owned additive, this has a chance to also " +
                                "add an additive to your deck the same time it fires.", MessageType.None);
    }

    public static void DrawComboRule(SerializedObject so)
    {
        EditorGUILayout.LabelField("Requirement", EditorStyles.boldLabel);
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.PropertyField(so.FindProperty("comboName"));
        if (GUILayout.Button("Use Asset Name", GUILayout.Width(110)))
            so.FindProperty("comboName").stringValue = so.targetObject.name;
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.PropertyField(so.FindProperty("requiredFlavors"), true);
        EditorGUILayout.PropertyField(so.FindProperty("requiredAdditives"), true);
        EditorGUILayout.HelpBox("Both requirements above apply together if both have entries (AND). At least " +
                                "one must have entries or this combo can never fire. Minimum Matching Additives " +
                                "below only counts flavor matches, not specific additive matches.", MessageType.None);
        EditorGUILayout.PropertyField(so.FindProperty("minimumMatchingAdditives"));
        EditorGUILayout.PropertyField(so.FindProperty("customRequirementDescription"), new GUIContent("Custom Requirement Text (optional)"));
        EditorGUILayout.HelpBox("If set, this replaces the auto-generated requirement text (e.g. \"Sour + Fizzy\") " +
                                "shown in UI, without changing what's actually required to trigger the combo.", MessageType.None);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Reward", EditorStyles.boldLabel);
        var bonusTypeProp = so.FindProperty("bonusType");
        EditorGUILayout.PropertyField(bonusTypeProp);
        var bonusType = (ComboBonusType)bonusTypeProp.enumValueIndex;

        if (bonusType == ComboBonusType.AddAdditiveToDeck)
        {
            EditorGUILayout.PropertyField(so.FindProperty("additiveAddCount"), new GUIContent("How Many To Add"));
            var randomProp = so.FindProperty("createRandomAdditive");
            EditorGUILayout.PropertyField(randomProp, new GUIContent("Create Randomly"));
            if (randomProp.boolValue)
            {
                EditorGUILayout.PropertyField(so.FindProperty("randomAdditivePool"), true);
                var filterProp = so.FindProperty("filterByFlavor");
                EditorGUILayout.PropertyField(filterProp, new GUIContent("Filter By Flavor"));
                if (filterProp.boolValue)
                    EditorGUILayout.PropertyField(so.FindProperty("randomFlavorFilter"));
            }
            else
            {
                EditorGUILayout.PropertyField(so.FindProperty("additiveToAdd"));
            }
            EditorGUILayout.PropertyField(so.FindProperty("addOnlyOnce"));
            EditorGUILayout.HelpBox("Adds an additive to your deck each time this combo fires (or only the " +
                                    "first time, if Add Only Once is on).", MessageType.None);
        }
        else
        {
            EditorGUILayout.PropertyField(so.FindProperty("bonusAmount"));
        }
    }

    public static void DrawModifier(SerializedObject so)
    {
        EditorGUILayout.LabelField("Identity", EditorStyles.boldLabel);
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.PropertyField(so.FindProperty("modifierName"));
        if (GUILayout.Button("Use Asset Name", GUILayout.Width(110)))
            so.FindProperty("modifierName").stringValue = so.targetObject.name;
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.PropertyField(so.FindProperty("description"));

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Effect", EditorStyles.boldLabel);
        var effectProp = so.FindProperty("effectType");
        EditorGUILayout.PropertyField(effectProp);
        var effect = (ModifierEffectType)effectProp.enumValueIndex;

        switch (effect)
        {
            case ModifierEffectType.BoostComboRule:
                EditorGUILayout.PropertyField(so.FindProperty("targetComboRule"));
                EditorGUILayout.PropertyField(so.FindProperty("amount"), new GUIContent("Bonus Amount Added"));
                break;
            case ModifierEffectType.RetriggerComboRule:
                EditorGUILayout.PropertyField(so.FindProperty("targetComboRule"));
                EditorGUILayout.PropertyField(so.FindProperty("amount"), new GUIContent("Extra Triggers"));
                break;
            case ModifierEffectType.GlobalMult:
                EditorGUILayout.PropertyField(so.FindProperty("amount"), new GUIContent("Global Mult Add"));
                break;
            case ModifierEffectType.RetriggerForFlavor:
                EditorGUILayout.PropertyField(so.FindProperty("targetFlavor"));
                EditorGUILayout.PropertyField(so.FindProperty("amount"), new GUIContent("Retrigger Add"));
                break;
            case ModifierEffectType.BoostFlavorPoints:
                EditorGUILayout.PropertyField(so.FindProperty("targetFlavor"));
                EditorGUILayout.PropertyField(so.FindProperty("amount"), new GUIContent("Points Added"));
                break;
            case ModifierEffectType.BoostFlavorMult:
                EditorGUILayout.PropertyField(so.FindProperty("targetFlavor"));
                EditorGUILayout.PropertyField(so.FindProperty("amount"), new GUIContent("Mult Added"));
                break;
            case ModifierEffectType.ScalePerFlavorOnBelt:
                EditorGUILayout.PropertyField(so.FindProperty("scalingFlavors"), true);
                EditorGUILayout.PropertyField(so.FindProperty("scaleIsMult"), new GUIContent("Scales Mult (off = Points)"));
                EditorGUILayout.PropertyField(so.FindProperty("amount"), new GUIContent("Amount Per Belt Match"));
                EditorGUILayout.HelpBox("Scales with how many belt additives match ANY of these flavors.", MessageType.None);
                break;
            case ModifierEffectType.AddAdditiveToDeck:
                EditorGUILayout.PropertyField(so.FindProperty("additiveToAdd"));
                break;
            case ModifierEffectType.AddRandomAdditiveToDeck:
            {
                EditorGUILayout.PropertyField(so.FindProperty("randomAdditivePool"), true);
                var filterProp = so.FindProperty("filterByFlavor");
                EditorGUILayout.PropertyField(filterProp, new GUIContent("Filter By Flavor"));
                if (filterProp.boolValue)
                    EditorGUILayout.PropertyField(so.FindProperty("targetFlavor"), new GUIContent("Flavor Filter"));
                EditorGUILayout.HelpBox("Picks one additive at random from the pool above (filtered by flavor " +
                                        "if enabled). If the pool is empty and filtering is on, searches every " +
                                        "additive in the game for that flavor instead.", MessageType.None);
                break;
            }
            case ModifierEffectType.RemoveAdditiveFromDeck:
                EditorGUILayout.PropertyField(so.FindProperty("additiveToRemove"));
                EditorGUILayout.HelpBox("Removes one owned additive matching this template, if you have one.", MessageType.None);
                break;
            case ModifierEffectType.RemoveRandomAdditiveFromDeck:
            {
                var filterProp = so.FindProperty("filterByFlavor");
                EditorGUILayout.PropertyField(filterProp, new GUIContent("Filter By Flavor"));
                if (filterProp.boolValue)
                    EditorGUILayout.PropertyField(so.FindProperty("targetFlavor"), new GUIContent("Flavor Filter"));
                EditorGUILayout.HelpBox("Removes one random owned additive (filtered by flavor if enabled).", MessageType.None);
                break;
            }
            case ModifierEffectType.AddAttempt:
                EditorGUILayout.PropertyField(so.FindProperty("attemptBonus"));
                break;
            case ModifierEffectType.AddBeltSize:
                EditorGUILayout.PropertyField(so.FindProperty("beltSizeBonus"));
                EditorGUILayout.HelpBox("Permanently increases how many additives are drawn onto the belt each round.", MessageType.None);
                break;
        }
    }
}
#endif