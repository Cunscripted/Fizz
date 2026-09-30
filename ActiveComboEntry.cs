using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// One row in the active-combos panel - purely informational, no interaction.
/// Put this on a small prefab with two TMP_Text fields.
///
/// Level progress: give it an empty RectTransform along the bottom of the row as Progress
/// Box Container. It fills that with one box per use needed to reach the next level:
/// lit green = uses already done, dim = uses still to go. The boxes always stretch to fill
/// the WHOLE container (width and height), splitting it evenly - 3 uses = 3 wide boxes,
/// 10 uses = 10 thin ones - and resize automatically if the container changes size.
/// Boxes are generated automatically (no box prefab needed).
/// </summary>
public class ActiveComboEntry : MonoBehaviour
{
    public TMP_Text nameText;
    public TMP_Text descriptionText;

    [Header("Level (optional)")]
    [Tooltip("Shows e.g. \"Lv 2\" (see Level Format). Hidden if the combo can't level up.")]
    public TMP_Text levelText;
    [Tooltip("{0} = current level.")]
    public string levelFormat = "Lv {0}";
    [Tooltip("Shown instead of the boxes' purpose once the combo is at its max level.")]
    public string maxLevelLabel = "MAX";
    [Tooltip("If on, the level is also shown after the combo name, e.g. \"Sour Power Lv 2\", when no Level Text is assigned.")]
    public bool appendLevelToName = true;

    [Header("Level Progress Boxes (optional)")]
    [Tooltip("Empty RectTransform along the bottom of the entry - the boxes are generated into it.")]
    public RectTransform progressBoxContainer;
    public Color filledBoxColor = new Color(0.35f, 1f, 0.45f, 1f);
    public Color emptyBoxColor = new Color(0.15f, 0.35f, 0.18f, 0.6f);
    [Tooltip("Gap between boxes. The boxes themselves always stretch to fill the rest of the container.")]
    public float boxSpacing = 4f;
    [Tooltip("Optional sprite for the boxes (e.g. a rounded square). Plain rectangles if empty.")]
    public Sprite boxSprite;

    private readonly List<Image> _boxes = new List<Image>();

    /// <summary>Old signature - shows the combo at level 1 with no progress.</summary>
    public void SetData(FlavorComboRule rule) => SetData(rule, null);

    public void SetData(FlavorComboRule rule, SodaScoringManager.ComboProgress progress)
    {
        int level = progress != null ? progress.level : 1;
        int uses = progress != null ? progress.uses : 0;
        bool maxed = rule.IsMaxLevel(level);

        if (nameText != null)
        {
            bool showInName = appendLevelToName && levelText == null && rule.canLevelUp;
            nameText.text = showInName ? $"{rule.comboName} {string.Format(levelFormat, level)}" : rule.comboName;
        }
        if (descriptionText != null) descriptionText.text = rule.FormatEffectDescription(level);

        if (levelText != null)
        {
            levelText.gameObject.SetActive(rule.canLevelUp);
            levelText.text = maxed && rule.canLevelUp && rule.maxLevel > 0
                ? $"{string.Format(levelFormat, level)} {maxLevelLabel}"
                : string.Format(levelFormat, level);
        }

        if (progressBoxContainer != null)
        {
            if (maxed)
            {
                progressBoxContainer.gameObject.SetActive(false);
            }
            else
            {
                progressBoxContainer.gameObject.SetActive(true);
                BuildBoxes(rule.UsesRequiredForLevel(level), uses);
            }
        }
    }

    private void BuildBoxes(int total, int filled)
    {
        // A HorizontalLayoutGroup that CONTROLS and force-expands its children makes every box
        // an equal share of the container's full width and height, and re-lays them out
        // automatically whenever the container is resized. Settings are forced every time so a
        // layout group added by hand with different settings can't shrink the boxes.
        var layout = progressBoxContainer.GetComponent<HorizontalLayoutGroup>();
        if (layout == null) layout = progressBoxContainer.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = true;
        layout.spacing = boxSpacing;

        while (_boxes.Count < total)
        {
            var go = new GameObject("LevelBox", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            go.transform.SetParent(progressBoxContainer, false);
            var img = go.GetComponent<Image>();
            img.raycastTarget = false;
            if (boxSprite != null) { img.sprite = boxSprite; img.type = Image.Type.Sliced; }

            // Equal flexible share, no minimum - so any number of boxes always fits exactly.
            var le = go.GetComponent<LayoutElement>();
            le.minWidth = 0f;
            le.preferredWidth = 0f;
            le.flexibleWidth = 1f;
            le.flexibleHeight = 1f;

            _boxes.Add(img);
        }

        for (int i = 0; i < _boxes.Count; i++)
        {
            bool used = i < total;
            _boxes[i].gameObject.SetActive(used); // inactive boxes are ignored by the layout
            if (used) _boxes[i].color = i < filled ? filledBoxColor : emptyBoxColor;
        }

        LayoutRebuilder.ForceRebuildLayoutImmediate(progressBoxContainer);
    }
}