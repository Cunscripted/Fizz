using UnityEngine;

/// <summary>
/// Shows feedback when a syrup is applied - "{name} Applied!" plus, if the syrup
/// added or removed a specific additive, that additive's icon and name. Unlike
/// ScoreFXPlayer this isn't a sequenced playback - it's a single spawn per syrup
/// pick, called directly from RoundManager.OnModifierChosen right after
/// ModifierManager.ApplyModifier runs, since syrup selection previously had no
/// visual feedback at all (ScoreFXPlayer only runs during scoring, not menus).
/// </summary>
public class SyrupApplyPopup : MonoBehaviour
{
    public FloatingScoreText floatingTextPrefab;
    [Tooltip("Parent the popup spawns under - usually a top-level Canvas RectTransform.")]
    public RectTransform textLayer;
    [Tooltip("Where the popup(s) spawn from - e.g. the syrup panel's center, or a fixed HUD point. Falls back to textLayer if unset.")]
    public RectTransform anchor;

    [Header("Colors")]
    public Color appliedColor = new Color(0.75f, 0.4f, 1f);    // purple - matches ScoreFXPlayer's comboColor for "special event"
    public Color addedColor = new Color(0.6f, 1f, 0.6f);       // green - matches addedToDeckColor
    public Color removedColor = new Color(0.6f, 0.6f, 0.6f);   // gray - matches deletedColor

    /// <summary>Call right after ModifierManager.ApplyModifier() with the same modifier and its result.</summary>
    public void ShowSyrupApplied(ModifierData modifier, ModifierApplyResult result)
    {
        if (modifier == null) return;

        SpawnPopup($"{modifier.modifierName} Applied!", appliedColor, null);

        if (result.addedAdditive != null)
            SpawnPopup($"Gained: {result.addedAdditive.additiveName}", addedColor, result.addedAdditive.icon);

        if (result.removedAdditive != null)
            SpawnPopup($"Lost: {result.removedAdditive.additiveName}", removedColor, result.removedAdditive.icon);
    }

    private void SpawnPopup(string text, Color color, Sprite icon)
    {
        if (floatingTextPrefab == null) return;

        RectTransform parent = textLayer != null ? textLayer : (RectTransform)transform;
        var popup = Instantiate(floatingTextPrefab, parent);

        RectTransform spawnAnchor = anchor != null ? anchor : parent;
        popup.Rect.position = spawnAnchor.position; // world-space copy, works regardless of Canvas render mode

        popup.Play(text, color, icon);
    }
}
