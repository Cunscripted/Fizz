using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Shows every additive currently in the player's owned collection, opened from
/// the pause menu. Reuses AdditiveCard (draggable=false) for each entry so hover
/// details and icon display work identically to gameplay - just not drag/drop.
///
/// Entries are laid out by a GridLayoutGroup on entryContainer - add that
/// component in the Inspector (set Cell Size, Spacing, and Constraint however you
/// want the grid to wrap) rather than positioning entries manually here.
///
/// panel.Show() is called BEFORE spawning entries, not after - Unity's layout
/// system doesn't run its automatic rebuild pass on objects that are inactive in
/// the hierarchy, and LerpPanel deactivates its GameObject while hidden. A forced
/// rebuild after spawning covers it either way.
/// </summary>
public class DeckStatsPanel : MonoBehaviour
{
    public RoundManager roundManager;
    public LerpPanel panel;
    public AdditiveCard statEntryPrefab;
    [Tooltip("Must have a GridLayoutGroup component - that's what arranges entries into rows/columns.")]
    public RectTransform entryContainer;
    [Tooltip("Optional - closed automatically whenever this panel opens, and vice versa, so the two never sit open at once.")]
    public ActiveComboPanel activeComboPanel;

    private readonly List<AdditiveCard> _spawned = new List<AdditiveCard>();

    public bool IsShown => panel != null && panel.IsShown;

    /// <summary>Closes it if already open, opens it otherwise - what Toggle actually means for a UI panel.</summary>
    public void Toggle()
    {
        if (IsShown) Hide();
        else Show();
    }

    public void Show()
    {
        if (statEntryPrefab == null)
        {
            Debug.LogError($"[DeckStatsPanel] '{name}' has no statEntryPrefab assigned - can't populate the deck stats grid.", this);
            return;
        }
        if (entryContainer == null)
        {
            Debug.LogError($"[DeckStatsPanel] '{name}' has no entryContainer assigned.", this);
            return;
        }
        if (panel == null)
        {
            Debug.LogError($"[DeckStatsPanel] '{name}' has no panel (LerpPanel) assigned - can't show deck stats.", this);
            return;
        }
        if (entryContainer.GetComponent<GridLayoutGroup>() == null)
        {
            Debug.LogWarning($"[DeckStatsPanel] '{entryContainer.name}' has no GridLayoutGroup component - " +
                              "entries will all spawn at the same position and stack on top of each other. " +
                              "Add one in the Inspector.", entryContainer);
        }

        // Close the other panel before opening this one - see IsShown/ActiveComboPanel wiring.
        activeComboPanel?.Hide();

        // Activate the panel FIRST so entryContainer is active in the hierarchy
        // before any entries are parented under it - see class comment above.
        panel.Show();

        Populate();

        // Force an immediate layout pass rather than waiting for Unity's next
        // automatic rebuild - belt-and-braces in case the container was still
        // settling from the panel's own show animation this same frame.
        LayoutRebuilder.ForceRebuildLayoutImmediate(entryContainer);
    }

    public void Hide()
    {
        panel.Hide();
    }

    private void Populate()
    {
        // Detach from entryContainer BEFORE destroying - Destroy() is deferred to the end
        // of the frame, but the forced layout rebuild after spawning runs synchronously
        // the same frame, so it would still count these as children until they actually
        // vanish a moment later. Detaching (SetParent(null)) takes effect immediately.
        foreach (var c in _spawned)
        {
            if (c == null) continue;
            c.transform.SetParent(null);
            Destroy(c.gameObject);
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

        if (roundManager == null) return;

        foreach (var instance in roundManager.OwnedAdditives)
        {
            var card = Instantiate(statEntryPrefab, entryContainer);
            card.draggable = false;
            card.instance = instance;
            _spawned.Add(card);
        }
    }
}