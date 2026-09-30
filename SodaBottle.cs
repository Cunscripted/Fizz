using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Canvas UI version of the drop target. Attach to the SodaBottle's RectTransform.
/// Drop detection uses RectTransformUtility.RectangleContainsScreenPoint (a screen-space
/// rect test) instead of a Collider2D, so it works correctly under a resizing Canvas.
/// Keeps an ORDERED list of accepted additives (order matters for scoring, matching
/// AdditiveData's left-to-right base effect resolution).
/// </summary>
[RequireComponent(typeof(RectTransform))]
public class SodaBottle : MonoBehaviour
{
    [Tooltip("The rect cards must be released inside to be accepted. Defaults to this object's own RectTransform.")]
    public RectTransform dropZoneRect;
    public SodaColorController colorController;

    [Header("Layout for accepted cards")]
    [Tooltip("Where accepted cards visually stack - usually a child RectTransform inside the bottle art.")]
    public RectTransform stackAnchor;
    public Vector2 stackOffsetPerCard = new Vector2(0f, 40f);
    public float snapSpeed = 10f;

    [Header("Capacity")]
    [Tooltip("Base capacity at the start of a run. AddCupCapacity syrups and AddCupCapacityPermanent additives " +
             "raise it on top of this via AddCapacity() - read MaxAdditives for the real current limit.")]
    public int maxAdditives = 5;

    [Header("Capacity Text (optional)")]
    [Tooltip("Shows how many additives are in the soda vs. how many it can hold, e.g. \"3/5\". Updated " +
             "automatically whenever the cup or its capacity changes - no event wiring needed.")]
    public TMP_Text capacityText;
    [Tooltip("{0} = additives currently in the soda, {1} = maximum it can hold.")]
    public string capacityTextFormat = "{0}/{1}";
    public Color capacityTextColor = Color.white;
    [Tooltip("Text color while the soda is full.")]
    public Color capacityTextFullColor = new Color(1f, 0.45f, 0.35f);

    /// <summary>Extra slots granted over the run by syrups/additives, on top of maxAdditives.</summary>
    public int BonusCapacity { get; private set; }

    /// <summary>The real current limit: base maxAdditives plus every permanent bonus earned this run.</summary>
    public int MaxAdditives => Mathf.Max(0, maxAdditives + BonusCapacity);

    /// <summary>How many additives are currently in the soda.</summary>
    public int CurrentCount => _accepted.Count;

    /// <summary>0-1 share of the soda's capacity currently used - what the liquid shader's fill level is driven by.</summary>
    public float FillPercent => MaxAdditives > 0 ? Mathf.Clamp01((float)_accepted.Count / MaxAdditives) : 0f;

    public bool IsFull => _accepted.Count >= MaxAdditives;
    public IReadOnlyList<AdditiveCard> AcceptedCards => _accepted;
    public bool Contains(AdditiveCard card) => _accepted.Contains(card);
    private readonly List<AdditiveCard> _accepted = new List<AdditiveCard>();

    /// <summary>
    /// Fires with the current cup contents any time an additive is added, removed,
    /// or the bottle is cleared - lets anything (combo indicators, UI, etc.) react
    /// to the cup live, not just SodaColorController.
    /// </summary>
    public UnityEvent<List<AdditiveInstance>> OnCupChanged;

    /// <summary>Fires with (current count, max) whenever either changes - e.g. for a custom capacity display.</summary>
    public UnityEvent<int, int> OnCapacityChanged;

    private void Awake()
    {
        if (dropZoneRect == null) dropZoneRect = GetComponent<RectTransform>();
    }

    private void Start()
    {
        NotifyCupChanged(); // so the capacity text and liquid fill show the right values from the first frame
    }

    /// <summary>
    /// Permanently raises (or, with a negative amount, lowers) how many additives the soda can
    /// hold for the rest of the run. Called by RoundManager for AddCupCapacity syrups and
    /// AddCupCapacityPermanent additives.
    /// </summary>
    public void AddCapacity(int amount)
    {
        if (amount == 0) return;
        BonusCapacity += amount;
        NotifyCupChanged();
    }

    /// <summary>
    /// Screen-space overlap check, replacing the old Collider2D.OverlapPoint.
    /// Pass the PointerEventData's position and pressEventCamera directly.
    /// </summary>
    public bool IsScreenPointInside(Vector2 screenPoint, Camera eventCamera)
    {
        return dropZoneRect != null && RectTransformUtility.RectangleContainsScreenPoint(dropZoneRect, screenPoint, eventCamera);
    }

    /// <summary>
    /// Called by AdditiveCard when it's released over this bottle.
    /// Returns false (and accepts nothing) if the bottle is already at MaxAdditives -
    /// the card should bounce back to the belt in that case.
    /// </summary>
    public bool AcceptAdditive(AdditiveCard card)
    {
        if (IsFull) return false;

        _accepted.Add(card);
        if (stackAnchor != null) card.Rect.SetParent(stackAnchor, worldPositionStays: true);
        StartCoroutine(SnapIntoStack(card, _accepted.Count - 1));
        NotifyCupChanged();
        return true;
    }

    /// <summary>
    /// Detaches an additive from the bottle's list and restacks the remainder, WITHOUT
    /// touching the belt - the caller decides where the card goes next. Call this the
    /// instant a card already in the bottle starts being dragged, so IsFull/the cup list
    /// update immediately rather than only once the drag resolves; otherwise pulling a
    /// card out still leaves it counted, silently blocking the freed-up slot.
    /// </summary>
    public void RemoveFromBottle(AdditiveCard card)
    {
        if (_accepted.Remove(card))
        {
            RestackAll();
            NotifyCupChanged();
        }
    }

    /// <summary>Removes an additive and sends it back to the belt in one call.</summary>
    public void RemoveAdditive(AdditiveCard card, ConveyorBelt belt)
    {
        if (!_accepted.Contains(card)) return;
        RemoveFromBottle(card);
        belt?.AddCard(card);
    }

    public List<AdditiveInstance> GetCupInstances()
    {
        var list = new List<AdditiveInstance>(_accepted.Count);
        foreach (var c in _accepted) list.Add(c.instance);
        return list;
    }

    /// <summary>Empties the bottle back onto the belt, e.g. after a failed scoring attempt.</summary>
    public void ClearToBelt(ConveyorBelt belt)
    {
        foreach (var c in _accepted)
            belt?.AddCard(c);
        _accepted.Clear();
        NotifyCupChanged();
    }

    /// <summary>Destroys every accepted card outright instead of returning it to the belt - used when the whole hand is being redrawn from scratch.</summary>
    public void ClearAndDestroy()
    {
        foreach (var c in _accepted)
            if (c != null) Destroy(c.gameObject);
        _accepted.Clear();
        NotifyCupChanged();
    }

    private void NotifyCupChanged()
    {
        var cup = GetCupInstances();
        colorController?.UpdateCup(cup, MaxAdditives);
        RefreshCapacityText();
        OnCupChanged?.Invoke(cup);
        OnCapacityChanged?.Invoke(_accepted.Count, MaxAdditives);
    }

    private void RefreshCapacityText()
    {
        if (capacityText == null) return;
        capacityText.text = string.Format(capacityTextFormat, _accepted.Count, MaxAdditives);
        capacityText.color = IsFull ? capacityTextFullColor : capacityTextColor;
    }

    private IEnumerator SnapIntoStack(AdditiveCard card, int slotIndex)
    {
        Vector2 target = stackOffsetPerCard * slotIndex;
        // card != null checked FIRST each iteration (short-circuits before touching
        // card.Rect) since this coroutine runs on SodaBottle, not the card itself -
        // destroying the card (e.g. ClearAndDestroy running mid-snap) doesn't stop
        // it automatically, so it has to bail out on its own once the card is gone.
        while (card != null && Vector2.Distance(card.Rect.anchoredPosition, target) > 0.5f)
        {
            card.Rect.anchoredPosition = Vector2.Lerp(card.Rect.anchoredPosition, target, Time.deltaTime * snapSpeed);
            yield return null;
        }
        if (card != null) card.Rect.anchoredPosition = target;
    }

    private void RestackAll()
    {
        for (int i = 0; i < _accepted.Count; i++)
            StartCoroutine(SnapIntoStack(_accepted[i], i));
    }
}