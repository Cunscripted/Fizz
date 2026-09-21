using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Listens for RoundManager's shop offers, spawns a clickable AdditiveCard
/// (draggable=false) per offer into offerContainer, and shows/hides panel via
/// LerpPanel. Picking a card (or calling Skip()) tells RoundManager to continue.
///
/// Offer cards are laid out by a HorizontalLayoutGroup on offerContainer -
/// add that component in the Inspector (with whatever spacing/padding/alignment
/// you want) rather than positioning cards manually here. Without it, every
/// spawned card lands at the prefab's own authored anchoredPosition (usually
/// 0,0) and they all stack exactly on top of each other.
///
/// panel.Show() is called BEFORE spawning cards, not after - Unity's layout
/// system doesn't run its automatic rebuild pass on objects that are inactive
/// in the hierarchy, and LerpPanel deactivates its GameObject while hidden. If
/// cards were instantiated first and the panel activated second, the layout
/// group might never get a proper first pass on them. A forced rebuild after
/// spawning covers it either way.
/// </summary>
public class ShopMenuController : MonoBehaviour
{
    public RoundManager roundManager;
    public LerpPanel panel;
    public AdditiveCard offerCardPrefab;
    [Tooltip("Must have a HorizontalLayoutGroup component - that's what spaces the offer cards evenly.")]
    public RectTransform offerContainer;
    [Tooltip("Scale multiplier applied to offer cards so they stand out as the focus of the shop screen. " +
             "Note: HorizontalLayoutGroup allocates space based on each card's unscaled size, not this " +
             "multiplier - if cards look like they're overlapping with a high scale, increase the layout " +
             "group's Spacing to compensate.")]
    public float offerCardScale = 1.3f;

    [Header("Other Panels")]
    [Tooltip("Optional - closed automatically whenever the shop opens, since they'd otherwise sit open behind/overlapping it.")]
    public DeckStatsPanel deckStatsPanel;
    public ActiveComboPanel activeComboPanel;

    [Header("Skip")]
    [Tooltip("Optional - auto-wired to Skip() in Awake(), no manual Inspector event wiring needed. Skipping " +
             "raises RoundManager.SkipStreak, which skews the NEXT shop/modifier offers further toward rarer " +
             "tiers - the payoff for passing up these offers is better odds later. The streak resets the " +
             "moment anything is actually picked in either menu.")]
    public Button skipButton;
    [Tooltip("Optional - updated with roundManager.SkipStreak every time offers are shown, so the player can " +
             "see the streak they're building (or about to cash in). Left untouched if unassigned.")]
    public TMP_Text skipStreakText;

    private readonly List<AdditiveCard> _spawned = new List<AdditiveCard>();

    private void Awake()
    {
        if (skipButton != null)
            skipButton.onClick.AddListener(Skip);
    }

    private void OnEnable()
    {
        if (roundManager != null)
            roundManager.OnShopOffersReady.AddListener(ShowOffers);
    }

    private void OnDisable()
    {
        if (roundManager != null)
            roundManager.OnShopOffersReady.RemoveListener(ShowOffers);
    }

    private void ShowOffers(List<AdditiveData> offers)
    {
        Debug.Log($"[ShopMenuController] ShowOffers received {offers.Count} offer(s).");

        if (offerCardPrefab == null)
        {
            Debug.LogError($"[ShopMenuController] '{name}' has no offerCardPrefab assigned - can't spawn shop offers.", this);
            return;
        }
        if (offerContainer == null)
        {
            Debug.LogError($"[ShopMenuController] '{name}' has no offerContainer assigned - offer cards would spawn with no parent.", this);
            return;
        }
        if (panel == null)
        {
            Debug.LogError($"[ShopMenuController] '{name}' has no panel (LerpPanel) assigned - can't show the shop.", this);
            return;
        }
        if (offerContainer.GetComponent<HorizontalLayoutGroup>() == null)
        {
            Debug.LogWarning($"[ShopMenuController] '{offerContainer.name}' has no HorizontalLayoutGroup component - " +
                              "offer cards will all spawn at the same position and stack on top of each other. " +
                              "Add one in the Inspector.", offerContainer);
        }

        // Close any other menus that shouldn't sit open at the same time as the shop.
        deckStatsPanel?.Hide();
        activeComboPanel?.Hide();

        // Activate the panel FIRST so offerContainer is active in the hierarchy before
        // any cards are parented under it - see class comment above.
        panel.Show();

        if (skipStreakText != null)
        {
            int streak = roundManager != null ? roundManager.SkipStreak : 0;
            skipStreakText.text = streak > 0 ? $"Skip streak: {streak} (better odds!)" : "";
        }

        ClearSpawned();

        foreach (var data in offers)
        {
            var card = Instantiate(offerCardPrefab, offerContainer);
            card.draggable = false;
            card.instance = new AdditiveInstance(data);
            card.Rect.localScale = Vector3.one * offerCardScale;

            var captured = data; // local copy for the closure below
            card.OnCardClicked += _ => Pick(captured);

            _spawned.Add(card);
        }

        // Force an immediate layout pass rather than waiting for Unity's next automatic
        // rebuild - belt-and-braces in case the container was still settling from the
        // panel's own show animation this same frame.
        LayoutRebuilder.ForceRebuildLayoutImmediate(offerContainer);

        Debug.Log($"[ShopMenuController] Spawned {_spawned.Count} offer card(s).");
    }

    private void Pick(AdditiveData chosen)
    {
        // RoundManager first, panel.Hide() LAST - see Skip() below for why the order matters.
        roundManager.OnShopPickChosen(chosen);
        ClearSpawned();
        panel.Hide();
    }

    /// <summary>Call from a "skip"/"no thanks" button if you want to offer that.</summary>
    public void Skip()
    {
        // Tell RoundManager the skip happened FIRST, then clear this panel's offer cards
        // and Hide() LAST - once the skip is fully processed, not before. RoundManager.
        // OnShopPickChosen(null) can, depending on how the next round resolves, end up
        // synchronously re-invoking OnShopOffersReady (ShowOffers() below) before this
        // method returns - if Hide() ran first, that re-entrant Show() would cancel the
        // close animation before it ever rendered a frame, so the panel would look like
        // it never closed even though a skip (and a round) genuinely happened. Calling
        // Hide() last guarantees it's the final word on this panel's state no matter what
        // OnShopPickChosen triggers along the way.
        roundManager.OnShopPickChosen(null);
        ClearSpawned();
        panel.Hide();
    }

    private void ClearSpawned()
    {
        foreach (var c in _spawned)
            if (c != null) Destroy(c.gameObject);
        _spawned.Clear();

        // Extra safety net: destroy ANY leftover children of offerContainer too, not just
        // what _spawned was tracking - guarantees a clean slate even if tracking somehow
        // got out of sync, which is the only way previous offers could survive the clear
        // above and pile up off-screen.
        if (offerContainer != null)
        {
            for (int i = offerContainer.childCount - 1; i >= 0; i--)
                Destroy(offerContainer.GetChild(i).gameObject);
        }
    }
}