#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Soda > Tablet Screen Animator Builder
///
/// Generates the tablet screen's Animator Controller (the "animation tree"):
///
///     [Off] --IsOn--> [TurningOn] --(finishes)--> [On, loops]
///       ^                                            |
///       +------(finishes)---- [TurningOff] <--!IsOn--+
///
/// For each state you either assign an existing AnimationClip, or just drop in sprite
/// frames + a frame rate and the builder makes the clip for you (it animates the Image's
/// sprite on the SAME GameObject as the Animator). Off can be a single image. TurningOff
/// is optional - leave it empty and the screen snaps straight to Off.
///
/// Re-running with the same output path overwrites the controller/clips, so you can tweak
/// frames and rebuild freely.
/// </summary>
public class TabletScreenAnimatorBuilder : EditorWindow
{
    [System.Serializable]
    private class StateSource
    {
        public bool useClip;
        public AnimationClip clip;
        public List<Sprite> frames = new List<Sprite>();
        public float fps = 12f;
    }

    private StateSource _off = new StateSource();
    private StateSource _turningOn = new StateSource();
    private StateSource _on = new StateSource { fps = 8f };
    private StateSource _turningOff = new StateSource();
    private bool _includeTurningOff;
    private string _folder = "Assets/Soda/Tablet";
    private string _controllerName = "TabletScreen";
    private Animator _assignTo;
    private Vector2 _scroll;

    // Holds the lists so Unity's list drawer (drag-and-drop of many sprites at once) works.
    private SerializedObject _so;
    [SerializeField] private List<Sprite> offFrames = new List<Sprite>();
    [SerializeField] private List<Sprite> turningOnFrames = new List<Sprite>();
    [SerializeField] private List<Sprite> onFrames = new List<Sprite>();
    [SerializeField] private List<Sprite> turningOffFrames = new List<Sprite>();

    [MenuItem("Soda/Tablet Screen Animator Builder")]
    public static void Open()
    {
        var win = GetWindow<TabletScreenAnimatorBuilder>("Tablet Screen Animator");
        win.minSize = new Vector2(420, 520);
    }

    private void OnEnable() => _so = new SerializedObject(this);

    private void OnGUI()
    {
        _so.Update();
        _scroll = EditorGUILayout.BeginScrollView(_scroll);

        EditorGUILayout.HelpBox("Builds an Animator Controller with states Off -> TurningOn -> On (looping) and an optional " +
                                "TurningOff, driven by a bool parameter 'IsOn'. For each state, assign a clip OR drop in " +
                                "sprite frames (drag a whole selection of sprites onto the list header).", MessageType.Info);

        DrawState("Off (screen dark - one sprite is fine)", _off, "offFrames");
        DrawState("Turning On (plays once)", _turningOn, "turningOnFrames");
        DrawState("On (loops while open)", _on, "onFrames");

        EditorGUILayout.Space();
        _includeTurningOff = EditorGUILayout.ToggleLeft("Include a Turning Off animation", _includeTurningOff);
        if (_includeTurningOff) DrawState("Turning Off (plays once)", _turningOff, "turningOffFrames");

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Output", EditorStyles.boldLabel);
        _folder = EditorGUILayout.TextField("Folder", _folder);
        _controllerName = EditorGUILayout.TextField("Controller Name", _controllerName);
        _assignTo = (Animator)EditorGUILayout.ObjectField(
            new GUIContent("Assign To (optional)", "An Animator in the scene to hook the new controller up to - usually on the tablet's screen Image."),
            _assignTo, typeof(Animator), true);

        EditorGUILayout.Space();
        if (GUILayout.Button("Build Animator Controller", GUILayout.Height(32))) Build();

        EditorGUILayout.EndScrollView();
        _so.ApplyModifiedProperties();
    }

    private void DrawState(string label, StateSource src, string framesProperty)
    {
        EditorGUILayout.Space();
        EditorGUILayout.LabelField(label, EditorStyles.boldLabel);
        src.useClip = GUILayout.Toolbar(src.useClip ? 1 : 0, new[] { "Sprite Frames", "Existing Clip" }) == 1;
        if (src.useClip)
        {
            src.clip = (AnimationClip)EditorGUILayout.ObjectField("Clip", src.clip, typeof(AnimationClip), false);
        }
        else
        {
            EditorGUILayout.PropertyField(_so.FindProperty(framesProperty), new GUIContent("Frames"), true);
            src.fps = Mathf.Max(0.1f, EditorGUILayout.FloatField("Frames Per Second", src.fps));
        }
    }

    private void Build()
    {
        _so.ApplyModifiedProperties();
        _off.frames = offFrames;
        _turningOn.frames = turningOnFrames;
        _on.frames = onFrames;
        _turningOff.frames = turningOffFrames;

        EnsureFolder(_folder);
        string baseName = string.IsNullOrWhiteSpace(_controllerName) ? "TabletScreen" : _controllerName.Trim();

        var offClip = MakeClip(_off, $"{baseName}_Off", loop: true);
        var turningOnClip = MakeClip(_turningOn, $"{baseName}_TurningOn", loop: false);
        var onClip = MakeClip(_on, $"{baseName}_On", loop: true);
        var turningOffClip = _includeTurningOff ? MakeClip(_turningOff, $"{baseName}_TurningOff", loop: false) : null;

        string path = $"{_folder}/{baseName}.controller";
        if (AssetDatabase.LoadAssetAtPath<AnimatorController>(path) != null) AssetDatabase.DeleteAsset(path);
        var controller = AnimatorController.CreateAnimatorControllerAtPath(path);
        controller.AddParameter("IsOn", AnimatorControllerParameterType.Bool);

        var sm = controller.layers[0].stateMachine;
        var off = sm.AddState("Off", new Vector3(250, 0, 0));
        var on = sm.AddState("On", new Vector3(750, 0, 0));
        off.motion = offClip;
        on.motion = onClip;
        sm.defaultState = off;

        // Off -> (TurningOn ->) On
        if (turningOnClip != null)
        {
            var turningOn = sm.AddState("TurningOn", new Vector3(500, -100, 0));
            turningOn.motion = turningOnClip;
            AddConditionTransition(off, turningOn, true);
            AddExitTransition(turningOn, on);
            AddConditionTransition(turningOn, off, false); // closed mid-way: go straight back to Off
        }
        else
        {
            AddConditionTransition(off, on, true);
        }

        // On -> (TurningOff ->) Off
        if (turningOffClip != null)
        {
            var turningOff = sm.AddState("TurningOff", new Vector3(500, 100, 0));
            turningOff.motion = turningOffClip;
            AddConditionTransition(on, turningOff, false);
            AddExitTransition(turningOff, off);
            AddConditionTransition(turningOff, on, true); // reopened mid-way
        }
        else
        {
            AddConditionTransition(on, off, false);
        }

        AssetDatabase.SaveAssets();

        if (_assignTo != null)
        {
            Undo.RecordObject(_assignTo, "Assign Tablet Screen Controller");
            _assignTo.runtimeAnimatorController = controller;
            EditorUtility.SetDirty(_assignTo);
            if (_assignTo.GetComponent<Image>() == null && !(_off.useClip && _on.useClip))
                Debug.LogWarning("[TabletScreenAnimatorBuilder] The sprite-frame clips animate an Image on the SAME " +
                                 $"GameObject as the Animator, but '{_assignTo.name}' has no Image.", _assignTo);
        }

        EditorGUIUtility.PingObject(controller);
        Debug.Log($"[TabletScreenAnimatorBuilder] Built '{path}'.", controller);
    }

    /// <summary>Returns the assigned clip, or builds a sprite-swap clip from the frames (null if neither).</summary>
    private AnimationClip MakeClip(StateSource src, string clipName, bool loop)
    {
        if (src.useClip) return src.clip;

        var frames = src.frames.FindAll(f => f != null);
        if (frames.Count == 0) return null;

        var clip = new AnimationClip { name = clipName, frameRate = Mathf.Max(1f, src.fps) };
        var binding = EditorCurveBinding.PPtrCurve("", typeof(Image), "m_Sprite");
        float frameTime = 1f / src.fps;

        // One key per frame, plus a final key repeating the last sprite so it holds for its full frame.
        var keys = new ObjectReferenceKeyframe[frames.Count + 1];
        for (int i = 0; i < frames.Count; i++)
            keys[i] = new ObjectReferenceKeyframe { time = i * frameTime, value = frames[i] };
        keys[frames.Count] = new ObjectReferenceKeyframe { time = frames.Count * frameTime, value = frames[frames.Count - 1] };
        AnimationUtility.SetObjectReferenceCurve(clip, binding, keys);

        var settings = AnimationUtility.GetAnimationClipSettings(clip);
        settings.loopTime = loop;
        AnimationUtility.SetAnimationClipSettings(clip, settings);

        string path = $"{_folder}/{clipName}.anim";
        if (AssetDatabase.LoadAssetAtPath<AnimationClip>(path) != null) AssetDatabase.DeleteAsset(path);
        AssetDatabase.CreateAsset(clip, path);
        return clip;
    }

    private static void AddConditionTransition(AnimatorState from, AnimatorState to, bool isOn)
    {
        var t = from.AddTransition(to);
        t.hasExitTime = false;
        t.duration = 0f;
        t.AddCondition(isOn ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot, 0f, "IsOn");
    }

    private static void AddExitTransition(AnimatorState from, AnimatorState to)
    {
        var t = from.AddTransition(to);
        t.hasExitTime = true;
        t.exitTime = 1f;
        t.duration = 0f;
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        var parts = path.Split('/');
        string current = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            string next = current + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, parts[i]);
            current = next;
        }
    }
}
#endif
