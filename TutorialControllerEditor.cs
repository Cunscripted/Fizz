#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// Custom editor for authoring a TutorialController's steps against a mock (or real)
/// gameplay scene. Workflow: enter Play mode, select a step below, click "Jump To This
/// Step" to preview its highlight/text, then drag textBox around in the Scene view with
/// the normal Move tool and click "Save Textbox Position" to write wherever you dropped
/// it into that step's data. Also handles assigning the highlight target and padding
/// directly, and reordering/adding/removing steps.
/// </summary>
[CustomEditor(typeof(TutorialController))]
public class TutorialControllerEditor : Editor
{
    private int _previewStepIndex = 0;

    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        var controller = (TutorialController)target;

        DrawReferences();
        EditorGUILayout.Space();
        DrawStepsList();
        EditorGUILayout.Space();
        DrawPreviewTools(controller);

        serializedObject.ApplyModifiedProperties();
    }

    private void DrawReferences()
    {
        EditorGUILayout.LabelField("References", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(serializedObject.FindProperty("overlayRoot"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("textBox"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("textBoxLabel"));

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Spotlight Panels", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("Each panel needs a CENTER anchor + CENTER pivot on its Rect Transform - " +
                                "hold Alt+Shift and click 'middle center' in the anchor preset picker.", MessageType.Info);
        EditorGUILayout.PropertyField(serializedObject.FindProperty("spotlightTop"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("spotlightBottom"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("spotlightLeft"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("spotlightRight"));

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Buttons", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(serializedObject.FindProperty("nextButton"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("previousButton"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("skipButton"));

        EditorGUILayout.Space();
        EditorGUILayout.PropertyField(serializedObject.FindProperty("transitionDuration"));
    }

    private void DrawStepsList()
    {
        var stepsProp = serializedObject.FindProperty("steps");
        EditorGUILayout.LabelField($"Steps ({stepsProp.arraySize})", EditorStyles.boldLabel);

        for (int i = 0; i < stepsProp.arraySize; i++)
        {
            var stepProp = stepsProp.GetArrayElementAtIndex(i);
            var nameProp = stepProp.FindPropertyRelative("stepName");

            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.BeginHorizontal();
            stepProp.isExpanded = EditorGUILayout.Foldout(stepProp.isExpanded, $"{i}: {nameProp.stringValue}", true);
            if (GUILayout.Button("\u2191", GUILayout.Width(22)) && i > 0)
                stepsProp.MoveArrayElement(i, i - 1);
            if (GUILayout.Button("\u2193", GUILayout.Width(22)) && i < stepsProp.arraySize - 1)
                stepsProp.MoveArrayElement(i, i + 1);
            if (GUILayout.Button("Delete", GUILayout.Width(60)))
            {
                stepsProp.DeleteArrayElementAtIndex(i);
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.EndVertical();
                break; // array changed size - bail out of this loop pass, redraws next frame
            }
            EditorGUILayout.EndHorizontal();

            if (stepProp.isExpanded)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(nameProp);
                EditorGUILayout.PropertyField(stepProp.FindPropertyRelative("bodyText"));
                EditorGUILayout.PropertyField(stepProp.FindPropertyRelative("highlightTarget"));
                EditorGUILayout.PropertyField(stepProp.FindPropertyRelative("highlightPadding"));
                EditorGUILayout.PropertyField(stepProp.FindPropertyRelative("textBoxAnchoredPosition"));
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndVertical();
        }

        if (GUILayout.Button("+ Add Step"))
        {
            stepsProp.arraySize++;
            var newStep = stepsProp.GetArrayElementAtIndex(stepsProp.arraySize - 1);
            newStep.FindPropertyRelative("stepName").stringValue = $"Step {stepsProp.arraySize}";
            newStep.FindPropertyRelative("bodyText").stringValue = "";
            newStep.FindPropertyRelative("highlightPadding").floatValue = 8f;
            newStep.isExpanded = true;
        }
    }

    private void DrawPreviewTools(TutorialController controller)
    {
        EditorGUILayout.LabelField("Preview & Position Tool", EditorStyles.boldLabel);

        if (controller.steps.Count == 0)
        {
            EditorGUILayout.HelpBox("Add at least one step above first.", MessageType.Info);
            return;
        }

        _previewStepIndex = EditorGUILayout.IntSlider("Step", _previewStepIndex, 0, controller.steps.Count - 1);

        using (new EditorGUI.DisabledScope(!Application.isPlaying))
        {
            if (GUILayout.Button("Jump To This Step"))
                controller.EditorJumpToStep(_previewStepIndex);

            using (new EditorGUI.DisabledScope(controller.textBox == null))
            {
                if (GUILayout.Button("Save Current Textbox Position To This Step"))
                {
                    Undo.RecordObject(controller, "Save Tutorial Textbox Position");
                    controller.steps[_previewStepIndex].textBoxAnchoredPosition = controller.textBox.anchoredPosition;
                    EditorUtility.SetDirty(controller);
                }
            }
        }

        if (!Application.isPlaying)
        {
            EditorGUILayout.HelpBox("Enter Play Mode to preview steps and drag the textbox around with the " +
                                    "normal Move tool in the Scene view, then use the buttons above to jump " +
                                    "between steps and save wherever you've dropped it.", MessageType.Info);
        }
        else
        {
            EditorGUILayout.HelpBox("Select TutorialController > textBox in the Hierarchy, drag it with the " +
                                    "Move tool (Rect Tool works too) in the Scene/Game view, then click Save above.",
                                    MessageType.None);
        }
    }
}
#endif
