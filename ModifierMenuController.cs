using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Listens for RoundManager's syrup (modifier) offers, spawns a ModifierOfferButton
/// per offer into offerContainer, and shows/hides panel via LerpPanel. Picking one
/// (or calling Skip()) tells RoundManager to continue.
///
/// Offer entries are laid out by a HorizontalLayoutGroup on offerContainer - add
/// that component in the Inspector rather than positioning entries manually here.
///
/// panel.Show() is called BEFORE spawning entries, not after - Unity's layout
/// system doesn't run its automatic rebuild pass on objects that are inactive in
/// the hierarchy, and LerpPanel deactivates its GameObject while hidden. A forced
/// rebuild after spawning covers it either way.
/// </summary>
public class ModifierMenuController : MonoBehaviour
{
    public RoundManager roundManager;
    public LerpPanel panel;
    public ModifierOfferButton offerButtonPrefab;
    [Tooltip("Must have a HorizontalLayoutGroup component - that's what spaces the offer entries evenly.")]
    public RectTransform offerContainer;

    [Header("Other Panels")]
    [Tooltip("Optional - closed automatically whenever the syrup offers open, since they'd otherwise sit open behind/overlapping it.")]
    public DeckStatsPanel deckStatsPanel;
    public ActiveComboPanel activeComboPanel;

    private readonly List<ModifierOfferButton> _spawned = new List<ModifierOfferButton>();

    private void OnEnable()
    {
        if (roundManager != null)
            roundManager.OnModifierOffersReady.AddListener(ShowOffers);
    }

    private void OnDisable()
    {
        if (roundManager != null)
            roundManager.OnModifierOffersReady.RemoveListener(ShowOffers);
    }

    private void ShowOffers(List<ModifierData> offers)
    {
        Debug.Log($"[ModifierMenuController] ShowOffers received {offers.Count} offer(s).");

        if (offerButtonPrefab == null)
        {
            Debug.LogError($"[ModifierMenuController] '{name}' has no offerButtonPrefab assigned - can't spawn syrup offers.", this);
            return;
        }
        if (offerContainer == null)
        {
            Debug.LogError($"[ModifierMenuController] '{name}' has no offerContainer assigned.", this);
            return;
        }
        if (panel == null)
        {
            Debug.LogError($"[ModifierMenuController] '{name}' has no panel (LerpPanel) assigned - can't show the syrup offers.", this);
            return;
        }
        if (offerContainer.GetComponent<HorizontalLayoutGroup>() == null)
        {
            Debug.LogWarning($"[ModifierMenuController] '{offerContainer.name}' has no HorizontalLayoutGroup component - " +
                              "offer entries will all spawn at the same position and stack on top of each other. " +
                              "Add one in the Inspector.", offerContainer);
        }

        // Close any other menus that shouldn't sit open at the same time as the syrup offers.
        deckStatsPanel?.Hide();
        activeComboPanel?.Hide();

        // Activate the panel FIRST so offerContainer is active in the hierarchy before
        // any entries are parented under it - see class comment above.
        panel.Show();

        ClearSpawned();

        foreach (var data in offers)
        {
            var entry = Instantiate(offerButtonPrefab, offerContainer);
            entry.SetData(data);
            entry.OnPicked += Pick;
            _spawned.Add(entry);
        }

        // Force an immediate layout pass rather than waiting for Unity's next automatic
        // rebuild - belt-and-braces in case the container was still settling from the
        // panel's own show animation this same frame.
        LayoutRebuilder.ForceRebuildLayoutImmediate(offerContainer);

        Debug.Log($"[ModifierMenuController] Spawned {_spawned.Count} offer entry(ies).");
    }

    private void Pick(ModifierData chosen)
    {
        panel.Hide();
        ClearSpawned();
        roundManager.OnModifierChosen(chosen);
    }

    /// <summary>Call from a "skip"/"no thanks" button if you want to offer that.</summary>
    public void Skip()
    {
        panel.Hide();
        ClearSpawned();
        roundManager.OnModifierChosen(null);
    }

    private void ClearSpawned()
    {
        // Detach from offerContainer BEFORE destroying - Destroy() is deferred to the end
        // of the frame, but the forced layout rebuild after spawning new offers runs
        // synchronously the same frame, so it would still count these as children (still
        // reserving their space) right up until they actually vanish a moment later.
        // Detaching (SetParent(null)) takes effect immediately, so the layout group only
        // ever sees the real current set.
        foreach (var e in _spawned)
        {
            if (e == null) continue;
            e.transform.SetParent(null);
            Destroy(e.gameObject);
        }
        _spawned.Clear();

        // Extra safety net: same treatment for any untracked leftover children, not just
        // what _spawned was tracking - guarantees a clean slate even if tracking somehow
        // got out of sync (duplicate controller, manual scene edits, etc).
        if (offerContainer != null)
        {
            for (int i = offerContainer.childCount - 1; i >= 0; i--)
            {
                var child = offerContainer.GetChild(i);
                child.SetParent(null);
                Destroy(child.gameObject);
            }
        }
    }
}