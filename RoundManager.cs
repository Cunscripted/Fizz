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
    [Tooltip("Templates the player starts the run owning. Grows via shop picks.")]
    public List<AdditiveData> startingAdditives = new List<AdditiveData>();
    public int cardsPerRound = 10;

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
    public int CurrentRequirement { get; private set; }

    /// <summary>Sum of every attempt's total so far this round - what's actually checked against CurrentRequirement.</summary>
    public float CumulativeScore { get; private set; }

    /// <summary>Everything the player currently owns, for a deck-stats/collection view.</summary>
    public IReadOnlyList<AdditiveInstance> OwnedAdditives => _ownedAdditives;

    private readonly List<AdditiveInstance> _ownedAdditives = new List<AdditiveInstance>();

    private void Start()
    {
        foreach (var template in startingAdditives)
            _ownedAdditives.Add(new AdditiveInstance(template));

        if (modifierManager != null)
        {
            modifierManager.OnAttemptBonusGranted += amount => _bonusAttempts += amount;
            modifierManager.OnBeltSizeBonusGranted += amount => _bonusBeltSize += amount;
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

    // ---------- Round start ----------

    public void BeginRound()
    {
        Debug.Log($"[RoundManager] BeginRound() - round {RoundNumber}.");

        CurrentState = State.Spawning;
        AttemptsUsed = 0;
        CumulativeScore = 0f;
        CurrentRequirement = Mathf.RoundToInt(baseScoreRequirement * Mathf.Pow(difficultyGrowth, RoundNumber - 1));

        SpawnHand();

        OnRoundStarted?.Invoke(RoundNumber);
        OnScoreRequirementSet?.Invoke(CurrentRequirement);
        scoringManager.SetRequiredScoreDisplay(CurrentRequirement);
        scoringManager.SetCurrentScoreDisplay(0f);
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
            addBeltSize = amount => _bonusBeltSize += amount,
            resolveCardRect = ResolveCardRect,
            fallbackAnchor = bottle.dropZoneRect
        };

        var result = scoringManager.ScoreSoda(context);

        if (scoreFX != null)
            yield return StartCoroutine(scoreFX.PlaySequence(result.events, CumulativeScore));

        CumulativeScore += result.total;
        scoringManager.SetCurrentScoreDisplay(CumulativeScore);

        AttemptsUsed++;

        int attemptsRemaining = EffectiveMaxAttempts - AttemptsUsed;
        OnAttemptScored?.Invoke(CumulativeScore, attemptsRemaining);

        if (CumulativeScore >= CurrentRequirement)
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

    private void HandleRoundSuccess(int attemptsRemaining)
    {
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
            var offers = modifierManager.GenerateOffers();
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
        var offers = shopManager.GenerateOffers(efficiency);
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
            var result = modifierManager.ApplyModifier(chosen, _ownedAdditives, shopManager != null ? shopManager.additivePool : null);
            syrupFX?.ShowSyrupApplied(chosen, result);
        }

        OpenShop(_pendingEfficiency);
    }

    /// <summary>Call from your shop UI when the player picks one (or null/skip).</summary>
    public void OnShopPickChosen(AdditiveData chosen)
    {
        Debug.Log($"[RoundManager] OnShopPickChosen: {(chosen != null ? chosen.additiveName : "skipped")}. " +
                  $"Advancing to round {RoundNumber + 1}.");

        if (chosen != null)
            _ownedAdditives.Add(new AdditiveInstance(chosen));

        RoundNumber++;
        BeginRound();
    }
}