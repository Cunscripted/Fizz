using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Makes a ScrollRect's content area exactly as tall (or wide) as the entries inside it, so
/// every entry can be scrolled to and the scrollbar handle reflects how much there is.
///
/// Why this is needed: a GridLayoutGroup / VerticalLayoutGroup arranges its children inside
/// whatever size its RectTransform ALREADY has - it never grows or shrinks that RectTransform
/// to fit them. Without something resizing the content, the ScrollRect thinks the content is
/// always the same size: extra rows hang off the bottom where you can't scroll to them, and
/// the handle never changes.
///
/// Refresh() force-rebuilds the content's layout group (so it reflects the CURRENT children),
/// reads the size that layout group wants (LayoutUtility.GetPreferredHeight / Width - for a
/// grid that's padding + rows * cell height + spacing), resizes the content to it, then sizes
/// the scrollbar handle from the viewport/content ratio.
///
/// Attach anywhere and reference the pieces directly, or put it on the ScrollRect's
/// GameObject and let Reset()/Awake() fill in the obvious defaults. Call Refresh() any time
/// entries are added or removed - DeckStatsPanel and ActiveComboPanel already do.
///
/// Setup tips: give the content a top pivot (Pivot Y = 1) and top anchors so it grows
/// downward, and don't also put a ContentSizeFitter on it (they'd fight over the height).
/// </summary>
public class ScrollbarContentSizer : MonoBehaviour
{
    public ScrollRect scrollRect;
    [Tooltip("The RectTransform actually holding the entries - the one with the GridLayoutGroup / VerticalLayoutGroup on it. Defaults to scrollRect.content if left unset.")]
    public RectTransform content;
    [Tooltip("Defaults to scrollRect.verticalScrollbar (or horizontalScrollbar, depending on 'vertical' below) if left unset.")]
    public Scrollbar scrollbar;

    [Tooltip("Size content/scrollbar for vertical scrolling (content grows downward as rows are added). Turn off for a horizontal scroll setup instead.")]
    public bool vertical = true;

    [Tooltip("If true, the content is never made smaller than the viewport. Leave OFF if the layout group has " +
             "Child Force Expand Height on, or a short list would get its entries stretched to fill the space. " +
             "With it off, a short list just sits at the top (as long as the content's Pivot Y is 1).")]
    public bool atLeastViewportSize = false;

    [Tooltip("If true, scroll position resets to the top/start every time Refresh() runs (e.g. after the deck stats grid repopulates), so a freshly (re)opened panel always starts scrolled to the top instead of wherever it happened to be left from last time.")]
    public bool resetPositionOnRefresh = true;

    [Tooltip("If true, the scrollbar is hidden whenever everything already fits (nothing to scroll).")]
    public bool hideScrollbarWhenNotNeeded = false;

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
    public void Refresh() => Refresh(resetPositionOnRefresh);

    /// <summary>
    /// Same as Refresh(), but you choose whether the scroll position jumps back to the top. Pass false for
    /// updates that happen while the player may be scrolled down (e.g. the combo list changing live).
    /// </summary>
    public void Refresh(bool resetPosition)
    {
        if (content == null)
        {
            Debug.LogWarning($"{nameof(ScrollbarContentSizer)} on '{name}' has no content assigned - can't resize the scroll area.", this);
            return;
        }

        // Layout groups don't calculate anything while inactive - wait until the panel is open.
        if (!content.gameObject.activeInHierarchy) return;

        // 1. Let the layout group arrange the CURRENT children and work out how much room they need.
        LayoutRebuilder.ForceRebuildLayoutImmediate(content);

        RectTransform viewport = scrollRect != null && scrollRect.viewport != null
            ? scrollRect.viewport
            : content.parent as RectTransform;

        // 2. Resize the content to fit them.
        var axis = vertical ? RectTransform.Axis.Vertical : RectTransform.Axis.Horizontal;
        float needed = vertical ? LayoutUtility.GetPreferredHeight(content) : LayoutUtility.GetPreferredWidth(content);
        float viewportSize = viewport != null ? (vertical ? viewport.rect.height : viewport.rect.width) : 0f;
        if (atLeastViewportSize) needed = Mathf.Max(needed, viewportSize);
        content.SetSizeWithCurrentAnchors(axis, needed);

        // 3. Rebuild once more so children are positioned inside the new size.
        LayoutRebuilder.ForceRebuildLayoutImmediate(content);

        // 4. Scrollbar handle: same ratio ScrollRect uses internally - shrinks as more content is
        //    off-screen, back to full when everything fits.
        float contentSize = vertical ? content.rect.height : content.rect.width;
        float ratio = contentSize <= 0f || viewportSize <= 0f ? 1f : Mathf.Clamp01(viewportSize / contentSize);
        if (scrollbar != null)
        {
            scrollbar.size = ratio;
            if (hideScrollbarWhenNotNeeded) scrollbar.gameObject.SetActive(ratio < 0.999f);
        }

        if (scrollRect != null)
        {
            if (resetPosition)
            {
                if (vertical) scrollRect.verticalNormalizedPosition = 1f;
                else scrollRect.horizontalNormalizedPosition = 0f;
            }
            else
            {
                // Content may have shrunk while scrolled down - nudge the ScrollRect to re-clamp.
                if (vertical) scrollRect.verticalNormalizedPosition = Mathf.Clamp01(scrollRect.verticalNormalizedPosition);
                else scrollRect.horizontalNormalizedPosition = Mathf.Clamp01(scrollRect.horizontalNormalizedPosition);
            }
        }
    }
}