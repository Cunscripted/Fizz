using System.Collections;
using System.Collections.Generic;
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
    public int maxAdditives = 5;

    public bool IsFull => _accepted.Count >= maxAdditives;
    public IReadOnlyList<AdditiveCard> AcceptedCards => _accepted;
    public bool Contains(AdditiveCard card) => _accepted.Contains(card);
    private readonly List<AdditiveCard> _accepted = new List<AdditiveCard>();

    /// <summary>
    /// Fires with the current cup contents any time an additive is added, removed,
    /// or the bottle is cleared - lets anything (combo indicators, UI, etc.) react
    /// to the cup live, not just SodaColorController.
    /// </summary>
    public UnityEvent<List<AdditiveInstance>> OnCupChanged;

    private void Awake()
    {
        if (dropZoneRect == null) dropZoneRect = GetComponent<RectTransform>();
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
    /// Returns false (and accepts nothing) if the bottle is already at maxAdditives -
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
        colorController?.UpdateCup(cup);
        OnCupChanged?.Invoke(cup);
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