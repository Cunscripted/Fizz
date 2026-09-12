using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Canvas UI version of the belt. Arranges active AdditiveCards evenly around a
/// flattened ellipse sized as a PERCENTAGE of this RectTransform's own size,
/// rather than fixed world-space radii - so with a CanvasScaler set to "Scale
/// With Screen Size", the belt automatically stays proportioned correctly as
/// the window/camera resizes, instead of everything shifting around.
///
/// Cards live as children of this RectTransform while on the belt. Cards
/// currently being dragged are skipped; cards added back (dropped somewhere
/// invalid) are reparented here and simply resume being positioned, producing
/// the "drifts back to the belt" behavior automatically.
/// </summary>
[RequireComponent(typeof(RectTransform))]
public class ConveyorBelt : MonoBehaviour
{
    [Header("Shape (percentage of this RectTransform's own width/height)")]
    [Range(0f, 1f)] public float radiusXPercent = 0.85f;
    [Range(0f, 1f)] public float radiusYPercent = 0.6f;
    public float rotationDegreesPerSecond = 8f;

    [Header("Motion")]
    [Tooltip("How fast non-dragged cards ease toward their belt slot. Higher = snappier.")]
    public float easeSpeed = 6f;

    [Header("Draw Order")]
    [Tooltip("Keeps this belt behind every other UI element sharing its parent (bottle, buttons, " +
             "panels, etc.), while staying IN FRONT of backgroundVisual below if you've assigned one.")]
    public bool alwaysRenderAtBack = true;
    [Tooltip("Optional - a separate background/track image sibling (e.g. from ConveyorBeltShaderController) " +
             "that should render even further back than the belt+cards themselves. If set, the belt is kept " +
             "directly in front of it instead of unconditionally claiming the very first sibling slot, so the " +
             "two don't fight over who's 'more behind'.")]
    public RectTransform backgroundVisual;

    private RectTransform _rect;
    private readonly List<AdditiveCard> _cards = new List<AdditiveCard>();
    private readonly List<AdditiveCard> _sortBuffer = new List<AdditiveCard>();
    private float _baseAngle;

    private void Awake()
    {
        _rect = GetComponent<RectTransform>();
    }

    private void Start()
    {
        // A common setup mistake: if this RectTransform has no real size (collapsed
        // anchors, sizeDelta 0, etc.), radiusX/radiusY below both compute to 0 and
        // every card collapses onto the same point - looking like only one spawned.
        if (_rect.rect.width < 10f || _rect.rect.height < 10f)
        {
            Debug.LogWarning($"[ConveyorBelt] '{name}' has a near-zero RectTransform size " +
                              $"({_rect.rect.width:0} x {_rect.rect.height:0}). Cards will all collapse to the " +
                              "same position since the ellipse radius is a percentage of this size - give this " +
                              "RectTransform an actual width/height.", this);
        }

        EnforceDrawOrder();
    }

    private void LateUpdate()
    {
        // Re-enforced every frame (cheap - just an index check/swap) rather than
        // only once at Start, so the belt stays at the back even if something else
        // dynamically adds or reorders siblings under the same parent later.
        EnforceDrawOrder();
    }

    /// <summary>
    /// Keeps the belt behind other UI without fighting a dedicated background sibling
    /// for the very first slot - if backgroundVisual is assigned, the belt sits directly
    /// in front of it (background stays furthest back); otherwise the belt just claims
    /// the first slot itself.
    /// </summary>
    private void EnforceDrawOrder()
    {
        if (!alwaysRenderAtBack) return;

        if (backgroundVisual != null && backgroundVisual.parent == _rect.parent)
        {
            int desiredIndex = backgroundVisual.GetSiblingIndex() + 1;
            if (_rect.GetSiblingIndex() != desiredIndex)
                _rect.SetSiblingIndex(desiredIndex);
        }
        else if (_rect.GetSiblingIndex() != 0)
        {
            _rect.SetAsFirstSibling();
        }
    }

    private void Update()
    {
        _baseAngle += rotationDegreesPerSecond * Time.deltaTime;

        for (int i = 0; i < _cards.Count; i++)
        {
            var card = _cards[i];
            if (card == null || card.IsDragging) continue;

            Vector2 target = GetSlotAnchoredPosition(i, _cards.Count);
            card.Rect.anchoredPosition = Vector2.Lerp(card.Rect.anchoredPosition, target, Time.deltaTime * easeSpeed);

            // Face "outward" along the ring for a nice carousel tilt (optional, remove if you want flat cards)
            float angle = _baseAngle + i * (360f / Mathf.Max(_cards.Count, 1));
            float tilt = Mathf.Sin(angle * Mathf.Deg2Rad) * 8f;
            card.Rect.localRotation = Quaternion.Lerp(card.Rect.localRotation, Quaternion.Euler(0, 0, -tilt), Time.deltaTime * easeSpeed);
        }

        UpdateDepthSorting();
    }

    /// <summary>
    /// Canvas UI doesn't have per-object sorting layers like SpriteRenderer does -
    /// draw order within one Canvas is just sibling index (later sibling = drawn on
    /// top). So "lower sorting the higher up" is achieved by re-sorting the belt's
    /// children by Y each frame: cards further up the ellipse get an earlier sibling
    /// index (drawn behind), cards further down get a later one (drawn in front) -
    /// giving the orbiting cards a coherent front/back depth read instead of a flat pile.
    /// </summary>
    private void UpdateDepthSorting()
    {
        _sortBuffer.Clear();
        foreach (var c in _cards)
            if (c != null && !c.IsDragging) _sortBuffer.Add(c);

        // Highest Y (furthest up) gets sibling index 0 (drawn first/behind);
        // lowest Y (furthest down) ends up last (drawn on top).
        _sortBuffer.Sort((a, b) => b.Rect.anchoredPosition.y.CompareTo(a.Rect.anchoredPosition.y));

        for (int i = 0; i < _sortBuffer.Count; i++)
            _sortBuffer[i].Rect.SetSiblingIndex(i);
    }

    private Vector2 GetSlotAnchoredPosition(int index, int count)
    {
        float angle = (_baseAngle + index * (360f / Mathf.Max(count, 1))) * Mathf.Deg2Rad;
        float radiusX = _rect.rect.width * 0.5f * radiusXPercent;
        float radiusY = _rect.rect.height * 0.5f * radiusYPercent;
        return new Vector2(Mathf.Cos(angle) * radiusX, Mathf.Sin(angle) * radiusY);
    }

    /// <summary>Call when a new card spawns at round start, or when a dropped card returns to the belt.</summary>
    public void AddCard(AdditiveCard card)
    {
        if (!_cards.Contains(card))
            _cards.Add(card);

        // worldPositionStays keeps its current on-screen position, so it eases in
        // from wherever it was (e.g. mid-drag) rather than snapping instantly.
        card.Rect.SetParent(_rect, worldPositionStays: true);
    }

    /// <summary>Call when a card is accepted into the SodaBottle and should leave the belt.</summary>
    public void RemoveCard(AdditiveCard card)
    {
        _cards.Remove(card);
    }

    public IReadOnlyList<AdditiveCard> Cards => _cards;

    /// <summary>
    /// Counts additives currently on the belt (not in the bottle) by flavor family.
    /// Used by "gain more points/mult per flavor on the belt" additive and syrup effects.
    /// </summary>
    public Dictionary<FlavorType, int> GetFlavorCounts()
    {
        var counts = new Dictionary<FlavorType, int>();
        foreach (var card in _cards)
        {
            if (card == null || card.instance == null) continue;
            foreach (var flavor in card.instance.Flavors)
            {
                counts.TryGetValue(flavor, out int c);
                counts[flavor] = c + 1;
            }
        }
        return counts;
    }
}