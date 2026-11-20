using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AutoSaveTool
{
    /// <summary>
    /// Editor window that periodically autosaves open scenes and/or the project.
    /// Tools > Auto Save > Window
    /// </summary>
    public class AutoSaveWindow : EditorWindow
    {
        // ---- EditorPrefs keys (per-project via PlayerSettings.productName prefix) ----
        private const string PrefPrefix = "AutoSaveTool_";
        private static string KeyEnabled => PrefPrefix + "Enabled";
        private static string KeyInterval => PrefPrefix + "IntervalMinutes";
        private static string KeySaveScenes => PrefPrefix + "SaveScenes";
        private static string KeySaveProject => PrefPrefix + "SaveProject";
        private static string KeyNotify => PrefPrefix + "Notify";
        private static string KeyOnlyWhenIdle => PrefPrefix + "OnlyWhenIdle";

        // ---- Settings ----
        private bool _enabled;
        private float _intervalMinutes = 10f;
        private bool _saveScenes = true;
        private bool _saveProject = true;
        private bool _notify = true;
        private bool _onlyWhenIdle = true; // skip autosave while in Play mode

        // ---- Runtime state ----
        private double _lastSaveTime;
        private string _lastSaveTimestamp = "Never";

        [MenuItem("Tools/Auto Save/Window")]
        public static void ShowWindow()
        {
            var window = GetWindow<AutoSaveWindow>("Auto Save");
            window.minSize = new Vector2(300, 260);
        }

        private void OnEnable()
        {
            LoadPrefs();
            _lastSaveTime = EditorApplication.timeSinceStartup;
            EditorApplication.update += OnEditorUpdate;
        }

        private void OnDisable()
        {
            EditorApplication.update -= OnEditorUpdate;
            SavePrefs();
        }

        private void LoadPrefs()
        {
            _enabled = EditorPrefs.GetBool(KeyEnabled, false);
            _intervalMinutes = EditorPrefs.GetFloat(KeyInterval, 10f);
            _saveScenes = EditorPrefs.GetBool(KeySaveScenes, true);
            _saveProject = EditorPrefs.GetBool(KeySaveProject, true);
            _notify = EditorPrefs.GetBool(KeyNotify, true);
            _onlyWhenIdle = EditorPrefs.GetBool(KeyOnlyWhenIdle, true);
        }

        private void SavePrefs()
        {
            EditorPrefs.SetBool(KeyEnabled, _enabled);
            EditorPrefs.SetFloat(KeyInterval, _intervalMinutes);
            EditorPrefs.SetBool(KeySaveScenes, _saveScenes);
            EditorPrefs.SetBool(KeySaveProject, _saveProject);
            EditorPrefs.SetBool(KeyNotify, _notify);
            EditorPrefs.SetBool(KeyOnlyWhenIdle, _onlyWhenIdle);
        }

        private void OnEditorUpdate()
        {
            if (!_enabled) return;
            if (_onlyWhenIdle && EditorApplication.isPlayingOrWillChangePlaymode) return;

            double elapsedSeconds = EditorApplication.timeSinceStartup - _lastSaveTime;
            double intervalSeconds = Math.Max(1, _intervalMinutes) * 60d;

            if (elapsedSeconds >= intervalSeconds)
            {
                PerformAutoSave();
            }

            // Repaint periodically so the countdown label stays live.
            Repaint();
        }

        private void PerformAutoSave()
        {
            try
            {
                if (_saveScenes)
                {
                    // Saves all currently open, dirty scenes.
                    for (int i = 0; i < SceneManager.sceneCount; i++)
                    {
                        var scene = SceneManager.GetSceneAt(i);
                        if (scene.isDirty && scene.IsValid() && !string.IsNullOrEmpty(scene.path))
                        {
                            EditorSceneManager.SaveScene(scene);
                        }
                    }
                }

                if (_saveProject)
                {
                    AssetDatabase.SaveAssets();
                }

                _lastSaveTime = EditorApplication.timeSinceStartup;
                _lastSaveTimestamp = DateTime.Now.ToString("HH:mm:ss");

                if (_notify)
                {
                    Debug.Log($"[AutoSave] Saved at {_lastSaveTimestamp}");
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[AutoSave] Save failed: {e.Message}");
            }
        }

        private void OnGUI()
        {
            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("Auto Save", EditorStyles.boldLabel);
            EditorGUILayout.Space(4);

            EditorGUI.BeginChangeCheck();

            _enabled = EditorGUILayout.ToggleLeft("Enable Auto Save", _enabled);

            EditorGUI.BeginDisabledGroup(!_enabled);

            _intervalMinutes = EditorGUILayout.Slider(
                new GUIContent("Interval (minutes)", "How often to autosave."),
                _intervalMinutes, 0.5f, 60f);

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("What to save", EditorStyles.boldLabel);
            _saveScenes = EditorGUILayout.ToggleLeft("Open scenes", _saveScenes);
            _saveProject = EditorGUILayout.ToggleLeft("Project assets (AssetDatabase.SaveAssets)", _saveProject);

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Behavior", EditorStyles.boldLabel);
            _onlyWhenIdle = EditorGUILayout.ToggleLeft("Skip while in Play mode", _onlyWhenIdle);
            _notify = EditorGUILayout.ToggleLeft("Log message on save", _notify);

            EditorGUI.EndDisabledGroup();

            if (EditorGUI.EndChangeCheck())
            {
                SavePrefs();
            }

            EditorGUILayout.Space(10);
            DrawStatus();

            EditorGUILayout.Space(10);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Save Now", GUILayout.Height(28)))
                {
                    PerformAutoSave();
                }
                if (GUILayout.Button("Reset Timer", GUILayout.Height(28)))
                {
                    _lastSaveTime = EditorApplication.timeSinceStartup;
                }
            }
        }

        private void DrawStatus()
        {
            EditorGUILayout.LabelField("Last save:", _lastSaveTimestamp);

            if (_enabled)
            {
                double intervalSeconds = Math.Max(1, _intervalMinutes) * 60d;
                double elapsed = EditorApplication.timeSinceStartup - _lastSaveTime;
                double remaining = Math.Max(0, intervalSeconds - elapsed);
                var ts = TimeSpan.FromSeconds(remaining);
                EditorGUILayout.LabelField("Next save in:", $"{ts.Minutes:00}:{ts.Seconds:00}");

                Rect r = EditorGUILayout.GetControlRect(false, 4);
                float pct = intervalSeconds > 0 ? (float)(elapsed / intervalSeconds) : 0f;
                EditorGUI.ProgressBar(r, Mathf.Clamp01(pct), "");
            }
            else
            {
                EditorGUILayout.HelpBox("Auto Save is disabled.", MessageType.Info);
            }
        }
    }
}