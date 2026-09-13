using UnityEngine;
using TMPro;

/// <summary>
/// One row in the active-combos panel - purely informational, no interaction.
/// Put this on a small prefab with two TMP_Text fields.
/// </summary>
public class ActiveComboEntry : MonoBehaviour
{
    public TMP_Text nameText;
    public TMP_Text descriptionText;

    public void SetData(FlavorComboRule rule)
    {
        if (nameText != null) nameText.text = rule.comboName;
        if (descriptionText != null) descriptionText.text = rule.FormatEffectDescription();
    }
}
