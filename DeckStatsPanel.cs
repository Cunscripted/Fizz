using System.Collections.Generic;
using TMPro;
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
    [Tooltip("Optional - refreshed every time this panel repopulates, so the scrollbar's handle size stays " +
             "accurate as the collection grows (more owned additives = more rows = a smaller handle).")]
    public ScrollbarContentSizer scrollbarSizer;
    [Header("Click Outside To Close")]
    [Tooltip("Optional - a full-screen invisible Button (Raycast Target on, fully transparent is fine) that " +
             "closes whichever of this panel or ActiveComboPanel is open when clicked anywhere outside them. " +
             "Assign the SAME Button here and on ActiveComboPanel.clickOutsideBackdrop to share one backdrop " +
             "between both - since they already never show at the same time as each other, its onClick just " +
             "ends up calling Hide() on whichever one is actually open (the other one's Hide() is a harmless " +
             "no-op, guarded by IsShown below). Wired automatically in Awake() - no manual Inspector event " +
             "wiring needed. The backdrop GameObject is toggled active/inactive alongside this panel (and " +
             "pushed to the back of its own parent every time - see Show()) so it only blocks clicks while " +
             "something's actually open. IMPORTANT: it must NOT cover either panel's own toggle button (this " +
             "one's, and ActiveComboPanel.toggleButton) - those need to sit in front of/outside it so pressing " +
             "one while the other's panel is open still reaches the button and switches directly, instead of " +
             "the backdrop swallowing the click and just closing the current panel. It should still cover " +
             "everything else that isn't a way to open these two panels (belt, other buttons, etc.).")]
    public Button clickOutsideBackdrop;

    [Header("Player-Chosen Deletion")]
    [Tooltip("Optional - shown with deletionPromptMessage while ShowForDeletion() is active (clicking any " +
             "card deletes it, instead of the normal read-only browsing) and cleared again once that " +
             "resolves. Lets the player know what clicking a card does right now, since normally clicking a " +
             "deck stats card does nothing at all.")]
    public TMP_Text deletionPromptText;
    public string deletionPromptMessage = "Choose an additive to delete";

    private readonly List<AdditiveCard> _spawned = new List<AdditiveCard>();

    /// <summary>Non-null while ShowForDeletion() is active - set BEFORE Show()/Populate() run so Populate() knows to wire click-to-delete on every spawned card.</summary>
    private System.Action<AdditiveInstance> _deletionCallback;

    public bool IsShown => panel != null && panel.IsShown;

    private void Awake()
    {
        if (clickOutsideBackdrop != null)
            clickOutsideBackdrop.onClick.AddListener(Hide);
    }

    /// <summary>Closes it if already open, opens it otherwise - what Toggle actually means for a UI panel.</summary>
    public void Toggle()
    {
        if (IsShown) Hide();
        else Show();
    }

    /// <summary>
    /// Opens deck stats in a special mode where clicking ANY card immediately deletes
    /// it (invoking onChosen with the deleted instance) and closes the panel - used to
    /// let the player pick which owned additive an effect deletes (see AdditiveData's
    /// DeletePlayerChosenThenSelf and RoundManager's pending-deletion queue), instead
    /// of that pick being made automatically/randomly like the other Delete*ThenSelf
    /// effects. If the panel closes any other way (backdrop click, some other close
    /// button, etc.) before a card is picked, onChosen is invoked with null instead of
    /// being left hanging forever - see Hide().
    /// </summary>
    public void ShowForDeletion(System.Action<AdditiveInstance> onChosen)
    {
        _deletionCallback = onChosen;
        if (deletionPromptText != null) deletionPromptText.text = deletionPromptMessage;
        Show();

        // Show() can bail out early (missing statEntryPrefab/entryContainer/panel - see its
        // own error logs above) without ever actually becoming shown, in which case Hide()'s
        // "closed without picking" fallback never gets a chance to run either. Resolve the
        // pending callback with null right here instead of leaving RoundManager's
        // ResolvePlayerDeletions() coroutine waiting forever on a choice that can now never come.
        if (!IsShown && _deletionCallback != null)
        {
            var cb = _deletionCallback;
            _deletionCallback = null;
            if (deletionPromptText != null) deletionPromptText.text = "";
            cb.Invoke(null);
        }
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
        if (clickOutsideBackdrop != null)
        {
            clickOutsideBackdrop.gameObject.SetActive(true);
            // Push it to the very back of ITS OWN parent every time it activates - a cheap
            // self-correcting safeguard against it accidentally ending up in front of (and
            // therefore swallowing clicks meant for) something under that same parent that
            // needs to stay clickable, most importantly ActiveComboPanel's own toggle button.
            // Without this, pressing that button while THIS panel is open would just close
            // this one instead of actually opening the combo panel, since the click would
            // never reach the button at all.
            clickOutsideBackdrop.transform.SetAsFirstSibling();
        }

        Populate();

        // Force an immediate layout pass rather than waiting for Unity's next
        // automatic rebuild - belt-and-braces in case the container was still
        // settling from the panel's own show animation this same frame.
        LayoutRebuilder.ForceRebuildLayoutImmediate(entryContainer);

        // Now that entryContainer's layout reflects the freshly spawned entries,
        // resize the scrollbar handle to match (and snap scroll position back to
        // the top) - see ScrollbarContentSizer for why this can't just be left to
        // Unity's own layout pass timing.
        scrollbarSizer?.Refresh();
    }

    public void Hide()
    {
        // Guards against being called when this specific panel isn't the one currently
        // open - matters for a shared clickOutsideBackdrop, whose click ends up calling
        // Hide() on both this and ActiveComboPanel regardless of which is actually shown.
        if (!IsShown) return;

        panel.Hide();
        if (clickOutsideBackdrop != null) clickOutsideBackdrop.gameObject.SetActive(false);

        // If a deletion choice was pending and the panel is closing WITHOUT a card having
        // been clicked (a card click clears _deletionCallback itself before calling Hide -
        // see HandleDeletionClick), resolve it as "nothing chosen" instead of leaving
        // RoundManager's ResolvePlayerDeletions() coroutine waiting forever.
        if (_deletionCallback != null)
        {
            var cb = _deletionCallback;
            _deletionCallback = null;
            if (deletionPromptText != null) deletionPromptText.text = "";
            cb.Invoke(null);
        }
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

            if (_deletionCallback != null)
            {
                var captured = instance; // local copy for the closure - `instance` itself is the loop variable
                card.OnCardClicked += _ => HandleDeletionClick(captured);
            }
        }
    }

    /// <summary>Wired to every spawned card's OnCardClicked only while _deletionCallback is set - see ShowForDeletion().</summary>
    private void HandleDeletionClick(AdditiveInstance chosen)
    {
        var cb = _deletionCallback;
        _deletionCallback = null; // clear FIRST so Hide()'s own "closed without picking" fallback below doesn't also fire
        if (deletionPromptText != null) deletionPromptText.text = "";
        Hide();
        cb?.Invoke(chosen);
    }
}