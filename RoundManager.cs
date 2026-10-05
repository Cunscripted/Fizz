using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

/// <summary>
/// Drives the full round loop:
///   Spawn 10 additives on the belt -> player builds a soda in the bottle ->
///   Brew (score) -> up to 3 attempts, each attempt's total ADDING to a cumulative
///   round score, until that cumulative total meets the round's requirement ->
///   on success, offer 4 additives from the shop (odds skewed by how efficiently
///   the round was won) -> every 5th round, offer a permanent modifier first ->
///   next round.
///
/// Wire the UnityEvents to your UI (score requirement label, attempt counter,
/// game-over screen, shop panel, modifier panel) - this class only owns state
/// and timing, not presentation.
/// </summary>
public class RoundManager : MonoBehaviour
{
    public enum State { Spawning, Playing, Scoring, RoundWon, Shopping, ModifierChoice, GameOver }

    [Header("Scene References")]
    public ConveyorBelt belt;
    public SodaBottle bottle;
    public SodaScoringManager scoringManager;
    public ShopManager shopManager;
    public ModifierManager modifierManager;
    public AdditiveCard cardPrefab;
    [Tooltip("Used by DeletePlayerChosenThenSelf additives to let the player pick which owned additive gets " +
             "deleted, via DeckStatsPanel's special deletion mode (ShowForDeletion). Optional, but any such " +
             "additive's player-choice request is silently skipped (logged as a warning) without it.")]
    public DeckStatsPanel deckStatsPanel;
    [Tooltip("Optional - flavor popularity. Overused flavors pay out less, neglected ones pay out more. Falls back " +
             "to PopularityManager.Instance if left empty; with neither, every additive always pays out 1x.")]
    public PopularityManager popularity;

    [Header("Brew Button")]
    [Tooltip("Drag your 'Brew'/'Score' Button here - it's wired to OnBrewPressed() automatically at " +
             "runtime, no need to manually add an OnClick() entry on the Button itself.")]
    public Button brewButton;

    [Header("Score FX")]
    [Tooltip("Optional - plays floating +N/xN text and escalating-pitch audio for each scoring step. " +
             "If unset, scoring resolves instantly with no reveal animation.")]
    public ScoreFXPlayer scoreFX;
    [Tooltip("Optional - shows a popup when a syrup is applied (and its added/removed additive, if any). " +
             "Syrup selection has no other visual feedback otherwise.")]
    public SyrupApplyPopup syrupFX;

    [Header("First-Time Intro")]
    [Tooltip("Optional. If assigned and this is the player's first-ever load of the gameplay scene, " +
             "BeginRound() is held off here in Start() - GameplayIntroController plays its video first " +
             "and calls BeginRound() itself once that finishes (or is skipped). On every later load, " +
             "or if left unassigned, the round begins immediately as before.")]
    public GameplayIntroController introController;

    [Header("Starting Collection")]
    [Tooltip("Templates the player starts the run owning, used when no Starting Deck Set is active (see below). " +
             "Grows via shop picks.")]
    public List<AdditiveData> startingAdditives = new List<AdditiveData>();
    public int cardsPerRound = 10;

    /// <summary>A named set of additives the player can start a run with.</summary>
    [System.Serializable]
    public class StartingDeckSet
    {
        public string setName = "New Deck";
        [TextArea] public string description;
        [Tooltip("Additives the run starts with. List one more than once to start with several copies.")]
        public List<AdditiveData> additives = new List<AdditiveData>();
    }

    public enum StartingDeckChoice
    {
        UseStartingAdditivesList, // the plain Starting Additives list above
        UseActiveSet,             // the set picked in Active Set Index
        RandomSet                 // a random set from Starting Deck Sets every run
    }

    [Header("Starting Deck Sets")]
    [Tooltip("Named starting decks. Add as many as you like, then choose which one a run uses below.")]
    public List<StartingDeckSet> startingDeckSets = new List<StartingDeckSet>();
    [Tooltip("Which starting deck a run uses.")]
    public StartingDeckChoice startingDeckChoice = StartingDeckChoice.UseStartingAdditivesList;
    [Tooltip("Index into Starting Deck Sets, used when Starting Deck Choice is Use Active Set.")]
    public int activeSetIndex = 0;

    /// <summary>
    /// Set this (e.g. from a main-menu deck picker, before loading the gameplay scene) to override the
    /// Inspector choice for the NEXT run only: the set's name, matched case-insensitively. Cleared once used.
    /// </summary>
    public static string NextRunDeckName;

    /// <summary>The set the current run started with (null if it used the plain Starting Additives list).</summary>
    public StartingDeckSet CurrentStartingSet { get; private set; }

    [Header("Round Difficulty")]
    public int baseScoreRequirement = 100;
    [Tooltip("Requirement multiplies by this each round, e.g. 1.25 = +25% per round.")]
    public float difficultyGrowth = 1.25f;
    public int maxAttempts = 3;
    public int modifierEveryNRounds = 5;

    /// <summary>Base maxAttempts plus any permanent bonus granted by AddAttempt syrups.</summary>
    public int EffectiveMaxAttempts => maxAttempts + _bonusAttempts;
    private int _bonusAttempts = 0;

    /// <summary>Base cardsPerRound plus any permanent bonus granted by AddBeltSize syrups.</summary>
    public int EffectiveCardsPerRound => cardsPerRound + _bonusBeltSize;
    private int _bonusBeltSize = 0;

    [Header("Events (wire to UI)")]
    public UnityEvent<int> OnRoundStarted;                 // round number
    public UnityEvent<int> OnScoreRequirementSet;           // required score
    public UnityEvent<float, int> OnAttemptScored;          // cumulative round score so far, attempts remaining
    public UnityEvent OnRoundSucceeded;
    public UnityEvent OnGameOver;
    public UnityEvent<List<AdditiveData>> OnShopOffersReady;
    public UnityEvent<List<ModifierData>> OnModifierOffersReady;

    public State CurrentState { get; private set; }
    public int RoundNumber { get; private set; } = 1;
    public int AttemptsUsed { get; private set; } = 0;
    /// <summary>The round's score requirement as a double (can go far past 2.1 billion).</summary>
    public double CurrentRequirementValue { get; private set; }

    /// <summary>Old int version, kept for anything that still reads it - capped at int.MaxValue instead of wrapping negative.</summary>
    public int CurrentRequirement => ScoreFormat.ClampToInt(CurrentRequirementValue);

    /// <summary>Sum of every attempt's total so far this round - what's actually checked against CurrentRequirement.</summary>
    public double CumulativeScore { get; private set; }

    /// <summary>Everything the player currently owns, for a deck-stats/collection view.</summary>
    public IReadOnlyList<AdditiveInstance> OwnedAdditives => _ownedAdditives;

    /// <summary>
    /// How many shop/modifier offers in a row the player has skipped WITHOUT picking
    /// anything, shared across both menus - fed into ShopManager/ModifierManager's
    /// weighted-by-rarity offer generation to skew subsequent offers toward rarer
    /// tiers the longer this streak runs. Resets to 0 the moment a pick is actually
    /// made in either menu (see OnShopPickChosen/OnModifierChosen) - the payoff for
    /// banking skips.
    /// </summary>
    public int SkipStreak => _skipStreak;
    private int _skipStreak = 0;

    [Header("Shop Skip / Reroll")]
    [Tooltip("Each reroll permanently multiplies every FUTURE round's score requirement by this - a smaller bump " +
             "than a whole round (Difficulty Growth). E.g. 1.08 = +8% per reroll.")]
    public float rerollRequirementGrowth = 1.08f;
    [Tooltip("Max rerolls per shop visit. 0 = unlimited.")]
    public int maxRerollsPerShop = 0;

    /// <summary>
    /// Total shops skipped this run - never resets. Permanently raises shop rarity odds, and every
    /// ShopManager.skipsPerExtraOffer skips adds another offer slot.
    /// </summary>
    public int ShopSkips { get; private set; }

    /// <summary>
    /// Player-chosen card deletions skipped this run (the Skip button on DeckStatsPanel's deletion
    /// screen) - never resets. Each one permanently raises shop rarity odds slightly
    /// (ShopManager.deletionSkipSkewPerStack).
    /// </summary>
    public int DeletionSkips { get; private set; }

    /// <summary>Total rerolls this run.</summary>
    public int ShopRerolls { get; private set; }

    /// <summary>Rerolls used in the current shop visit.</summary>
    public int RerollsThisShop { get; private set; }

    /// <summary>Permanent multiplier on score requirements from rerolls (1 = none yet).</summary>
    public float RerollRequirementMultiplier { get; private set; } = 1f;

    /// <summary>What the NEXT round will require, with rerolls so far included.</summary>
    public double NextRoundRequirement => RequirementForRound(RoundNumber + 1);

    public bool CanReroll => CurrentState == State.Shopping && (maxRerollsPerShop <= 0 || RerollsThisShop < maxRerollsPerShop);

    /// <summary>Fires with the new offers when the shop is rerolled (ShopMenuController swaps its cards).</summary>
    public event System.Action<List<AdditiveData>> ShopRerolled;

    private double RequirementForRound(int round) =>
        System.Math.Round(baseScoreRequirement * System.Math.Pow(difficultyGrowth, round - 1) * RerollRequirementMultiplier);

    private readonly List<AdditiveInstance> _ownedAdditives = new List<AdditiveInstance>();

    /// <summary>
    /// DeletePlayerChosenThenSelf additives queue themselves up here (via
    /// ScoringContext.requestPlayerDeletion) during a scoring pass; ResolvePlayerDeletions()
    /// works through them one at a time right after that attempt's score is booked.
    /// </summary>
    private readonly List<AdditiveInstance> _pendingPlayerDeletions = new List<AdditiveInstance>();

    private void Start()
    {
        if (popularity == null) popularity = PopularityManager.Instance;

        foreach (var template in GetStartingAdditives())
            if (template != null) _ownedAdditives.Add(new AdditiveInstance(template));

        if (modifierManager != null)
        {
            modifierManager.OnAttemptBonusGranted += amount => _bonusAttempts += amount;
            modifierManager.OnBeltSizeBonusGranted += amount => _bonusBeltSize += amount;
            modifierManager.OnCupCapacityBonusGranted += amount => bottle?.AddCapacity(amount);
        }

        if (brewButton != null)
            brewButton.onClick.AddListener(OnBrewPressed);

        // If a first-time intro video is about to play, hold off - GameplayIntroController
        // calls BeginRound() itself once the video ends or gets skipped. WillPlayIntro is
        // decided in its Awake(), which always runs before this Start(), so this is safe to
        // read regardless of script execution order between the two components.
        if (introController == null || !introController.WillPlayIntro)
            BeginRound();
    }

    private void OnDestroy()
    {
        if (brewButton != null)
            brewButton.onClick.RemoveListener(OnBrewPressed);
    }

    // ---------- Starting deck ----------

    /// <summary>
    /// Works out which additives this run starts with: a set requested via NextRunDeckName wins,
    /// otherwise Starting Deck Choice decides. Falls back to the plain Starting Additives list if the
    /// chosen set doesn't exist or is empty.
    /// </summary>
    public List<AdditiveData> GetStartingAdditives()
    {
        CurrentStartingSet = PickStartingSet();
        if (CurrentStartingSet != null)
        {
            Debug.Log($"[RoundManager] Starting deck: '{CurrentStartingSet.setName}' ({CurrentStartingSet.additives.Count} additives).", this);
            return CurrentStartingSet.additives;
        }
        return startingAdditives;
    }

    private StartingDeckSet PickStartingSet()
    {
        var usable = startingDeckSets.FindAll(s => s != null && s.additives != null && s.additives.Exists(a => a != null));

        if (!string.IsNullOrEmpty(NextRunDeckName))
        {
            string wanted = NextRunDeckName;
            NextRunDeckName = null; // one run only
            var named = usable.Find(s => string.Equals(s.setName, wanted, System.StringComparison.OrdinalIgnoreCase));
            if (named != null) return named;
            Debug.LogWarning($"[RoundManager] No starting deck set named '{wanted}' (or it's empty) - using the Inspector choice instead.", this);
        }

        switch (startingDeckChoice)
        {
            case StartingDeckChoice.UseActiveSet:
                if (activeSetIndex >= 0 && activeSetIndex < startingDeckSets.Count && usable.Contains(startingDeckSets[activeSetIndex]))
                    return startingDeckSets[activeSetIndex];
                Debug.LogWarning($"[RoundManager] Active Set Index {activeSetIndex} isn't a valid, non-empty set - using Starting Additives instead.", this);
                return null;
            case StartingDeckChoice.RandomSet:
                if (usable.Count > 0) return usable[Random.Range(0, usable.Count)];
                Debug.LogWarning("[RoundManager] Random Set chosen but no non-empty Starting Deck Sets exist - using Starting Additives instead.", this);
                return null;
            default:
                return null;
        }
    }

    /// <summary>Every additive in any starting source (plain list + all sets) - for editor tools.</summary>
    public IEnumerable<AdditiveData> AllStartingAdditives()
    {
        foreach (var a in startingAdditives) yield return a;
        foreach (var set in startingDeckSets)
            if (set?.additives != null)
                foreach (var a in set.additives) yield return a;
    }

    // ---------- Round start ----------

    public void BeginRound()
    {
        Debug.Log($"[RoundManager] BeginRound() - round {RoundNumber}.");

        CurrentState = State.Spawning;
        AttemptsUsed = 0;
        CumulativeScore = 0.0;
        CurrentRequirementValue = RequirementForRound(RoundNumber);

        SpawnHand();

        OnRoundStarted?.Invoke(RoundNumber);
        OnScoreRequirementSet?.Invoke(CurrentRequirement); // int event: capped, never negative
        scoringManager.SetRequiredScoreDisplay(CurrentRequirementValue);
        scoringManager.SetCurrentScoreDisplay(0.0);
        CurrentState = State.Playing;
    }

    /// <summary>
    /// Destroys whatever's currently on the belt and draws a brand new random hand
    /// from the owned collection. Called at the start of every round, AND after
    /// every brew attempt (success or failed-with-attempts-remaining) - so the
    /// belt is never the same 10 cards twice in a row.
    /// </summary>
    private void SpawnHand()
    {
        belt.ClearAllCards();

        var hand = DrawHand(EffectiveCardsPerRound);
        foreach (var instance in hand)
        {
            var card = Instantiate(cardPrefab, belt.transform);
            card.instance = instance;
        }
    }

    private List<AdditiveInstance> DrawHand(int count)
    {
        var shuffled = new List<AdditiveInstance>(_ownedAdditives);
        for (int i = shuffled.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (shuffled[i], shuffled[j]) = (shuffled[j], shuffled[i]);
        }
        return shuffled.GetRange(0, Mathf.Min(count, shuffled.Count));
    }

    // ---------- Brewing / scoring ----------

    /// <summary>Hook this up to your "Brew"/"Score" button.</summary>
    public void OnBrewPressed()
    {
        if (CurrentState != State.Playing) return;
        StartCoroutine(BrewRoutine());
    }

    private IEnumerator BrewRoutine()
    {
        CurrentState = State.Scoring;

        // Snapshot the cup's AND belt's card views so score events can be anchored to the
        // specific card that produced them (for the floating text spawn point / jump).
        var cupCards = bottle.AcceptedCards;
        var beltCards = belt.Cards;
        RectTransform ResolveCardRect(AdditiveInstance inst)
        {
            foreach (var c in cupCards)
                if (c.instance == inst) return c.Rect;
            foreach (var c in beltCards)
                if (c.instance == inst) return c.Rect;
            return null;
        }

        var beltInstances = new List<AdditiveInstance>();
        foreach (var c in beltCards)
            if (c.instance != null) beltInstances.Add(c.instance);

        var context = new ScoringContext
        {
            cup = bottle.GetCupInstances(),
            ownedAdditives = _ownedAdditives,
            beltFlavorCounts = belt.GetFlavorCounts(),
            beltInstances = beltInstances,
            allAdditivesPool = shopManager != null ? shopManager.additivePool : null,
            addToDeck = template =>
            {
                if (template != null) _ownedAdditives.Add(new AdditiveInstance(template));
            },
            removeFromDeck = inst =>
            {
                if (inst != null) _ownedAdditives.Remove(inst);
            },
            requestPlayerDeletion = inst =>
            {
                if (inst != null) _pendingPlayerDeletions.Add(inst);
            },
            addBeltSize = amount => _bonusBeltSize += amount,
            addCupCapacity = amount => bottle.AddCapacity(amount),
            getEffectiveness = popularity != null ? popularity.GetEffectiveness : (System.Func<AdditiveInstance, float>)null,
            reduceHeat = popularity != null ? popularity.ReduceAllHeat : (System.Action<float>)null,
            modifyAmountChange = modifierManager != null ? modifierManager.ModifyAmountChange : (System.Func<AdditiveInstance, float, float>)null,
            resolveCardRect = ResolveCardRect,
            fallbackAnchor = bottle.dropZoneRect
        };

        var result = scoringManager.ScoreSoda(context);

        if (scoreFX != null)
            yield return StartCoroutine(scoreFX.PlaySequence(result.events, CumulativeScore));

        // Update flavor popularity AFTER the reveal, so this soda was scored with the
        // popularity it was built under and card cues change once the popups are done.
        popularity?.RecordBrew(context.cup);

        CumulativeScore += result.total;
        scoringManager.SetCurrentScoreDisplay(CumulativeScore);

        AttemptsUsed++;

        int attemptsRemaining = EffectiveMaxAttempts - AttemptsUsed;
        OnAttemptScored?.Invoke((float)CumulativeScore, attemptsRemaining); // float event kept for existing listeners

        // Resolve any DeletePlayerChosenThenSelf requests queued up during this attempt's
        // scoring pass - opens deckStatsPanel in its deletion mode and waits for the
        // player to actually click a card before continuing, one request at a time.
        if (_pendingPlayerDeletions.Count > 0)
            yield return StartCoroutine(ResolvePlayerDeletions());

        if (CumulativeScore >= CurrentRequirementValue)
        {
            HandleRoundSuccess(attemptsRemaining);
        }
        else if (attemptsRemaining <= 0)
        {
            CurrentState = State.GameOver;
            OnGameOver?.Invoke();
        }
        else
        {
            // Failed but attempts remain: throw out this hand entirely and draw a
            // brand new random one, rather than reusing the same 10 additives.
            bottle.ClearAndDestroy();
            SpawnHand();
            CurrentState = State.Playing;
        }
    }

    /// <summary>
    /// Resolves every pending DeletePlayerChosenThenSelf request queued up during this
    /// attempt's scoring pass, one at a time - opens deckStatsPanel in its special
    /// deletion mode (see DeckStatsPanel.ShowForDeletion) and waits for the player to
    /// actually click a card before moving on to the next pending request, if any. The
    /// requesting additive has already deleted ITSELF synchronously during scoring (see
    /// AdditiveInstance.RequestPlayerChosenDeletion) - this only handles the separate,
    /// player-chosen additional deletion.
    /// </summary>
    private IEnumerator ResolvePlayerDeletions()
    {
        while (_pendingPlayerDeletions.Count > 0)
        {
            var requester = _pendingPlayerDeletions[0];
            _pendingPlayerDeletions.RemoveAt(0);

            if (deckStatsPanel == null)
            {
                Debug.LogWarning($"[RoundManager] '{name}' has no deckStatsPanel assigned - can't let the " +
                                  $"player choose a card to delete for '{requester?.Name}'. Skipping.", this);
                continue;
            }
            if (_ownedAdditives.Count == 0)
            {
                Debug.Log($"[RoundManager] '{requester?.Name}' requested a player-chosen deletion, but the " +
                          "collection is empty - nothing left to delete. Skipping.", this);
                continue;
            }

            AdditiveInstance chosen = null;
            bool done = false;
            deckStatsPanel.ShowForDeletion(picked =>
            {
                chosen = picked;
                done = true;
            },
            onSkipped: () =>
            {
                DeletionSkips++;
                Debug.Log($"[RoundManager] Card deletion skipped - {DeletionSkips} total this run. " +
                          "Shop rarity permanently up a little.");
                done = true;
            });

            yield return new WaitUntil(() => done);

            if (chosen != null)
                _ownedAdditives.Remove(chosen);
        }
    }

    private void HandleRoundSuccess(int attemptsRemaining)
    {
        popularity?.OnRoundEnded(); // flavors recover a little between rounds
        CurrentState = State.RoundWon;
        OnRoundSucceeded?.Invoke();

        // Efficiency: 1.0 if won on the first attempt, 0.0 if it took every attempt.
        float efficiency = (float)attemptsRemaining / Mathf.Max(EffectiveMaxAttempts - 1, 1);
        bottle.ClearAndDestroy(); // any cards left in the cup are discarded; belt is cleared/redrawn on the next round

        bool isModifierRound = RoundNumber % modifierEveryNRounds == 0;
        Debug.Log($"[RoundManager] Round {RoundNumber} won (efficiency {efficiency:0.00}). " +
                  $"Going to {(isModifierRound ? "MODIFIER CHOICE" : "SHOP")} next.");

        if (isModifierRound)
        {
            CurrentState = State.ModifierChoice;
            var offers = modifierManager.GenerateOffers(_skipStreak);
            Debug.Log($"[RoundManager] ModifierManager.GenerateOffers() returned {offers.Count} offer(s). Invoking OnModifierOffersReady.");
            OnModifierOffersReady?.Invoke(offers);
        }
        else
        {
            OpenShop(efficiency);
        }

        _pendingEfficiency = efficiency;
    }

    private float _pendingEfficiency;

    private void OpenShop(float efficiency)
    {
        CurrentState = State.Shopping;
        RerollsThisShop = 0;
        var offers = shopManager.GenerateOffers(efficiency, ShopSkips, DeletionSkips);
        Debug.Log($"[RoundManager] ShopManager.GenerateOffers() returned {offers.Count} offer(s). Invoking OnShopOffersReady.");
        OnShopOffersReady?.Invoke(offers);
    }

    // ---------- UI callbacks ----------

    /// <summary>Call from your modifier-choice UI when the player picks one (or null/skip).</summary>
    public void OnModifierChosen(ModifierData chosen)
    {
        Debug.Log($"[RoundManager] OnModifierChosen: {(chosen != null ? chosen.modifierName : "skipped")}. Opening shop next.");

        if (chosen != null)
        {
            _skipStreak = 0; // picking something resets the streak - the payoff for banking skips
            var result = modifierManager.ApplyModifier(chosen, _ownedAdditives, shopManager != null ? shopManager.additivePool : null);
            syrupFX?.ShowSyrupApplied(chosen, result);
        }
        else
        {
            _skipStreak++;
            Debug.Log($"[RoundManager] Shop/modifier skip streak now {_skipStreak} - offers are skewed further toward rarer tiers.");
        }

        OpenShop(_pendingEfficiency);
    }

    /// <summary>Call from your shop UI when the player picks one (or null/skip).</summary>
    public void OnShopPickChosen(AdditiveData chosen)
    {
        Debug.Log($"[RoundManager] OnShopPickChosen: {(chosen != null ? chosen.additiveName : "skipped")}. " +
                  $"Advancing to round {RoundNumber + 1}.");

        if (chosen != null)
        {
            _skipStreak = 0; // picking something resets the streak - the payoff for banking skips
            _ownedAdditives.Add(new AdditiveInstance(chosen));
        }
        else
        {
            // Skipping the shop is a permanent investment: better shop odds for the rest of the run,
            // and an extra offer slot every few skips (see ShopManager).
            ShopSkips++;
            _skipStreak++; // syrup offers still use the old streak (resets when something is picked)
            Debug.Log($"[RoundManager] Shop skipped - {ShopSkips} total skip(s) this run. Shop rarity permanently up; " +
                      $"{(shopManager != null ? shopManager.OfferCountFor(ShopSkips) : 0)} offer slot(s) from now on.");
        }

        RoundNumber++;
        BeginRound();
    }

    /// <summary>Skip button: closes the shop, permanently improves it, and starts the next round.</summary>
    public void SkipShop() => OnShopPickChosen(null);

    /// <summary>
    /// Reroll button: new shop offers right now, at the cost of permanently raising every future
    /// round's score requirement by rerollRequirementGrowth (a smaller step than a round).
    /// Returns false if rerolling isn't allowed right now.
    /// </summary>
    public bool RerollShop()
    {
        if (!CanReroll || shopManager == null) return false;

        RerollsThisShop++;
        ShopRerolls++;
        RerollRequirementMultiplier *= Mathf.Max(1f, rerollRequirementGrowth);

        var offers = shopManager.GenerateOffers(_pendingEfficiency, ShopSkips, DeletionSkips);
        Debug.Log($"[RoundManager] Shop rerolled ({RerollsThisShop} this shop). Next round now needs {NextRoundRequirement}.");
        ShopRerolled?.Invoke(offers);
        return true;
    }
}