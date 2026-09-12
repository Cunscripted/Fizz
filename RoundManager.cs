using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

/// <summary>
/// Drives the full round loop:
///   Spawn 10 additives on the belt -> player builds a soda in the bottle ->
///   Brew (score) -> up to 3 attempts to meet the round's score requirement ->
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

    [Header("Events (wire to UI)")]
    public UnityEvent<int> OnRoundStarted;                 // round number
    public UnityEvent<int> OnScoreRequirementSet;           // required score
    public UnityEvent<float, int> OnAttemptScored;          // total score, attempts remaining
    public UnityEvent OnRoundSucceeded;
    public UnityEvent OnGameOver;
    public UnityEvent<List<AdditiveData>> OnShopOffersReady;
    public UnityEvent<List<ModifierData>> OnModifierOffersReady;

    public State CurrentState { get; private set; }
    public int RoundNumber { get; private set; } = 1;
    public int AttemptsUsed { get; private set; } = 0;
    public int CurrentRequirement { get; private set; }

    /// <summary>Everything the player currently owns, for a deck-stats/collection view.</summary>
    public IReadOnlyList<AdditiveInstance> OwnedAdditives => _ownedAdditives;

    private readonly List<AdditiveInstance> _ownedAdditives = new List<AdditiveInstance>();

    private void Start()
    {
        foreach (var template in startingAdditives)
            _ownedAdditives.Add(new AdditiveInstance(template));

        if (modifierManager != null)
            modifierManager.OnAttemptBonusGranted += amount => _bonusAttempts += amount;

        if (brewButton != null)
            brewButton.onClick.AddListener(OnBrewPressed);

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
        CurrentState = State.Spawning;
        AttemptsUsed = 0;
        CurrentRequirement = Mathf.RoundToInt(baseScoreRequirement * Mathf.Pow(difficultyGrowth, RoundNumber - 1));

        var hand = DrawHand(cardsPerRound);
        foreach (var instance in hand)
        {
            var card = Instantiate(cardPrefab, belt.transform);
            card.instance = instance;
        }

        OnRoundStarted?.Invoke(RoundNumber);
        OnScoreRequirementSet?.Invoke(CurrentRequirement);
        CurrentState = State.Playing;
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

        // Snapshot the cup's card views so score events can be anchored to the
        // specific card that produced them (for the floating text spawn point).
        var cupCards = bottle.AcceptedCards;
        RectTransform ResolveCardRect(AdditiveInstance inst)
        {
            foreach (var c in cupCards)
                if (c.instance == inst) return c.Rect;
            return null;
        }

        var context = new ScoringContext
        {
            cup = bottle.GetCupInstances(),
            ownedAdditives = _ownedAdditives,
            beltFlavorCounts = belt.GetFlavorCounts(),
            addToDeck = template =>
            {
                if (template != null) _ownedAdditives.Add(new AdditiveInstance(template));
            },
            resolveCardRect = ResolveCardRect,
            fallbackAnchor = bottle.dropZoneRect
        };

        var result = scoringManager.ScoreSoda(context);

        if (scoreFX != null)
            yield return StartCoroutine(scoreFX.PlaySequence(result.events));

        AttemptsUsed++;

        int attemptsRemaining = EffectiveMaxAttempts - AttemptsUsed;
        OnAttemptScored?.Invoke(result.total, attemptsRemaining);

        if (result.total >= CurrentRequirement)
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
            // Failed but attempts remain: reset the cup for another try with the same hand.
            bottle.ClearToBelt(belt);
            CurrentState = State.Playing;
        }
    }

    private void HandleRoundSuccess(int attemptsRemaining)
    {
        CurrentState = State.RoundWon;
        OnRoundSucceeded?.Invoke();

        // Efficiency: 1.0 if won on the first attempt, 0.0 if it took every attempt.
        float efficiency = (float)attemptsRemaining / Mathf.Max(EffectiveMaxAttempts - 1, 1);
        bottle.ClearToBelt(belt); // leftover unused additives return; belt is cleared on next spawn anyway

        if (RoundNumber % modifierEveryNRounds == 0)
        {
            CurrentState = State.ModifierChoice;
            var offers = modifierManager.GenerateOffers();
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
        OnShopOffersReady?.Invoke(offers);
    }

    // ---------- UI callbacks ----------

    /// <summary>Call from your modifier-choice UI when the player picks one (or null/skip).</summary>
    public void OnModifierChosen(ModifierData chosen)
    {
        if (chosen != null)
            modifierManager.ApplyModifier(chosen, _ownedAdditives);

        OpenShop(_pendingEfficiency);
    }

    /// <summary>Call from your shop UI when the player picks one (or null/skip).</summary>
    public void OnShopPickChosen(AdditiveData chosen)
    {
        if (chosen != null)
            _ownedAdditives.Add(new AdditiveInstance(chosen));

        RoundNumber++;
        BeginRound();
    }
}