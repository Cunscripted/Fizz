using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// A single clickable syrup offer entry - simpler than AdditiveCard since ModifierData
/// has no flavors/icon of its own, just a name, description, and a click-to-pick button.
/// Put this on a small prefab with a Button, and TMP_Text fields for name/description.
/// </summary>
public class ModifierOfferButton : MonoBehaviour
{
    public Button button;
    public TMP_Text nameText;
    public TMP_Text descriptionText;

    private ModifierData _data;
    public System.Action<ModifierData> OnPicked;

    private void Awake()
    {
        if (button != null) button.onClick.AddListener(() => OnPicked?.Invoke(_data));
    }

    public void SetData(ModifierData data)
    {
        _data = data;
        if (nameText != null) nameText.text = data.modifierName;
        if (descriptionText != null) descriptionText.text = data.description;
    }
}
