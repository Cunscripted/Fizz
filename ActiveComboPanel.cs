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

    [Tooltip("Set automatically by TabletMenu. While set, the toggle button / backdrop never close this page - " +
             "the tablet's tabs and close button do.")]
    public TabletMenu tablet;

    [Header("Badge")]
    [Tooltip("Small icon overlaid on the toggle button - enabled only while at least one combo is currently active.")]
    public GameObject badgeIcon;
    [Tooltip("Extra indicators that show/hide together with Badge Icon - e.g. an image on the tablet itself or on " +
             "the Combos app button. Each is turned on only while at least one combo is active. Don't list " +
             "anything that contains this panel or the tablet, or they'd get hidden too.")]
    public List<GameObject> extraBadgeIcons = new List<GameObject>();

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

    private bool _subscribed;

    private void Awake()
    {
        // Listen to the cup from Awake to OnDestroy (not just while enabled), so the badges keep
        // updating while this page is hidden - LerpPanel deactivates hidden pages, and the badge
        // on the tablet/button matters most exactly when the Combos page ISN'T open.
        Subscribe();

        if (toggleButton != null)
            toggleButton.onClick.AddListener(ToggleSelf);
        if (clickOutsideBackdrop != null)
            clickOutsideBackdrop.onClick.AddListener(OnBackdropClicked);
    }

    private void OnBackdropClicked()
    {
        if (tablet != null) return; // the tablet handles closing
        Hide();
    }

    private void ToggleSelf()
    {
        if (panel == null) return;
        if (tablet != null) { tablet.ShowTab(TabletMenu.Tab.Combos); return; }

        if (panel.IsShown) Hide();
        else Show();
    }

    /// <summary>Opens the panel (closing DeckStatsPanel first). Used by TabletMenu's Combos tab.</summary>
    public void Show()
    {
        if (panel == null) return;
        deckStatsPanel?.Hide(); // close the other panel before opening this one
        panel.Show();
        if (clickOutsideBackdrop != null && tablet == null) clickOutsideBackdrop.gameObject.SetActive(true);
        Refresh(bottle != null ? bottle.GetCupInstances() : new List<AdditiveInstance>());
    }

    private void OnEnable()
    {
        if (badgeIcon == null && extraBadgeIcons.Count == 0)
            Debug.LogError($"[ActiveComboPanel] '{name}' has no badgeIcon (or Extra Badge Icons) assigned - nothing can show/hide.", this);
        if (bottle == null)
            Debug.LogError($"[ActiveComboPanel] '{name}' has no bottle assigned - it can't know what's in the scoring zone.", this);
        if (scoringManager == null)
            Debug.LogError($"[ActiveComboPanel] '{name}' has no scoringManager assigned - it can't check any combo rules.", this);

        if (bottle != null)
        {
            Subscribe();
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

    private void Subscribe()
    {
        if (_subscribed || bottle == null) return;
        bottle.OnCupChanged.AddListener(Refresh);
        _subscribed = true;
    }

    private void OnDestroy()
    {
        if (_subscribed && bottle != null) bottle.OnCupChanged.RemoveListener(Refresh);
        _subscribed = false;
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
                    entry.SetData(rule, scoringManager.GetProgress(rule));
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
        foreach (var icon in extraBadgeIcons)
            if (icon != null && icon != gameObject) icon.SetActive(anyActive);
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