using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Keeps a Scrollbar's handle size (and, by extension, whether it even looks
/// "active") in sync with how much content is actually inside it. A GridLayoutGroup
/// arranges children within whatever size its RectTransform already has - it doesn't
/// grow that RectTransform to fit its content on its own, so without something like
/// this the ScrollRect never sees a content height that reflects the real entry
/// count, and the scrollbar's handle stays a fixed size no matter how many entries
/// get spawned in. Refresh() force-rebuilds the content's own layout group first
/// (so its preferred size reflects the CURRENT child count, not a stale one) and
/// then sizes the scrollbar handle directly from the viewport/content ratio - the
/// same math ScrollRect uses internally, just triggered on demand rather than
/// waiting on Unity's own layout pass timing.
///
/// Attach anywhere and reference the pieces directly, or drop it on the same
/// GameObject as the ScrollRect and let Reset()/Awake() wire the obvious defaults.
/// Call Refresh() any time entries are added to or removed from content - e.g.
/// DeckStatsPanel calls it right after Populate() repopulates the deck stats grid.
/// </summary>
public class ScrollbarContentSizer : MonoBehaviour
{
    public ScrollRect scrollRect;
    [Tooltip("The RectTransform actually holding the entries - the one with the GridLayoutGroup on it. Defaults to scrollRect.content if left unset.")]
    public RectTransform content;
    [Tooltip("Defaults to scrollRect.verticalScrollbar (or horizontalScrollbar, depending on 'vertical' below) if left unset.")]
    public Scrollbar scrollbar;

    [Tooltip("Size content/scrollbar for vertical scrolling (content grows downward as rows are added via GridLayoutGroup). Turn off for a horizontal scroll setup instead.")]
    public bool vertical = true;

    [Tooltip("If true, scroll position resets to the top/start every time Refresh() runs (e.g. after the deck stats grid repopulates), so a freshly (re)opened panel always starts scrolled to the top instead of wherever it happened to be left from last time.")]
    public bool resetPositionOnRefresh = true;

    private void Reset()
    {
        scrollRect = GetComponentInChildren<ScrollRect>();
    }

    private void Awake()
    {
        if (scrollRect != null)
        {
            if (content == null) content = scrollRect.content;
            if (scrollbar == null) scrollbar = vertical ? scrollRect.verticalScrollbar : scrollRect.horizontalScrollbar;
        }
    }

    /// <summary>Call right after entries are added to or removed from content.</summary>
    public void Refresh()
    {
        if (content == null)
        {
            Debug.LogWarning($"{nameof(ScrollbarContentSizer)} on '{name}' has no content assigned - can't resize the scrollbar.", this);
            return;
        }

        // The content's own layout group (GridLayoutGroup, etc.) needs to actually run
        // first so its preferred/rect size reflects the CURRENT child count.
        LayoutRebuilder.ForceRebuildLayoutImmediate(content);

        if (scrollbar == null) return;

        RectTransform viewport = scrollRect != null && scrollRect.viewport != null
            ? scrollRect.viewport
            : content.parent as RectTransform;
        if (viewport == null) return;

        float viewportSize = vertical ? viewport.rect.height : viewport.rect.width;
        float contentSize = vertical ? content.rect.height : content.rect.width;

        // Same ratio ScrollRect computes internally - handle shrinks as more content
        // is added (more of it is off-screen at once), grows back toward a full-looking
        // 1 as entries are removed and everything fits again.
        scrollbar.size = contentSize <= 0f ? 1f : Mathf.Clamp01(viewportSize / contentSize);

        if (resetPositionOnRefresh && scrollRect != null)
        {
            if (vertical) scrollRect.verticalNormalizedPosition = 1f;
            else scrollRect.horizontalNormalizedPosition = 0f;
        }
    }
}
