#if UNITY_EDITOR
using UnityEditor;
using UnityEditorInternal;
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
        var iconProp = so.FindProperty("icon");
        EditorGUILayout.PropertyField(iconProp);
        DrawIconPreview(iconProp);

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
            case EffectType.BuffRandomBufferTargetCount:
                EditorGUILayout.PropertyField(so.FindProperty("amount"), new GUIContent("Extra Targets"));
                EditorGUILayout.HelpBox("Each time scored, finds a random OTHER owned additive that is itself a Buff Random Additive " +
                                        "(Points/Mult/Retrigger) card, and permanently makes IT buff this many additional additives " +
                                        "every time IT fires - e.g. a buffer that normally buffs 1 additive now buffs 2. Falls back " +
                                        "to amplifying itself if this card is ALSO a buffer and no other buffer is owned; does " +
                                        "nothing that fire if no buffer is owned at all.", MessageType.None);
                EditorGUILayout.HelpBox("Unlike the other Buff* effects, an amount BELOW 1 is not rounded up - it accumulates as " +
                                        "partial progress on whichever additive gets hit, carrying over across separate fires " +
                                        "(even if a different target gets picked each time isn't an issue - progress is tracked " +
                                        "per-target). 0.5 needs to land on the same target twice before that target actually " +
                                        "buffs 1 extra additive; 0.25 needs to land on it 4 times; 1.5 grants +1 target " +
                                        "immediately and leaves 0.5 progress banked toward the next.", MessageType.None);
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
            case EffectType.AddRandomClawToDeck:
                DrawClawPool(so.FindProperty("clawPool"));
                EditorGUILayout.HelpBox("Your own curated pool of \"claw\" additives, each with its own relative " +
                                        "weight - a 3 next to a 1 is picked 3x as often. Each entry also has an " +
                                        "Eligibility dropdown: Requires Owned Flavor means it's only a candidate " +
                                        "once you own something tagged with that entry's own Match Flavor field " +
                                        "(set manually per entry - NOT read off the additive's own flavor tags); " +
                                        "Always In Pool skips that check entirely and is always a candidate " +
                                        "(weight permitting). Does nothing that fire if the pool is empty, every " +
                                        "entry has no additive assigned or weight 0, or every Requires-Owned-" +
                                        "Flavor entry's flavor isn't owned yet and nothing is Always In Pool - a " +
                                        "warning logs to the Console explaining which, so it's distinguishable " +
                                        "from the effect just not having fired yet.", MessageType.None);
                break;
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

    /// <summary>
    /// Shows a preview of the additive's packet artwork (its icon sprite) right under the
    /// icon field on the Additives tab, so you can see what the card will look like without
    /// hunting the sprite down in the Project window.
    /// </summary>
    private static void DrawIconPreview(SerializedProperty iconProp)
    {
        var sprite = iconProp.objectReferenceValue as Sprite;

        EditorGUILayout.BeginHorizontal();
        GUILayout.Label("Packet Preview", GUILayout.Width(EditorGUIUtility.labelWidth));

        const float previewSize = 96f;
        Rect previewRect = GUILayoutUtility.GetRect(previewSize, previewSize, GUILayout.Width(previewSize));
        GUI.Box(previewRect, GUIContent.none);

        if (sprite != null)
        {
            Texture2D tex = AssetPreview.GetAssetPreview(sprite) ?? AssetPreview.GetMiniThumbnail(sprite);
            if (tex != null)
            {
                // Fit the preview inside the box while keeping the sprite's aspect ratio.
                float aspect = (float)tex.width / tex.height;
                Rect fitRect = previewRect;
                if (aspect >= 1f)
                {
                    fitRect.height = previewRect.width / aspect;
                    fitRect.y += (previewRect.height - fitRect.height) * 0.5f;
                }
                else
                {
                    fitRect.width = previewRect.height * aspect;
                    fitRect.x += (previewRect.width - fitRect.width) * 0.5f;
                }
                GUI.DrawTexture(fitRect, tex, ScaleMode.ScaleToFit);
            }
            else
            {
                // Preview still loading (common right after a selection change) - ask for a repaint
                // so it pops in as soon as it's ready instead of staying blank until the next click.
                EditorGUILayout.HelpBox("Loading preview...", MessageType.None);
            }
        }
        else
        {
            GUI.Label(previewRect, "No Icon", EditorStyles.centeredGreyMiniLabel);
        }

        EditorGUILayout.EndHorizontal();
    }

    /// <summary>
    /// Custom list drawer for AdditiveData.clawPool, used INSTEAD OF a plain
    /// EditorGUILayout.PropertyField(..., true) specifically so new entries default to
    /// weight 1 instead of Unity's usual default(float) = 0. A 0-weight entry is
    /// permanently ineligible in ClawPicker - the plain list drawer's "+" button (and
    /// typing a bigger number directly into its "Size" field) both leave new slots at
    /// weight 0, which is exactly the "I added claws and they just never spawn" trap
    /// this avoids.
    /// </summary>
    private static void DrawClawPool(SerializedProperty clawPoolProp)
    {
        var list = new ReorderableList(clawPoolProp.serializedObject, clawPoolProp, true, true, true, true)
        {
            drawHeaderCallback = rect => EditorGUI.LabelField(rect, "Claw Pool"),
            elementHeightCallback = _ => (EditorGUIUtility.singleLineHeight * 2f) + 6f,
            drawElementCallback = (rect, index, isActive, isFocused) =>
            {
                var element = clawPoolProp.GetArrayElementAtIndex(index);
                var additiveProp = element.FindPropertyRelative("additive");
                var weightProp = element.FindPropertyRelative("weight");
                var eligibilityProp = element.FindPropertyRelative("eligibility");
                var matchFlavorProp = element.FindPropertyRelative("matchFlavor");

                float lineHeight = EditorGUIUtility.singleLineHeight;
                float y = rect.y + 2f;
                const float weightWidth = 55f;
                const float gap = 6f;

                // Row 1: which additive, and its relative weight.
                var additiveRect = new Rect(rect.x, y, rect.width - weightWidth - gap, lineHeight);
                var weightRect = new Rect(rect.xMax - weightWidth, y, weightWidth, lineHeight);
                EditorGUI.PropertyField(additiveRect, additiveProp, GUIContent.none);
                float newWeight = EditorGUI.FloatField(weightRect, weightProp.floatValue);
                weightProp.floatValue = Mathf.Max(0f, newWeight); // mirrors the [Min(0f)] on the struct field itself

                y += lineHeight + 4f;

                // Row 2: eligibility dropdown, plus (only when relevant) the manually-set
                // flavor that entry counts as. Always In Pool needs no second field, so it
                // gets the full row width instead of leaving dead space next to it.
                bool alwaysInPool = eligibilityProp.enumValueIndex == (int)ClawEligibility.AlwaysInPool;
                if (alwaysInPool)
                {
                    var eligibilityRect = new Rect(rect.x, y, rect.width, lineHeight);
                    EditorGUI.PropertyField(eligibilityRect, eligibilityProp, GUIContent.none);
                }
                else
                {
                    float eligibilityWidth = rect.width * 0.55f;
                    var eligibilityRect = new Rect(rect.x, y, eligibilityWidth, lineHeight);
                    var matchFlavorRect = new Rect(rect.x + eligibilityWidth + gap, y, rect.width - eligibilityWidth - gap, lineHeight);
                    EditorGUI.PropertyField(eligibilityRect, eligibilityProp, GUIContent.none);
                    EditorGUI.PropertyField(matchFlavorRect, matchFlavorProp, GUIContent.none);
                }
            },
            onAddCallback = l =>
            {
                int index = l.serializedProperty.arraySize;
                l.serializedProperty.arraySize++;
                l.index = index;
                var newElement = l.serializedProperty.GetArrayElementAtIndex(index);
                newElement.FindPropertyRelative("additive").objectReferenceValue = null;
                newElement.FindPropertyRelative("weight").floatValue = 1f; // fixes the "defaults to 0, never spawns" trap
                newElement.FindPropertyRelative("eligibility").enumValueIndex = (int)ClawEligibility.RequiresOwnedFlavor;
                newElement.FindPropertyRelative("matchFlavor").enumValueIndex = 0;
            }
        };

        list.DoLayoutList();
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