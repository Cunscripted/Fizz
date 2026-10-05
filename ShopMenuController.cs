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
    [Tooltip("Auto-wired to Skip(). Skipping closes the shop, starts the next round, and PERMANENTLY improves the " +
             "shop for the rest of the run: better rarity odds every skip, and an extra offer slot every few skips " +
             "(ShopManager.skipsPerExtraOffer).")]
    public Button skipButton;
    [Tooltip("Optional - shows total skips and how many more until the next extra offer slot (see Skip Info Format).")]
    public TMP_Text skipStreakText;
    [Tooltip("If no Skip Button is assigned above, create a simple one at runtime (bottom of the shop panel) so " +
             "the shop can always be skipped. Turn off if you deliberately don't want a skip option.")]
    public bool autoCreateSkipButton = true;
    public string skipButtonLabel = "Skip";
    [Tooltip("{0} = total shop skips this run, {1} = skips left until the next extra offer slot, {2} = current offer slots.")]
    public string skipInfoFormat = "Skips: {0}  (+1 slot in {1})";

    [Header("Reroll")]
    [Tooltip("Rerolls the offers for a permanent bump to future score requirements (RoundManager.rerollRequirementGrowth). " +
             "Auto-wired. If empty (and Auto Create is on), a basic one is made at runtime.")]
    public Button rerollButton;
    public bool autoCreateRerollButton = true;
    [Tooltip("{0} = % the requirement goes up per reroll.")]
    public string rerollButtonLabel = "Reroll (+{0:0}% score)";
    [Tooltip("Optional - shows what the next round will require, updated after every reroll.")]
    public TMP_Text nextRequirementText;
    [Tooltip("{0} = next round's score requirement (already formatted, e.g. 12,345 or 1.23e9).")]
    public string nextRequirementFormat = "Next round: {0}";

    /// <summary>How an always-sell slot picks which item from its pool to offer.</summary>
    public enum RotationMode
    {
        CycleEachVisit,  // item 1 this shop, item 2 next shop, ... then back to item 1
        RandomEachVisit, // a random item from the pool every time the shop opens
        TimedWhileOpen   // keeps rotating through the pool every few seconds while the shop is open
    }

    /// <summary>One always-sell button/card. A pool with a single item always sells that item.</summary>
    [System.Serializable]
    public class AlwaysSellSlot
    {
        public string slotName = "Always Sell";
        public bool enabled = true;
        [Tooltip("Items this slot offers. One item = always that item. Several = rotates through them (see Rotation).")]
        public List<AdditiveData> pool = new List<AdditiveData>();
        public RotationMode rotation = RotationMode.CycleEachVisit;
        [Tooltip("Timed While Open only: seconds each item stays up before switching to the next.")]
        public float secondsPerItem = 3f;
        [Tooltip("Random Each Visit only: never offer the same item two shops in a row (if the pool has 2+ items).")]
        public bool avoidRepeats = true;

        [Header("Display - Option A (card)")]
        [Tooltip("A card for this slot's item is spawned here each time the shop opens (hover details + click to take). " +
                 "Use offerContainer to put it at the end of the offer row, or its own slot object.")]
        public RectTransform container;

        [Header("Display - Option B (button, used if Container is empty)")]
        [Tooltip("Your own Button - auto-wired. If empty (and Auto Create is on), a basic one is made at runtime.")]
        public Button button;
        public TMP_Text label;
        public Image icon;
        [Tooltip("{0} = the current item's name.")]
        public string labelFormat = "Take {0}";
        public bool autoCreateButton = true;

        // Runtime
        [System.NonSerialized] public int index = -1;
        [System.NonSerialized] public AdditiveData current;
        [System.NonSerialized] public AdditiveCard card;
        [System.NonSerialized] public Coroutine timer;
        [System.NonSerialized] public bool buttonWired;
    }

    [Header("Always-Sell Slots")]
    [Tooltip("Master switch for every always-sell slot below.")]
    public bool alwaysSellEnabled = true;
    [Tooltip("Each slot is its own always-available offer (card or button), with a pool of items it can rotate through.")]
    public List<AlwaysSellSlot> alwaysSellSlots = new List<AlwaysSellSlot>();

    // ---- Legacy single always-sell fields (pre-slots). Converted into the first slot automatically. ----
    [HideInInspector] public AdditiveData alwaysSellItem;
    [HideInInspector] public RectTransform alwaysSellContainer;
    [HideInInspector] public Button alwaysSellButton;
    [HideInInspector] public TMP_Text alwaysSellLabel;
    [HideInInspector] public Image alwaysSellIcon;
    [HideInInspector] public string alwaysSellLabelFormat = "Take {0}";
    [HideInInspector] public bool autoCreateAlwaysSellButton = true;

    private readonly List<AdditiveCard> _spawned = new List<AdditiveCard>();

    private void Awake()
    {
        if (skipButton != null)
            skipButton.onClick.AddListener(Skip);
        if (rerollButton != null)
            rerollButton.onClick.AddListener(Reroll);
        MigrateLegacyAlwaysSell();
    }

#if UNITY_EDITOR
    private void OnValidate() => MigrateLegacyAlwaysSell(); // so the converted slot shows up in the Inspector too
#endif

    /// <summary>Turns the old single always-sell setup into the first slot, once.</summary>
    private void MigrateLegacyAlwaysSell()
    {
        if (alwaysSellItem == null) return;
        if (alwaysSellSlots == null) alwaysSellSlots = new List<AlwaysSellSlot>();
        alwaysSellSlots.Insert(0, new AlwaysSellSlot
        {
            slotName = "Always Sell",
            pool = new List<AdditiveData> { alwaysSellItem },
            container = alwaysSellContainer,
            button = alwaysSellButton,
            label = alwaysSellLabel,
            icon = alwaysSellIcon,
            labelFormat = string.IsNullOrEmpty(alwaysSellLabelFormat) ? "Take {0}" : alwaysSellLabelFormat,
            autoCreateButton = autoCreateAlwaysSellButton
        });
        alwaysSellItem = null;
        alwaysSellContainer = null;
        alwaysSellButton = null;
        alwaysSellLabel = null;
        alwaysSellIcon = null;
    }

    /// <summary>
    /// Sets the FIRST slot to always sell just this item (null clears it) - kept for older code.
    /// Takes effect the next time the shop opens.
    /// </summary>
    public void SetAlwaysSellItem(AdditiveData item) => SetSlotPool(0, item != null ? new List<AdditiveData> { item } : new List<AdditiveData>());

    /// <summary>Replaces a slot's pool from code (creating slots up to that index if needed). Takes effect the next time the shop opens.</summary>
    public void SetSlotPool(int slotIndex, List<AdditiveData> pool)
    {
        while (alwaysSellSlots.Count <= slotIndex) alwaysSellSlots.Add(new AlwaysSellSlot());
        alwaysSellSlots[slotIndex].pool = pool ?? new List<AdditiveData>();
        alwaysSellSlots[slotIndex].index = -1;
    }

    private void OnEnable()
    {
        if (roundManager != null)
        {
            roundManager.OnShopOffersReady.AddListener(ShowOffers);
            roundManager.ShopRerolled += OnRerolled;
        }
    }

    private void OnDisable()
    {
        if (roundManager != null)
        {
            roundManager.OnShopOffersReady.RemoveListener(ShowOffers);
            roundManager.ShopRerolled -= OnRerolled;
        }
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

        EnsureSkipButton();
        EnsureRerollButton();
        RefreshInfo();

        ClearSpawned();
        SpawnOffers(offers);
        RefreshAlwaysSell();

        // Force an immediate layout pass rather than waiting for Unity's next automatic
        // rebuild - belt-and-braces in case the container was still settling from the
        // panel's own show animation this same frame.
        LayoutRebuilder.ForceRebuildLayoutImmediate(offerContainer);
        foreach (var slot in alwaysSellSlots)
            if (slot?.container != null && slot.container != offerContainer)
                LayoutRebuilder.ForceRebuildLayoutImmediate(slot.container);

        Debug.Log($"[ShopMenuController] Spawned {_spawned.Count} offer card(s).");
    }

    /// <summary>Reroll: swap only the random offer cards - always-sell slots keep what they're showing.</summary>
    private void OnRerolled(List<AdditiveData> offers)
    {
        ClearRandomOffers();
        SpawnOffers(offers);

        // Keep always-sell cards that share the offer row at the end of it.
        foreach (var slot in alwaysSellSlots)
            if (slot?.card != null && slot.container == offerContainer) slot.card.transform.SetAsLastSibling();

        LayoutRebuilder.ForceRebuildLayoutImmediate(offerContainer);
        RefreshInfo();
    }

    public void Reroll()
    {
        if (roundManager == null) return;
        if (!roundManager.RerollShop())
            Debug.Log("[ShopMenuController] Can't reroll right now (not shopping, or out of rerolls for this shop).", this);
    }

    /// <summary>Updates the skip progress text, reroll button label/state and next-round requirement.</summary>
    private void RefreshInfo()
    {
        if (roundManager == null) return;

        if (skipStreakText != null && roundManager.shopManager != null)
        {
            var sm = roundManager.shopManager;
            int skips = roundManager.ShopSkips;
            int slots = sm.OfferCountFor(skips);
            bool atMax = slots >= sm.maxOfferCount || sm.skipsPerExtraOffer <= 0;
            int left = atMax ? 0 : sm.skipsPerExtraOffer - (skips % sm.skipsPerExtraOffer);
            skipStreakText.text = atMax
                ? $"Skips: {skips}  (max offer slots)"
                : string.Format(skipInfoFormat, skips, left, slots);
        }

        if (rerollButton != null)
        {
            rerollButton.interactable = roundManager.CanReroll;
            var label = rerollButton.GetComponentInChildren<TMP_Text>();
            if (label != null)
                label.text = string.Format(rerollButtonLabel, (roundManager.rerollRequirementGrowth - 1f) * 100f);
        }

        if (nextRequirementText != null)
            nextRequirementText.text = string.Format(nextRequirementFormat, ScoreFormat.Big(roundManager.NextRoundRequirement));
    }

    private void SpawnOffers(List<AdditiveData> offers)
    {
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
    }

    private void PickAlwaysSell(AlwaysSellSlot slot)
    {
        if (!alwaysSellEnabled || slot == null || !slot.enabled || slot.current == null) return;
        Pick(slot.current); // whatever the slot is showing right now (matters for Timed While Open)
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

    /// <summary>Removes just the random offer cards (used by reroll).</summary>
    private void ClearRandomOffers()
    {
        foreach (var c in _spawned)
        {
            if (c == null) continue;
            c.transform.SetParent(null); // detach now so the same-frame layout rebuild doesn't count it
            Destroy(c.gameObject);
        }
        _spawned.Clear();
    }

    private void ClearSpawned()
    {
        foreach (var c in _spawned)
            if (c != null) Destroy(c.gameObject);
        _spawned.Clear();

        foreach (var slot in alwaysSellSlots)
        {
            if (slot == null) continue;
            if (slot.timer != null) { StopCoroutine(slot.timer); slot.timer = null; }
            if (slot.card != null)
            {
                slot.card.transform.SetParent(null); // detach now so a same-frame layout rebuild doesn't count it
                Destroy(slot.card.gameObject);
            }
            slot.card = null;
        }

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

    // ---------- Always-sell slot ----------

    /// <summary>Picks each slot's item for this shop visit and shows it as a card or button.</summary>
    private void RefreshAlwaysSell()
    {
        for (int i = 0; i < alwaysSellSlots.Count; i++)
        {
            var slot = alwaysSellSlots[i];
            if (slot == null) continue;

            var usable = slot.pool != null ? slot.pool.FindAll(a => a != null) : new List<AdditiveData>();
            bool active = alwaysSellEnabled && slot.enabled && usable.Count > 0;
            slot.current = active ? ChooseItem(slot, usable) : null;

            ShowSlot(slot, i, active);

            if (active && slot.rotation == RotationMode.TimedWhileOpen && usable.Count > 1)
                slot.timer = StartCoroutine(RotateWhileOpen(slot));
        }
    }

    private AdditiveData ChooseItem(AlwaysSellSlot slot, List<AdditiveData> usable)
    {
        switch (slot.rotation)
        {
            case RotationMode.RandomEachVisit:
            {
                int pick = Random.Range(0, usable.Count);
                if (slot.avoidRepeats && usable.Count > 1 && pick == slot.index)
                    pick = (pick + 1 + Random.Range(0, usable.Count - 1)) % usable.Count;
                slot.index = pick;
                break;
            }
            default: // CycleEachVisit, and the starting item for TimedWhileOpen
                slot.index = (slot.index + 1) % usable.Count;
                break;
        }
        return usable[Mathf.Clamp(slot.index, 0, usable.Count - 1)];
    }

    private System.Collections.IEnumerator RotateWhileOpen(AlwaysSellSlot slot)
    {
        while (true)
        {
            float wait = Mathf.Max(0.25f, slot.secondsPerItem);
            for (float t = 0f; t < wait; t += Time.unscaledDeltaTime) yield return null;

            var usable = slot.pool.FindAll(a => a != null);
            if (usable.Count == 0) yield break;
            slot.index = (slot.index + 1) % usable.Count;
            slot.current = usable[slot.index];
            UpdateSlotVisuals(slot);
        }
    }

    private void ShowSlot(AlwaysSellSlot slot, int slotIndex, bool active)
    {
        // Option A: a real offer card.
        if (slot.container != null)
        {
            if (slot.button != null) slot.button.gameObject.SetActive(false);
            if (!active) return;

            var card = Instantiate(offerCardPrefab, slot.container);
            card.draggable = false;
            card.Rect.localScale = Vector3.one * offerCardScale;
            card.OnCardClicked += _ => PickAlwaysSell(slot);
            slot.card = card;
            UpdateSlotVisuals(slot);
            return;
        }

        // Option B: a button (assigned, or created on the fly - stacked upward so several don't overlap).
        if (slot.button == null && active && slot.autoCreateButton)
        {
            slot.button = CreateRuntimeButton($"AlwaysSellButton_{slotIndex} (auto)", "", new Vector2(150f, 50f + slotIndex * 74f));
            slot.label = slot.button.GetComponentInChildren<TMP_Text>();
        }
        if (slot.button == null) return;

        if (!slot.buttonWired)
        {
            slot.button.onClick.AddListener(() => PickAlwaysSell(slot));
            slot.buttonWired = true;
        }

        slot.button.gameObject.SetActive(active);
        if (active) UpdateSlotVisuals(slot);
    }

    /// <summary>Points the slot's card/button at its current item.</summary>
    private void UpdateSlotVisuals(AlwaysSellSlot slot)
    {
        if (slot.current == null) return;

        if (slot.card != null)
            slot.card.instance = new AdditiveInstance(slot.current); // also refreshes art/rarity/popularity cue

        if (slot.label != null)
            slot.label.text = string.Format(slot.labelFormat, slot.current.additiveName);
        if (slot.icon != null)
        {
            slot.icon.enabled = slot.current.icon != null;
            if (slot.current.icon != null) slot.icon.sprite = slot.current.icon;
        }
    }

    // ---------- Skip button ----------

    /// <summary>Creates a basic Reroll button (above the Skip button) if none was assigned.</summary>
    private void EnsureRerollButton()
    {
        if (rerollButton != null || !autoCreateRerollButton) return;
        rerollButton = CreateRuntimeButton("RerollButton (auto)", "Reroll", new Vector2(-150f, 124f));
        rerollButton.onClick.AddListener(Reroll);
    }

    /// <summary>Creates a basic Skip button on the shop panel if none was assigned (and autoCreateSkipButton is on).</summary>
    private void EnsureSkipButton()
    {
        if (skipButton != null || !autoCreateSkipButton) return;
        skipButton = CreateRuntimeButton("SkipButton (auto)", skipButtonLabel, new Vector2(-150f, 50f));
        skipButton.onClick.AddListener(Skip);
    }

    /// <summary>
    /// Minimal runtime button (dark box + TMP label) anchored to the bottom-center of the shop
    /// panel. Only a fallback so the feature works with zero scene setup - for a styled button,
    /// build one in the scene and drag it into the matching field instead.
    /// </summary>
    private Button CreateRuntimeButton(string objectName, string label, Vector2 anchoredPosition)
    {
        var go = new GameObject(objectName, typeof(RectTransform), typeof(Image), typeof(Button));
        var rect = (RectTransform)go.transform;
        rect.SetParent(panel.transform, false);
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0f);
        rect.pivot = new Vector2(0.5f, 0f);
        rect.sizeDelta = new Vector2(260f, 64f);
        rect.anchoredPosition = anchoredPosition;

        var image = go.GetComponent<Image>();
        image.color = new Color(0.12f, 0.12f, 0.16f, 0.9f);
        var button = go.GetComponent<Button>();
        button.targetGraphic = image;

        var textGo = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
        var textRect = (RectTransform)textGo.transform;
        textRect.SetParent(rect, false);
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(8f, 4f);
        textRect.offsetMax = new Vector2(-8f, -4f);
        var text = textGo.GetComponent<TextMeshProUGUI>();
        text.text = label;
        text.alignment = TextAlignmentOptions.Center;
        text.enableAutoSizing = true;
        text.fontSizeMin = 12f;
        text.fontSizeMax = 32f;
        text.color = Color.white;
        text.raycastTarget = false;

        return button;
    }
}