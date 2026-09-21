using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Shows every FlavorComboRule currently satisfied by SodaBottle's cup, updating
/// LIVE as additives are added/removed (via SodaBottle.OnCupChanged) - not just at
/// scoring time. Also toggles a badge icon on the button that opens this panel, so
/// players can tell at a glance whether any combo is active before even opening it.
///
/// Uses SodaScoringManager.ComboRules as the source of truth, so this always
/// reflects the exact same rules actually used when the soda is scored.
/// </summary>
public class ActiveComboPanel : MonoBehaviour
{
    public SodaBottle bottle;
    public SodaScoringManager scoringManager;
    public LerpPanel panel;
    public Button toggleButton;
    public ActiveComboEntry entryPrefab;
    [Tooltip("Must have a VerticalLayoutGroup component - that's what arranges entries into a clean stacked " +
             "list. Without one, entries all spawn at the same position and distort/overlap each other.")]
    public RectTransform entryContainer;
    [Tooltip("Optional - closed automatically whenever this panel opens, and vice versa, so the two never sit open at once.")]
    public DeckStatsPanel deckStatsPanel;
    [Header("Click Outside To Close")]
    [Tooltip("Optional - a full-screen invisible Button (Raycast Target on, fully transparent is fine) that " +
             "closes whichever of this panel or DeckStatsPanel is open when clicked anywhere outside them. " +
             "Assign the SAME Button here and on DeckStatsPanel.clickOutsideBackdrop to share one backdrop " +
             "between both - since they already never show at the same time as each other, its onClick just " +
             "ends up calling Hide() on whichever one is actually open (the other one's Hide() is a harmless " +
             "no-op, guarded by IsShown below). Wired automatically in Awake() - no manual Inspector event " +
             "wiring needed. The backdrop GameObject is toggled active/inactive alongside this panel so it " +
             "only blocks clicks while something's actually open; place it at an earlier sibling index than " +
             "the panel(s) it closes (so a click landing ON the panel is captured by the panel first) but a " +
             "later one than everything else underneath (belt, buttons, etc.) that should stay covered while open.")]
    public Button clickOutsideBackdrop;

    [Header("Badge")]
    [Tooltip("Small icon overlaid on the toggle button - enabled only while at least one combo is currently active.")]
    public GameObject badgeIcon;

    private readonly List<ActiveComboEntry> _spawned = new List<ActiveComboEntry>();

    // Tracks each rule's last active/inactive state, so we only log a diagnostic when it
    // actually CHANGES - Refresh runs on every single cup edit, so logging unconditionally
    // would spam the console constantly while you're just dragging cards around.
    private readonly Dictionary<FlavorComboRule, bool> _lastActiveState = new Dictionary<FlavorComboRule, bool>();

    public bool IsShown => panel != null && panel.IsShown;

    public void Hide()
    {
        // Guards against being called when this specific panel isn't the one currently
        // open - matters for a shared clickOutsideBackdrop, whose click ends up calling
        // Hide() on both this and DeckStatsPanel regardless of which is actually shown.
        if (!IsShown) return;

        if (panel != null) panel.Hide();
        if (clickOutsideBackdrop != null) clickOutsideBackdrop.gameObject.SetActive(false);
    }

    private void Awake()
    {
        if (toggleButton != null)
            toggleButton.onClick.AddListener(ToggleSelf);
        if (clickOutsideBackdrop != null)
            clickOutsideBackdrop.onClick.AddListener(Hide);
    }

    private void ToggleSelf()
    {
        if (panel == null) return;

        if (panel.IsShown)
        {
            Hide();
        }
        else
        {
            deckStatsPanel?.Hide(); // close the other panel before opening this one
            panel.Show();
            if (clickOutsideBackdrop != null) clickOutsideBackdrop.gameObject.SetActive(true);
        }
    }

    private void OnEnable()
    {
        if (badgeIcon == null)
            Debug.LogError($"[ActiveComboPanel] '{name}' has no badgeIcon assigned - it can never show/hide.", this);
        if (bottle == null)
            Debug.LogError($"[ActiveComboPanel] '{name}' has no bottle assigned - it can't know what's in the scoring zone.", this);
        if (scoringManager == null)
            Debug.LogError($"[ActiveComboPanel] '{name}' has no scoringManager assigned - it can't check any combo rules.", this);

        if (bottle != null)
        {
            bottle.OnCupChanged.AddListener(Refresh);
            Refresh(bottle.GetCupInstances());
        }
        else
        {
            Refresh(new List<AdditiveInstance>());
        }
    }

#if UNITY_EDITOR
    // OnValidate runs in the Editor even when the GameObject is disabled (unlike
    // OnEnable) - this is the most common reason a live-updating indicator like this
    // silently never updates: its own object got turned off.
    private void OnValidate()
    {
        if (!gameObject.activeSelf)
        {
            Debug.LogWarning($"[ActiveComboPanel] '{name}' GameObject is disabled. OnEnable() will never run, " +
                              "so it never subscribes to SodaBottle.OnCupChanged and the badge will never update.", this);
        }
    }
#endif

    private void OnDisable()
    {
        if (bottle != null) bottle.OnCupChanged.RemoveListener(Refresh);
    }

    private void Refresh(List<AdditiveInstance> cup)
    {
        ClearSpawned();

        bool anyActive = false;
        if (scoringManager != null)
        {
            if (entryPrefab == null)
                Debug.LogError($"[ActiveComboPanel] '{name}' has no entryPrefab assigned - can't display active combos.", this);
            if (entryContainer == null)
                Debug.LogError($"[ActiveComboPanel] '{name}' has no entryContainer assigned.", this);
            else if (entryContainer.GetComponent<VerticalLayoutGroup>() == null)
                Debug.LogWarning($"[ActiveComboPanel] '{entryContainer.name}' has no VerticalLayoutGroup - " +
                                  "entries will all spawn at the same position and distort/overlap each other. " +
                                  "Add one in the Inspector.", entryContainer);

            foreach (var rule in scoringManager.ComboRules)
            {
                if (rule == null) continue;

                bool active = rule.Evaluate(cup, out _, out var failReason);

                if (!_lastActiveState.TryGetValue(rule, out bool wasActive) || wasActive != active)
                {
                    _lastActiveState[rule] = active;
                    if (active)
                        Debug.Log($"[ActiveComboPanel] Combo '{rule.comboName}' is now ACTIVE.", rule);
                    else if (!string.IsNullOrEmpty(failReason))
                        Debug.Log($"[ActiveComboPanel] Combo '{rule.comboName}' is not active: {failReason}.", rule);
                }

                if (!active) continue;
                anyActive = true;

                if (entryPrefab != null && entryContainer != null)
                {
                    var entry = Instantiate(entryPrefab, entryContainer);
                    entry.SetData(rule);
                    _spawned.Add(entry);
                }
            }

            if (entryContainer != null)
            {
                // Force an immediate layout pass rather than waiting for Unity's next
                // automatic rebuild - belt-and-braces against same-frame timing issues.
                LayoutRebuilder.ForceRebuildLayoutImmediate(entryContainer);
            }
        }

        if (badgeIcon != null) badgeIcon.SetActive(anyActive);
    }

    private void ClearSpawned()
    {
        // Detach from entryContainer BEFORE destroying - Destroy() is deferred to the end
        // of the frame, but a layout rebuild can run synchronously the same frame, still
        // counting a soon-to-be-destroyed entry until it actually vanishes a moment later.
        foreach (var e in _spawned)
        {
            if (e == null) continue;
            e.transform.SetParent(null);
            Destroy(e.gameObject);
        }
        _spawned.Clear();

        // Extra safety net: same treatment for any untracked leftover children, not just
        // what _spawned was tracking - guarantees a clean slate even if tracking somehow
        // got out of sync.
        if (entryContainer != null)
        {
            for (int i = entryContainer.childCount - 1; i >= 0; i--)
            {
                var child = entryContainer.GetChild(i);
                child.SetParent(null);
                Destroy(child.gameObject);
            }
        }
    }
}