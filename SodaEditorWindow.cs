#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// One-stop window for building out the soda deckbuilder's data assets without
/// hunting through the Project window. Open via Soda > Additive & Modifier Editor.
/// Left panel lists existing assets of the selected type; right panel edits the
/// selection (or a freshly created one) using the same conditional field drawers
/// as the per-asset inspectors.
/// </summary>
public class SodaEditorWindow : EditorWindow
{
    private enum Tab { Additives, ComboRules, Modifiers }

    private const string AdditiveFolder = "Assets/Soda/Additives";
    private const string ComboFolder = "Assets/Soda/ComboRules";
    private const string ModifierFolder = "Assets/Soda/Modifiers";

    private Tab _tab = Tab.Additives;
    private Vector2 _listScroll;
    private Vector2 _detailScroll;
    private Object _selected;
    private SerializedObject _selectedSO;
    private string _newAssetName = "New Additive";

    [MenuItem("Soda/Additive & Modifier Editor")]
    public static void Open()
    {
        var win = GetWindow<SodaEditorWindow>("Soda Editor");
        win.minSize = new Vector2(640, 420);
    }

    private void OnGUI()
    {
        DrawToolbar();
        EditorGUILayout.Space(4);
        EditorGUILayout.BeginHorizontal();
        DrawList();
        DrawDetail();
        EditorGUILayout.EndHorizontal();
    }

    private void DrawToolbar()
    {
        EditorGUI.BeginChangeCheck();
        _tab = (Tab)GUILayout.Toolbar((int)_tab, new[] { "Additives", "Combo Rules", "Modifiers (Syrups)" });
        if (EditorGUI.EndChangeCheck())
        {
            _selected = null;
            _selectedSO = null;
            _newAssetName = _tab switch
            {
                Tab.Additives => "New Additive",
                Tab.ComboRules => "New Combo Rule",
                Tab.Modifiers => "New Modifier",
                _ => "New Asset"
            };
        }
    }

    private void DrawList()
    {
        EditorGUILayout.BeginVertical(GUILayout.Width(230));
        EditorGUILayout.LabelField("Existing Assets", EditorStyles.boldLabel);

        string filter = _tab switch
        {
            Tab.Additives => "t:AdditiveData",
            Tab.ComboRules => "t:FlavorComboRule",
            Tab.Modifiers => "t:ModifierData",
            _ => ""
        };

        var guids = AssetDatabase.FindAssets(filter);
        _listScroll = EditorGUILayout.BeginScrollView(_listScroll, "box", GUILayout.Height(280));
        foreach (var guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var obj = AssetDatabase.LoadAssetAtPath<Object>(path);
            if (obj == null) continue;

            bool isSelected = _selected == obj;
            bool nowSelected = GUILayout.Toggle(isSelected, obj.name, "Button");
            if (nowSelected && !isSelected)
            {
                _selected = obj;
                _selectedSO = new SerializedObject(obj);
            }
        }
        EditorGUILayout.EndScrollView();

        EditorGUILayout.Space();
        _newAssetName = EditorGUILayout.TextField("New Name", _newAssetName);
        if (GUILayout.Button("+ Create New"))
            CreateNewAsset();

        using (new EditorGUI.DisabledScope(_selected == null))
        {
            if (GUILayout.Button("Duplicate Selected"))
                DuplicateSelected();

            if (GUILayout.Button("Delete Selected"))
                DeleteSelected();
        }

        EditorGUILayout.Space();
        if (GUILayout.Button("Sync ALL Names to Assets"))
            SyncAllNamesToAssets();

        EditorGUILayout.EndVertical();
    }

    /// <summary>
    /// Sets every asset of the current tab's type to have its name field match its
    /// .asset file name (additiveName / comboName / modifierName). Useful after a
    /// batch of renames in the Project window.
    /// </summary>
    private void SyncAllNamesToAssets()
    {
        var (filter, nameField, typeLabel) = _tab switch
        {
            Tab.Additives => ("t:AdditiveData", "additiveName", "additive"),
            Tab.ComboRules => ("t:FlavorComboRule", "comboName", "combo rule"),
            Tab.Modifiers => ("t:ModifierData", "modifierName", "modifier"),
            _ => ("", "", "")
        };
        if (string.IsNullOrEmpty(filter)) return;

        var guids = AssetDatabase.FindAssets(filter);
        if (guids.Length == 0) return;

        if (!EditorUtility.DisplayDialog(
                "Sync Names",
                $"Set the name field to match the asset file name for all {guids.Length} {typeLabel}(s)? " +
                "This overwrites the current name field on each.",
                "Sync All", "Cancel"))
            return;

        int changed = 0;
        foreach (var guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var asset = AssetDatabase.LoadAssetAtPath<Object>(path);
            if (asset == null) continue;

            var so = new SerializedObject(asset);
            var nameProp = so.FindProperty(nameField);
            if (nameProp == null) continue;

            if (nameProp.stringValue != asset.name)
            {
                nameProp.stringValue = asset.name;
                so.ApplyModifiedProperties();
                EditorUtility.SetDirty(asset);
                changed++;
            }
        }

        AssetDatabase.SaveAssets();

        // Refresh the currently-open detail view in case it was one of the changed assets.
        if (_selected != null)
            _selectedSO = new SerializedObject(_selected);

        Debug.Log($"[Soda Editor] Synced {changed} {typeLabel}(s) to their asset file names.");
    }

    private void DrawDetail()
    {
        EditorGUILayout.BeginVertical();

        if (_selectedSO == null)
        {
            EditorGUILayout.HelpBox("Select an asset on the left, or create a new one.", MessageType.Info);
            EditorGUILayout.EndVertical();
            return;
        }

        // The underlying object can be deleted from outside this window (e.g. Project window).
        if (_selected == null)
        {
            _selectedSO = null;
            EditorGUILayout.EndVertical();
            return;
        }

        _selectedSO.Update();
        _detailScroll = EditorGUILayout.BeginScrollView(_detailScroll);

        switch (_tab)
        {
            case Tab.Additives:
                SodaEditorFields.DrawAdditive(_selectedSO);
                break;
            case Tab.ComboRules:
                SodaEditorFields.DrawComboRule(_selectedSO);
                break;
            case Tab.Modifiers:
                SodaEditorFields.DrawModifier(_selectedSO);
                break;
        }

        EditorGUILayout.EndScrollView();

        if (_selectedSO.ApplyModifiedProperties())
            EditorUtility.SetDirty(_selected);

        EditorGUILayout.EndVertical();
    }

    private void CreateNewAsset()
    {
        string folder = _tab switch
        {
            Tab.Additives => AdditiveFolder,
            Tab.ComboRules => ComboFolder,
            Tab.Modifiers => ModifierFolder,
            _ => "Assets"
        };
        EnsureFolder(folder);

        ScriptableObject asset = _tab switch
        {
            Tab.Additives => ScriptableObject.CreateInstance<AdditiveData>(),
            Tab.ComboRules => ScriptableObject.CreateInstance<FlavorComboRule>(),
            Tab.Modifiers => ScriptableObject.CreateInstance<ModifierData>(),
            _ => null
        };
        if (asset == null) return;

        string safeName = string.IsNullOrWhiteSpace(_newAssetName) ? "New Asset" : _newAssetName;
        string path = AssetDatabase.GenerateUniqueAssetPath($"{folder}/{safeName}.asset");
        AssetDatabase.CreateAsset(asset, path);
        AssetDatabase.SaveAssets();

        _selected = asset;
        _selectedSO = new SerializedObject(asset);
        EditorGUIUtility.PingObject(asset);
    }

    private void DuplicateSelected()
    {
        string srcPath = AssetDatabase.GetAssetPath(_selected);
        string newPath = AssetDatabase.GenerateUniqueAssetPath(srcPath);
        AssetDatabase.CopyAsset(srcPath, newPath);
        AssetDatabase.SaveAssets();

        var dup = AssetDatabase.LoadAssetAtPath<Object>(newPath);
        _selected = dup;
        _selectedSO = new SerializedObject(dup);
        EditorGUIUtility.PingObject(dup);
    }

    private void DeleteSelected()
    {
        if (!EditorUtility.DisplayDialog("Delete Asset", $"Delete '{_selected.name}'? This cannot be undone.", "Delete", "Cancel"))
            return;

        string path = AssetDatabase.GetAssetPath(_selected);
        AssetDatabase.DeleteAsset(path);
        _selected = null;
        _selectedSO = null;
    }

    private void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;

        var parts = path.Split('/');
        string current = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            string next = current + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next))
                AssetDatabase.CreateFolder(current, parts[i]);
            current = next;
        }
    }
}
#endif