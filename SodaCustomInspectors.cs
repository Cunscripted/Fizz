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
        serializedObject.Update();

        // The pool gets Add All / Clear buttons right under it; everything else draws as normal.
        var pool = serializedObject.FindProperty("additivePool");
        var it = serializedObject.GetIterator();
        bool enterChildren = true;
        while (it.NextVisible(enterChildren))
        {
            enterChildren = false;
            if (it.propertyPath == "m_Script")
            {
                using (new EditorGUI.DisabledScope(true)) EditorGUILayout.PropertyField(it);
                continue;
            }
            if (it.propertyPath == "additivePool")
            {
                SodaEditorFields.DrawAdditivePool(pool);
                continue;
            }
            EditorGUILayout.PropertyField(it, true);
        }

        serializedObject.ApplyModifiedProperties();
    }
}

/// <summary>
/// RoundManager's normal Inspector plus a Starting Deck Sets helper: pick the active set by name
/// and create new sets (empty, or copied from the plain Starting Additives list / another set).
/// </summary>
[CustomEditor(typeof(RoundManager))]
public class RoundManagerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        var rm = (RoundManager)target;
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Starting Deck Sets - quick tools", EditorStyles.boldLabel);

        if (rm.startingDeckSets.Count > 0)
        {
            var names = new string[rm.startingDeckSets.Count];
            for (int i = 0; i < names.Length; i++)
            {
                var set = rm.startingDeckSets[i];
                int count = set?.additives?.Count ?? 0;
                names[i] = $"{i}: {(string.IsNullOrWhiteSpace(set?.setName) ? "(unnamed)" : set.setName)}  ({count})";
            }

            EditorGUI.BeginChangeCheck();
            int picked = EditorGUILayout.Popup("Active Set", Mathf.Clamp(rm.activeSetIndex, 0, names.Length - 1), names);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(rm, "Change Active Starting Deck");
                rm.activeSetIndex = picked;
                if (rm.startingDeckChoice == RoundManager.StartingDeckChoice.UseStartingAdditivesList)
                    rm.startingDeckChoice = RoundManager.StartingDeckChoice.UseActiveSet;
                EditorUtility.SetDirty(rm);
            }

            string using_ = rm.startingDeckChoice switch
            {
                RoundManager.StartingDeckChoice.UseActiveSet => $"Runs start with set '{rm.startingDeckSets[Mathf.Clamp(rm.activeSetIndex, 0, names.Length - 1)]?.setName}'.",
                RoundManager.StartingDeckChoice.RandomSet => "Runs start with a random set each time.",
                _ => "Runs start with the plain Starting Additives list (pick a set above to switch)."
            };
            EditorGUILayout.HelpBox(using_, MessageType.None);
        }
        else
        {
            EditorGUILayout.HelpBox("No sets yet - create one below.", MessageType.None);
        }

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("+ New Empty Set"))
            AddSet(rm, $"Deck {rm.startingDeckSets.Count + 1}", null);
        if (GUILayout.Button("+ New Set From Starting Additives"))
            AddSet(rm, $"Deck {rm.startingDeckSets.Count + 1}", rm.startingAdditives);
        EditorGUILayout.EndHorizontal();

        if (rm.startingDeckSets.Count > 0 && rm.activeSetIndex >= 0 && rm.activeSetIndex < rm.startingDeckSets.Count &&
            GUILayout.Button("Duplicate Active Set"))
        {
            var src = rm.startingDeckSets[rm.activeSetIndex];
            AddSet(rm, $"{src.setName} Copy", src.additives);
        }
    }

    private static void AddSet(RoundManager rm, string setName, List<AdditiveData> copyFrom)
    {
        Undo.RecordObject(rm, "Add Starting Deck Set");
        var set = new RoundManager.StartingDeckSet { setName = setName };
        if (copyFrom != null) set.additives.AddRange(copyFrom);
        rm.startingDeckSets.Add(set);
        rm.activeSetIndex = rm.startingDeckSets.Count - 1;
        EditorUtility.SetDirty(rm);
    }
}

/// <summary>
/// ModifierManager's normal Inspector, with Add All / Clear buttons right under modifierPool
/// that fill it with every ModifierData (syrup) asset in the project.
/// </summary>
[CustomEditor(typeof(ModifierManager))]
public class ModifierManagerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        SodaEditorFields.DrawInspectorWithPool(serializedObject, "modifierPool",
            p => SodaEditorFields.DrawAssetPool<ModifierData>(p, "Modifiers (Syrups)"));
        serializedObject.ApplyModifiedProperties();
    }
}

/// <summary>
/// SodaScoringManager's normal Inspector, with Add All / Clear buttons right under comboRules
/// that fill it with every FlavorComboRule asset in the project.
/// </summary>
[CustomEditor(typeof(SodaScoringManager))]
public class SodaScoringManagerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        SodaEditorFields.DrawInspectorWithPool(serializedObject, "comboRules",
            p => SodaEditorFields.DrawAssetPool<FlavorComboRule>(p, "Combo Rules"));
        serializedObject.ApplyModifiedProperties();
    }
}
#endif