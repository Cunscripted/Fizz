using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Shows "mixes" (brew attempts) remaining as a row of small icon images - one per
/// attempt available this round, all bright to start, greying out left to right as
/// attempts get used. Rather than destroying and rebuilding the whole row every round,
/// it only ADDS new icons when EffectiveMaxAttempts grows (e.g. an AddAttempt syrup),
/// keeping existing icons in place. Grows immediately the moment a syrup grants the
/// bonus (via ModifierManager.OnAttemptBonusGranted), not just at the next round start.
/// </summary>
public class MixesRemainingDisplay : MonoBehaviour
{
    public RoundManager roundManager;
    [Tooltip("Optional - if assigned, the icon row grows the instant an AddAttempt syrup is picked, " +
             "rather than waiting for the next round to start.")]
    public ModifierManager modifierManager;
    [Tooltip("Prefab for a single mix icon - just needs an Image component.")]
    public Image iconPrefab;
    [Tooltip("Parent the icons spawn into. Add a HorizontalLayoutGroup here for even spacing - " +
             "without one, every icon lands at the same position and stacks on top of each other.")]
    public RectTransform iconContainer;

    [Header("Colors")]
    public Color availableColor = Color.white;
    public Color usedColor = new Color(0.4f, 0.4f, 0.4f, 0.6f);

    [Header("Size")]
    [Tooltip("Forced onto every spawned icon explicitly, regardless of whatever size the prefab was " +
             "authored with or what the layout group's own sizing settings would otherwise produce.")]
    public Vector2 iconSize = new Vector2(32f, 32f);

    private readonly List<Image> _icons = new List<Image>();

    private void OnEnable()
    {
        if (roundManager != null)
        {
            roundManager.OnRoundStarted.AddListener(OnRoundStarted);
            roundManager.OnAttemptScored.AddListener(OnAttemptScored);
        }
        if (modifierManager != null)
            modifierManager.OnAttemptBonusGranted += OnAttemptBonusGranted;
    }

    private void OnDisable()
    {
        if (roundManager != null)
        {
            roundManager.OnRoundStarted.RemoveListener(OnRoundStarted);
            roundManager.OnAttemptScored.RemoveListener(OnAttemptScored);
        }
        if (modifierManager != null)
            modifierManager.OnAttemptBonusGranted -= OnAttemptBonusGranted;
    }

    private void OnRoundStarted(int roundNumber)
    {
        SyncIconCount();
        RefreshGreyOut(); // fresh round - every icon (including any just added) goes back to available
    }

    private void OnAttemptScored(float cumulativeScore, int attemptsRemaining)
    {
        RefreshGreyOut();
    }

    private void OnAttemptBonusGranted(int amount)
    {
        // Grow immediately at the moment the syrup is applied, rather than waiting for
        // the next OnRoundStarted - doesn't touch grey-out state, since AttemptsUsed
        // hasn't reset for the new round yet at this point in the flow anyway.
        SyncIconCount();
    }

    /// <summary>
    /// Adds icons if EffectiveMaxAttempts is now higher than what's currently shown, or
    /// removes any excess if it's somehow lower (shouldn't normally happen, since
    /// attempts only ever grow, but handled defensively). Never destroys and rebuilds
    /// icons that are still valid.
    /// </summary>
    private void SyncIconCount()
    {
        if (iconPrefab == null)
        {
            Debug.LogError($"[MixesRemainingDisplay] '{name}' has no iconPrefab assigned.", this);
            return;
        }
        if (iconContainer == null)
        {
            Debug.LogError($"[MixesRemainingDisplay] '{name}' has no iconContainer assigned.", this);
            return;
        }
        if (roundManager == null)
        {
            Debug.LogError($"[MixesRemainingDisplay] '{name}' has no roundManager assigned.", this);
            return;
        }
        if (iconContainer.GetComponent<HorizontalLayoutGroup>() == null)
        {
            Debug.LogWarning($"[MixesRemainingDisplay] '{iconContainer.name}' has no HorizontalLayoutGroup - " +
                              "icons will all spawn at the same position and stack on top of each other. " +
                              "Add one in the Inspector.", iconContainer);
        }

        int target = roundManager.EffectiveMaxAttempts;

        while (_icons.Count < target)
        {
            var icon = Instantiate(iconPrefab, iconContainer);
            ApplyIconSize(icon);
            _icons.Add(icon);
        }

        while (_icons.Count > target)
        {
            var last = _icons[_icons.Count - 1];
            _icons.RemoveAt(_icons.Count - 1);
            if (last != null)
            {
                // Detach BEFORE destroying - Destroy() is deferred to the end of the frame,
                // but a layout group can rebuild synchronously the same frame.
                last.transform.SetParent(null);
                Destroy(last.gameObject);
            }
        }
    }

    /// <summary>
    /// Forces iconSize onto the spawned icon two ways, so it sticks regardless of the
    /// layout group's "Control Child Size" setting: sizeDelta directly (used when Control
    /// Child Size is off) and a LayoutElement's preferred size (which takes priority over
    /// the child's own rect when Control Child Size is on).
    /// </summary>
    private void ApplyIconSize(Image icon)
    {
        icon.rectTransform.sizeDelta = iconSize;

        var layoutElement = icon.GetComponent<LayoutElement>();
        if (layoutElement == null) layoutElement = icon.gameObject.AddComponent<LayoutElement>();
        layoutElement.preferredWidth = iconSize.x;
        layoutElement.preferredHeight = iconSize.y;
    }

    private void RefreshGreyOut()
    {
        if (roundManager == null) return;

        int used = roundManager.AttemptsUsed;
        for (int i = 0; i < _icons.Count; i++)
        {
            if (_icons[i] == null) continue;
            _icons[i].color = i < used ? usedColor : availableColor;
        }
    }
}