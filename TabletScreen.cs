using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Drives the tablet's screen Animator through Off -> TurningOn -> On (and optionally
/// TurningOff -> Off), and shows the "only when on" objects (tab buttons, title, close
/// button...) only once the On state is actually reached.
///
/// The Animator Controller is generated for you by Soda > Tablet Screen Animator Builder
/// (states "Off", "TurningOn", "On", "TurningOff" and a bool parameter "IsOn"). You can also
/// build your own, as long as it uses those state names and that parameter.
///
/// TabletMenu calls PowerOn()/PowerOff() automatically when its Screen field points here.
/// </summary>
public class TabletScreen : MonoBehaviour
{
    [Tooltip("The Animator playing the screen animations (usually on the screen Image).")]
    public Animator animator;
    [Tooltip("Shown only while the screen is fully On - the tab buttons, title, close button, etc. " +
             "Hidden instantly when the screen starts turning off.")]
    public List<GameObject> showWhenOn = new List<GameObject>();

    [Header("Animator Names (match the generated controller)")]
    public string isOnParameter = "IsOn";
    public string offState = "Off";
    public string onState = "On";

    [Header("Timing")]
    [Tooltip("Run the Animator on unscaled time, so it still plays if the game is paused (e.g. TabletMenu's Pause Time While Open).")]
    public bool useUnscaledTime = true;
    [Tooltip("Safety net: if the Animator never reaches On within this many seconds (missing controller, wrong state " +
             "names...), treat the screen as on anyway so the buttons still appear. Also logs a warning.")]
    public float maxWaitSeconds = 5f;

    /// <summary>True while the Animator is in the On state (buttons are showing).</summary>
    public bool IsOn { get; private set; }

    public event Action OnTurnedOn;
    public event Action OnTurnedOff;

    private Coroutine _routine;
    private Action _pendingThen; // what the running wait will do when it finishes
    private int _onHash, _offHash;
    private bool _warnedAncestor;

    private void Awake()
    {
        _onHash = Animator.StringToHash(onState);
        _offHash = Animator.StringToHash(offState);
        if (animator != null && useUnscaledTime) animator.updateMode = AnimatorUpdateMode.UnscaledTime;
        // IsOn, not false: if PowerOn() already finished before this object was ever active
        // (e.g. it lives under something that gets enabled later), don't undo it here.
        SetOnObjects(IsOn);
    }

    private void OnDisable()
    {
        // If this object gets switched off mid-animation, its coroutine dies with it. Finish the
        // pending step right away instead, so whoever is waiting (TabletMenu) never gets stuck.
        if (_routine != null)
        {
            _routine = null;
            var then = _pendingThen;
            _pendingThen = null;
            then?.Invoke();
        }
    }

    /// <summary>Plays TurningOn, then calls onReady (and shows the On objects) once the On state is reached.</summary>
    public void PowerOn(Action onReady = null)
    {
        StopRoutine();
        IsOn = false;
        SetOnObjects(false);

        if (!HasController() || !isActiveAndEnabled)
        {
            FinishOn(onReady);
            return;
        }

        // The tablet usually was just activated, which resets the Animator to its default (Off) state.
        animator.SetBool(isOnParameter, true);
        _routine = StartCoroutine(WaitForState(_onHash, () => FinishOn(onReady)));
    }

    /// <summary>Skips the turn-on animation and jumps straight to On (used when something needs a page open immediately).</summary>
    public void PowerOnInstant()
    {
        StopRoutine();
        if (HasController())
        {
            animator.SetBool(isOnParameter, true);
            animator.Play(onState, 0, 0f);
        }
        FinishOn(null);
    }

    /// <summary>Hides the On objects immediately, plays TurningOff (if the controller has it), then calls onDone once Off.</summary>
    public void PowerOff(Action onDone = null)
    {
        StopRoutine();
        IsOn = false;
        SetOnObjects(false);

        if (!HasController() || !isActiveAndEnabled)
        {
            onDone?.Invoke();
            OnTurnedOff?.Invoke();
            return;
        }

        animator.SetBool(isOnParameter, false);
        _routine = StartCoroutine(WaitForState(_offHash, () =>
        {
            onDone?.Invoke();
            OnTurnedOff?.Invoke();
        }));
    }

    private void FinishOn(Action onReady)
    {
        IsOn = true;
        SetOnObjects(true);
        onReady?.Invoke();
        OnTurnedOn?.Invoke();
    }

    private IEnumerator WaitForState(int stateHash, Action then)
    {
        _pendingThen = then;
        float waited = 0f;
        // Wait at least one frame so the Animator has processed the parameter change.
        yield return null;
        while (!IsInState(stateHash))
        {
            waited += Time.unscaledDeltaTime;
            if (waited > maxWaitSeconds)
            {
                Debug.LogWarning($"[TabletScreen] '{name}' waited {maxWaitSeconds}s for the Animator to reach a state " +
                                 $"that never came - check the controller has states named '{offState}' / '{onState}' " +
                                 $"and a bool parameter '{isOnParameter}'. Continuing anyway.", this);
                break;
            }
            yield return null;
        }
        _routine = null;
        _pendingThen = null;
        then?.Invoke();
    }

    private bool IsInState(int hash)
    {
        if (animator == null || !animator.isActiveAndEnabled) return true;
        if (animator.IsInTransition(0))
            return animator.GetNextAnimatorStateInfo(0).shortNameHash == hash;
        return animator.GetCurrentAnimatorStateInfo(0).shortNameHash == hash;
    }

    private bool HasController() =>
        animator != null && animator.runtimeAnimatorController != null && animator.isActiveAndEnabled;

    private void SetOnObjects(bool on)
    {
        foreach (var go in showWhenOn)
        {
            if (go == null) continue;

            // Never switch off the screen itself (or anything it lives under) - that would stop the
            // animation, so it could never reach On and the buttons would never come back.
            if (!on && (IsSelfOrAncestor(go, transform) || (animator != null && IsSelfOrAncestor(go, animator.transform))))
            {
                if (!_warnedAncestor)
                {
                    _warnedAncestor = true;
                    Debug.LogWarning($"[TabletScreen] '{go.name}' is in Show When On, but it contains the screen/Animator " +
                                     "itself, so it's left visible. Only list the buttons (or a button bar that doesn't " +
                                     "contain the screen).", go);
                }
                continue;
            }
            go.SetActive(on);
        }
    }

    private static bool IsSelfOrAncestor(GameObject candidate, Transform t)
    {
        for (var p = t; p != null; p = p.parent)
            if (p.gameObject == candidate) return true;
        return false;
    }

    private void StopRoutine()
    {
        if (_routine != null) StopCoroutine(_routine);
        _routine = null;
    }
}