#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(AdditiveData))]
public class AdditiveDataEditor : Editor
{
    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        SodaEditorFields.DrawAdditive(serializedObject);
        serializedObject.ApplyModifiedProperties();
    }
}

[CustomEditor(typeof(FlavorComboRule))]
public class FlavorComboRuleEditor : Editor
{
    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        SodaEditorFields.DrawComboRule(serializedObject);
        serializedObject.ApplyModifiedProperties();
    }
}

[CustomEditor(typeof(ModifierData))]
public class ModifierDataEditor : Editor
{
    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        SodaEditorFields.DrawModifier(serializedObject);
        serializedObject.ApplyModifiedProperties();
    }
}

/// <summary>
/// Adds an "Add All Additives In Project" button under ShopManager's normal Inspector,
/// so you don't have to manually drag every AdditiveData asset into additivePool one by
/// one. Adds whatever's missing rather than overwriting - safe to click again later after
/// creating new additives without disturbing pool entries you've already curated/ordered.
/// </summary>
[CustomEditor(typeof(ShopManager))]
public class ShopManagerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        EditorGUILayout.Space();
        if (GUILayout.Button("Add All Additives In Project"))
        {
            var shopManager = (ShopManager)target;
            var existing = new HashSet<AdditiveData>(shopManager.additivePool);
            int added = 0;

            foreach (var guid in AssetDatabase.FindAssets("t:AdditiveData"))
            {
                var asset = AssetDatabase.LoadAssetAtPath<AdditiveData>(AssetDatabase.GUIDToAssetPath(guid));
                if (asset == null || !existing.Add(asset)) continue;
                shopManager.additivePool.Add(asset);
                added++;
            }

            EditorUtility.SetDirty(shopManager);
            Debug.Log($"[ShopManagerEditor] Added {added} additive(s) to additivePool " +
                      $"({shopManager.additivePool.Count} total).");
        }
    }
}

/// <summary>
/// Same idea as ShopManagerEditor, but for ModifierManager's modifierPool.
/// </summary>
[CustomEditor(typeof(ModifierManager))]
public class ModifierManagerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        EditorGUILayout.Space();
        if (GUILayout.Button("Add All Modifiers (Syrups) In Project"))
        {
            var modifierManager = (ModifierManager)target;
            var existing = new HashSet<ModifierData>(modifierManager.modifierPool);
            int added = 0;

            foreach (var guid in AssetDatabase.FindAssets("t:ModifierData"))
            {
                var asset = AssetDatabase.LoadAssetAtPath<ModifierData>(AssetDatabase.GUIDToAssetPath(guid));
                if (asset == null || !existing.Add(asset)) continue;
                modifierManager.modifierPool.Add(asset);
                added++;
            }

            EditorUtility.SetDirty(modifierManager);
            Debug.Log($"[ModifierManagerEditor] Added {added} modifier(s) to modifierPool " +
                      $"({modifierManager.modifierPool.Count} total).");
        }
    }
}
#endif