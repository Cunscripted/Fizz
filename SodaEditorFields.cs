#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
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
                DrawAdditivePool(so.FindProperty("randomAdditivePool"));
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
                    DrawAdditivePool(so.FindProperty("deletePool"));

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
            case EffectType.DeletePlayerChosenThenSelf:
                EditorGUILayout.HelpBox("Opens the deck stats panel and lets the PLAYER click which owned additive " +
                                        "to delete (after scoring finishes), then deletes this additive too. Needs " +
                                        "RoundManager.deckStatsPanel assigned, and that panel must NOT be a child of " +
                                        "something hidden during play (like the pause menu).", MessageType.None);
                break;
            case EffectType.RetriggerAllOtherAdditives:
                EditorGUILayout.PropertyField(so.FindProperty("amount"), new GUIContent("Retriggers Per Additive"));
                EditorGUILayout.PropertyField(so.FindProperty("retriggerAllHeatReduction"), new GUIContent("Heat Reduction (0-1)"));
                EditorGUILayout.HelpBox("When this fires, every OTHER additive in the soda fires its effect again this " +
                                        "many times (rounded, minimum 1). Other Retrigger All cards are skipped, so two " +
                                        "of them can't loop forever. Base Retrigger Count below makes THIS card fire " +
                                        "more often, which retriggers everything else again each time.", MessageType.None);
                break;
            case EffectType.AddCupCapacityPermanent:
                EditorGUILayout.PropertyField(so.FindProperty("amount"), new GUIContent("Cup Capacity Increase"));
                EditorGUILayout.HelpBox("Permanently raises how many additives the soda can hold, for the rest of the " +
                                        "run. Rounded, minimum 1. Takes effect from the next soda. Turn on Delete Self " +
                                        "After Use below to make it a one-shot.", MessageType.None);
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
        EditorGUILayout.LabelField("Popularity", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(so.FindProperty("affectedByPopularity"), new GUIContent("Affected By Popularity"));
        EditorGUILayout.PropertyField(so.FindProperty("addsPopularityHeat"), new GUIContent("Adds Popularity Heat"));
        EditorGUILayout.HelpBox("Affected: Trending/Stale/Overdone change this card's payouts (off = always 1x, no glow). " +
                                "Adds Heat: brewing it counts toward its flavors getting Stale/Overdone. Turn both off " +
                                "to fully exempt this card.", MessageType.None);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Extra Payout (optional, stacks with any effect above)", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(so.FindProperty("extraPoints"), new GUIContent("+ Points"));
        EditorGUILayout.PropertyField(so.FindProperty("extraMult"), new GUIContent("+ Mult"));
        EditorGUILayout.PropertyField(so.FindProperty("extraXMult"), new GUIContent("x Mult (1 = none)"));
        EditorGUILayout.HelpBox("Given every time this card fires (retriggers included), on top of its main effect - " +
                                "use these to make a card that gives a mix, e.g. Effect = Flat Points 30 plus +4 Mult " +
                                "and x1.5 Mult here. Order: main effect -> + Points -> + Mult -> buffs -> x Mult.", MessageType.None);

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

    private const string BanListPath = "Assets/Soda/AdditiveBanList.asset";

    /// <summary>The project's AdditiveBanList asset, or null if there isn't one yet.</summary>
    public static AdditiveBanList FindBanList()
    {
        var guid = AssetDatabase.FindAssets("t:AdditiveBanList").FirstOrDefault();
        return guid != null ? AssetDatabase.LoadAssetAtPath<AdditiveBanList>(AssetDatabase.GUIDToAssetPath(guid)) : null;
    }

    /// <summary>Finds the ban list, creating it at Assets/Soda/AdditiveBanList.asset if missing.</summary>
    public static AdditiveBanList FindOrCreateBanList()
    {
        var list = FindBanList();
        if (list != null) return list;

        if (!AssetDatabase.IsValidFolder("Assets/Soda")) AssetDatabase.CreateFolder("Assets", "Soda");
        list = ScriptableObject.CreateInstance<AdditiveBanList>();
        AssetDatabase.CreateAsset(list, BanListPath);
        AssetDatabase.SaveAssets();
        return list;
    }

    [MenuItem("Soda/Additive Ban List")]
    public static void SelectBanList()
    {
        var list = FindOrCreateBanList();
        Selection.activeObject = list;
        EditorGUIUtility.PingObject(list);
    }

    /// <summary>
    /// Draws an additive pool list with quick-fill buttons underneath: add every AdditiveData in
    /// the project (skipping ones already listed, anything on the project's Additive Ban List, and
    /// the asset being edited itself), clear the list, or open the ban list. Warns if the pool
    /// contains banned additives, with a button to remove them.
    /// </summary>
    public static void DrawAdditivePool(SerializedProperty poolProp)
    {
        if (poolProp == null) return;
        EditorGUILayout.PropertyField(poolProp, true);

        var banList = FindBanList();

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Add All Additives In Project"))
        {
            var self = poolProp.serializedObject.targetObject;
            var existing = new HashSet<Object>();
            for (int i = 0; i < poolProp.arraySize; i++)
            {
                var o = poolProp.GetArrayElementAtIndex(i).objectReferenceValue;
                if (o != null) existing.Add(o);
            }

            int added = 0, skippedBanned = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:AdditiveData"))
            {
                var asset = AssetDatabase.LoadAssetAtPath<AdditiveData>(AssetDatabase.GUIDToAssetPath(guid));
                if (asset == null || asset == self || existing.Contains(asset)) continue;
                if (banList != null && banList.IsBanned(asset)) { skippedBanned++; continue; }
                int index = poolProp.arraySize;
                poolProp.arraySize++;
                poolProp.GetArrayElementAtIndex(index).objectReferenceValue = asset;
                existing.Add(asset);
                added++;
            }
            Debug.Log($"[Soda Editor] Added {added} additive(s) to {poolProp.displayName} ({poolProp.arraySize} total)" +
                      (skippedBanned > 0 ? $", skipped {skippedBanned} on the Additive Ban List." : "."));
        }
        if (GUILayout.Button("Clear", GUILayout.Width(60)) &&
            EditorUtility.DisplayDialog("Clear pool", $"Remove all {poolProp.arraySize} entries from {poolProp.displayName}?", "Clear", "Cancel"))
        {
            poolProp.ClearArray();
        }
        if (GUILayout.Button(new GUIContent("Ban List", "Open the project's Additive Ban List (creates it if needed)."), GUILayout.Width(70)))
            SelectBanList();
        EditorGUILayout.EndHorizontal();

        // Point out banned additives that are already in this pool (e.g. added before they were banned).
        if (banList != null)
        {
            int bannedInPool = 0;
            for (int i = 0; i < poolProp.arraySize; i++)
                if (banList.IsBanned(poolProp.GetArrayElementAtIndex(i).objectReferenceValue as AdditiveData)) bannedInPool++;

            if (bannedInPool > 0)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.HelpBox($"{bannedInPool} additive(s) in this pool are on the Ban List.", MessageType.Warning);
                if (GUILayout.Button("Remove Banned", GUILayout.Width(110), GUILayout.Height(38)))
                {
                    for (int i = poolProp.arraySize - 1; i >= 0; i--)
                    {
                        var el = poolProp.GetArrayElementAtIndex(i);
                        if (!banList.IsBanned(el.objectReferenceValue as AdditiveData)) continue;
                        // Null it first: on some Unity versions deleting a non-null object slot only
                        // clears it instead of removing it.
                        el.objectReferenceValue = null;
                        poolProp.DeleteArrayElementAtIndex(i);
                    }
                }
                EditorGUILayout.EndHorizontal();
            }
        }
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
    // ReorderableLists are cached rather than rebuilt every OnGUI call. A fresh list each
    // repaint loses its selected index, so the "-" button never knew which entry to remove
    // and drag-to-reorder couldn't complete - the claw pool looked editable but wasn't really.
    // Keyed by the SerializedObject itself (not an instance ID - GetInstanceID() is obsolete in
    // newer Unity versions). The inspector and the Soda Editor window each keep one
    // SerializedObject alive while an asset is selected, so this finds the same list every repaint.
    private static readonly Dictionary<SerializedObject, ReorderableList> _clawPoolLists = new Dictionary<SerializedObject, ReorderableList>();

    private static void DrawClawPool(SerializedProperty clawPoolProp)
    {
        var so = clawPoolProp.serializedObject;
        if (!_clawPoolLists.TryGetValue(so, out var list))
        {
            // Old entries belong to SerializedObjects from previous selections - drop them so
            // the cache never grows beyond the one or two assets currently being edited.
            if (_clawPoolLists.Count > 8) _clawPoolLists.Clear();
            list = CreateClawPoolList(clawPoolProp);
            _clawPoolLists[so] = list;
        }
        list.DoLayoutList();
    }

    private static ReorderableList CreateClawPoolList(SerializedProperty clawPoolProp)
    {
        return new ReorderableList(clawPoolProp.serializedObject, clawPoolProp, true, true, true, true)
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
            DrawAdditivePool(so.FindProperty("randomAdditivePool"));
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
        DrawComboDiagnostics(so.targetObject as FlavorComboRule);

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
                                "counts flavor matches - or, on a combo with no Required Flavors, how many of the " +
                                "Required Additives are in the cup. List an additive twice to require two of it.", MessageType.None);
        EditorGUILayout.PropertyField(so.FindProperty("minimumMatchingAdditives"));
        EditorGUILayout.PropertyField(so.FindProperty("customRequirementDescription"), new GUIContent("Custom Requirement Text (optional)"));
        EditorGUILayout.HelpBox("If set, this replaces the auto-generated requirement text (e.g. \"Sour + Fizzy\") " +
                                "shown in UI, without changing what's actually required to trigger the combo.", MessageType.None);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Rewards (all of these fire, top to bottom)", EditorStyles.boldLabel);
        var effectsProp = so.FindProperty("effects");
        EditorGUILayout.PropertyField(effectsProp, new GUIContent("Effects"), true);
        EditorGUILayout.HelpBox("Stack as many as you like - e.g. +20 Points, +3 Mult and Retrigger Matching Additives " +
                                "all on one combo.\n" +
                                "  Bonus Points / Mult: Amount added.   Bonus XMult: Amount = the multiplier.\n" +
                                "  Retrigger Matching Additives: Amount = how many times they fire again.\n" +
                                "  Reduce Heat Percent: Amount = share of every flavor's heat removed (0.25 = 25%, 1 = reset).\n" +
                                "  Add Additive To Deck: uses the settings below; Amount above 1 adds extra copies.\n" +
                                "Amount Per Level is added for every level above 1.", MessageType.None);

        bool anyAddToDeck = false;
        for (int i = 0; i < effectsProp.arraySize; i++)
        {
            var typeProp = effectsProp.GetArrayElementAtIndex(i).FindPropertyRelative("type");
            if (typeProp != null && typeProp.enumValueIndex == (int)ComboBonusType.AddAdditiveToDeck) anyAddToDeck = true;
        }

        if (anyAddToDeck)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Add Additive To Deck", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(so.FindProperty("additiveAddCount"), new GUIContent("How Many To Add"));
            var randomProp = so.FindProperty("createRandomAdditive");
            EditorGUILayout.PropertyField(randomProp, new GUIContent("Create Randomly"));
            if (randomProp.boolValue)
            {
                DrawAdditivePool(so.FindProperty("randomAdditivePool"));
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
            EditorGUILayout.PropertyField(so.FindProperty("consumeMatchedAdditives"));
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Leveling", EditorStyles.boldLabel);
        var canLevelProp = so.FindProperty("canLevelUp");
        EditorGUILayout.PropertyField(canLevelProp);
        if (canLevelProp.boolValue)
        {
            EditorGUILayout.PropertyField(so.FindProperty("usesForFirstLevelUp"));
            EditorGUILayout.PropertyField(so.FindProperty("extraUsesPerLevel"));
            EditorGUILayout.PropertyField(so.FindProperty("usesGrowthMultiplier"));
            EditorGUILayout.PropertyField(so.FindProperty("maxLevel"), new GUIContent("Max Level (0 = none)"));

            if (so.targetObject is FlavorComboRule)
            {
                // Preview uses the values as currently typed in (ApplyModifiedProperties hasn't run yet
                // this frame, so read from the properties rather than the asset).
                int first = so.FindProperty("usesForFirstLevelUp").intValue;
                int extra = so.FindProperty("extraUsesPerLevel").intValue;
                float growth = so.FindProperty("usesGrowthMultiplier").floatValue;
                int max = so.FindProperty("maxLevel").intValue;
                var steps = new List<string>();
                int shown = max > 0 ? Mathf.Min(max - 1, 8) : 8;
                for (int lvl = 1; lvl <= shown; lvl++)
                {
                    int n = lvl - 1;
                    steps.Add(Mathf.Max(1, Mathf.RoundToInt(first * Mathf.Pow(growth, n) + extra * n)).ToString());
                }
                EditorGUILayout.HelpBox("Uses needed for each level up: " + (steps.Count > 0 ? string.Join(", ", steps) : "-") +
                                        (max <= 0 ? ", ..." : "") +
                                        "\nOne use = one soda this combo triggers in.", MessageType.None);
            }
        }
    }

    /// <summary>
    /// "Why won't my combo fire?" - checks the most common setup mistakes and lists them at the
    /// top of the combo's inspector. Scene checks look at whatever scenes are currently open.
    /// </summary>
    private static void DrawComboDiagnostics(FlavorComboRule rule)
    {
        if (rule == null) return;
        var problems = new List<string>();
        var notes = new List<string>();

        // 1. Registered on a SodaScoringManager? (Unregistered combos can never fire.)
        var managers = FindInOpenScenes<SodaScoringManager>();
        if (managers.Count > 0)
        {
            bool registered = false;
            foreach (var sm in managers)
            {
                var list = new SerializedObject(sm).FindProperty("comboRules");
                for (int i = 0; list != null && i < list.arraySize; i++)
                    if (list.GetArrayElementAtIndex(i).objectReferenceValue == rule) registered = true;
            }
            if (!registered)
                problems.Add("Not in SodaScoringManager's Combo Rules list - it can NEVER fire. Drag this asset into that list.");
        }
        else
        {
            notes.Add("Open the gameplay scene to also check this combo is in SodaScoringManager's Combo Rules list.");
        }

        // 2. Requirement
        bool hasFlavors = rule.requiredFlavors != null && rule.requiredFlavors.Count > 0;
        bool hasAdditives = rule.requiredAdditives != null && rule.requiredAdditives.Count > 0;
        if (!hasFlavors && !hasAdditives)
            problems.Add("No Required Flavors or Required Additives - it can never fire.");
        if (hasAdditives && rule.requiredAdditives.Any(a => a == null))
            problems.Add("Required Additives has an empty slot - remove it or assign an additive.");
        if (hasFlavors && hasAdditives)
            notes.Add("Has BOTH Required Flavors and Required Additives - the cup needs all of both.");
        if (!hasFlavors && hasAdditives && rule.minimumMatchingAdditives > rule.requiredAdditives.Count)
            problems.Add($"Minimum Matching Additives ({rule.minimumMatchingAdditives}) is more than the number of " +
                         $"Required Additives ({rule.requiredAdditives.Count}) - it can never be met.");

        // 3. Duplicate assets with the same name as a required additive (the cup's "Potato" may not be THIS Potato).
        if (hasAdditives)
        {
            var allAdditives = AssetDatabase.FindAssets("t:AdditiveData")
                .Select(g => AssetDatabase.LoadAssetAtPath<AdditiveData>(AssetDatabase.GUIDToAssetPath(g)))
                .Where(a => a != null).ToList();
            foreach (var req in rule.requiredAdditives.Where(a => a != null).Distinct())
            {
                var twins = allAdditives.Where(a => a != req && a.additiveName == req.additiveName).ToList();
                if (twins.Count > 0)
                    problems.Add($"'{req.additiveName}' exists as more than one asset ({req.name}, " +
                                 $"{string.Join(", ", twins.Select(t => t.name))}). The combo only accepts '{req.name}' - " +
                                 "make sure THAT one is what the shop/deck gives you, or delete the duplicate.");
            }

            // 4. Can the player even get the required additives?
            var shop = FindInOpenScenes<ShopManager>().FirstOrDefault();
            var round = FindInOpenScenes<RoundManager>().FirstOrDefault();
            if (shop != null || round != null)
            {
                foreach (var req in rule.requiredAdditives.Where(a => a != null).Distinct())
                {
                    bool inShop = shop != null && shop.additivePool.Contains(req);
                    bool inStart = round != null && System.Linq.Enumerable.Contains(round.AllStartingAdditives(), req);
                    if (!inShop && !inStart)
                        notes.Add($"'{req.additiveName}' isn't in the shop pool or starting deck - make sure something else creates it.");
                }
            }
        }

        // 5. Rewards
        if (rule.effects == null || rule.effects.Count == 0)
            problems.Add("No Effects - the combo would fire but do nothing. Add at least one effect.");
        bool addsToDeck = rule.effects != null && rule.effects.Any(e => e != null && e.type == ComboBonusType.AddAdditiveToDeck);
        if (rule.consumeMatchedAdditives && !addsToDeck)
            problems.Add("Consume Matched Additives is on, but there's no Add Additive To Deck effect - consuming only " +
                         "happens alongside creating something. Add an Add Additive To Deck effect.");
        if (addsToDeck)
        {
            if (!rule.createRandomAdditive && rule.additiveToAdd == null)
                problems.Add("Add Additive To Deck: Additive To Add is empty, so nothing is created (and nothing is consumed).");
            if (rule.createRandomAdditive && (rule.randomAdditivePool == null || rule.randomAdditivePool.Count == 0) && !rule.filterByFlavor)
                problems.Add("Add Additive To Deck: Create Randomly is on but the pool is empty and Filter By Flavor is off - nothing to pick from.");
            if (rule.addOnlyOnce)
                notes.Add("Add Only Once is on - it creates (and consumes) only the first time it fires each run.");
        }

        if (problems.Count > 0)
            EditorGUILayout.HelpBox("Combo check - problems:\n• " + string.Join("\n• ", problems), MessageType.Warning);
        else
            EditorGUILayout.HelpBox("Combo check: no setup problems found.", MessageType.Info);
        if (notes.Count > 0)
            EditorGUILayout.HelpBox("• " + string.Join("\n• ", notes), MessageType.None);
        EditorGUILayout.Space();
    }

    private static List<T> FindInOpenScenes<T>() where T : Component
    {
        return Resources.FindObjectsOfTypeAll<T>().Where(c => c != null && c.gameObject.scene.IsValid()).ToList();
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
                DrawAdditivePool(so.FindProperty("randomAdditivePool"));
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
            case ModifierEffectType.AddCupCapacity:
                EditorGUILayout.PropertyField(so.FindProperty("cupCapacityBonus"));
                EditorGUILayout.HelpBox("Permanently increases how many additives can be placed in the soda, for the rest of the run.", MessageType.None);
                break;
            case ModifierEffectType.ReduceHeatPercent:
                EditorGUILayout.PropertyField(so.FindProperty("amount"), new GUIContent("Heat Removed (0-1)"));
                EditorGUILayout.PropertyField(so.FindProperty("repeatEveryRound"), new GUIContent("Repeat Every Round End"));
                EditorGUILayout.HelpBox("Cuts every flavor's popularity heat by this share when picked (0.25 = 25%, 1 = reset all). " +
                                        "With Repeat Every Round End on, it also happens at the end of every round for the rest " +
                                        "of the run (several stack multiplicatively).", MessageType.None);
                break;
            case ModifierEffectType.BoostAmountGrowth:
            {
                var modeProp = so.FindProperty("amountGrowthMode");
                EditorGUILayout.PropertyField(modeProp, new GUIContent("Mode"));
                bool flip = modeProp.enumValueIndex == (int)AmountGrowthMode.FlipNegativeToPositive;
                if (flip)
                    EditorGUILayout.PropertyField(so.FindProperty("flipRate"), new GUIContent("Flip Rate"));
                else
                    EditorGUILayout.PropertyField(so.FindProperty("amount"), new GUIContent("Added To Amount Change"));
                var filterProp = so.FindProperty("filterByFlavor");
                EditorGUILayout.PropertyField(filterProp, new GUIContent("Only One Flavor"));
                if (filterProp.boolValue)
                    EditorGUILayout.PropertyField(so.FindProperty("targetFlavor"), new GUIContent("Flavor"));
                EditorGUILayout.HelpBox(flip
                    ? "For the rest of the run, every additive that SHRINKS per use grows instead, at Flip Rate - e.g. 0.5 " +
                      "turns -2 per use into +1. Growing and non-changing cards are untouched. Several flip syrups don't " +
                      "stack - the best rate is used. Applies before any Flat Addition syrups."
                    : "For the rest of the run, adds this to the Amount Change Per Use (and Amount Change When Played) of " +
                      "every additive whose change is NOT 0 - cards that don't grow or shrink are unaffected. E.g. +1 turns a " +
                      "+2-per-use card into +3, and a -2 card into -1.",
                    MessageType.None);
                break;
            }
        }
    }
}
#endif