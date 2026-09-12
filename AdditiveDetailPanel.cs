using System.Text;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Subscribes to AdditiveCard's hover/drag events and fills in a detail box.
/// Attach to a UI panel GameObject with the referenced TMP_Text fields as children.
///
/// IMPORTANT: keep the GameObject THIS SCRIPT is on always active. Toggle
/// visibility via panelRoot (a separate child object) instead. If you disable
/// the object this script itself lives on, Unity never calls OnEnable(), the
/// event subscriptions below never happen, and the panel will silently never
/// show - this is the single most common cause of "the detail panel isn't
/// showing up". OnEnable below logs errors/warnings for the other usual
/// culprits (missing references) so a misconfiguration shows up in the
/// Console instead of failing silently.
/// </summary>
public class AdditiveDetailPanel : MonoBehaviour
{
    public GameObject panelRoot;
    public TMP_Text nameText;
    public TMP_Text descriptionText;
    public TMP_Text statsText;
    [Tooltip("Small preview of the additive's icon. Hidden automatically if the additive has no icon set.")]
    public Image iconImage;

    private void OnEnable()
    {
        AdditiveCard.OnCardHoverStart += Show;
        AdditiveCard.OnCardHoverEnd += Hide;

        if (panelRoot == null)
            Debug.LogError($"[AdditiveDetailPanel] '{name}' has no panelRoot assigned - it can never show.", this);
        if (nameText == null)
            Debug.LogWarning($"[AdditiveDetailPanel] '{name}' has no nameText assigned.", this);
        if (descriptionText == null)
            Debug.LogWarning($"[AdditiveDetailPanel] '{name}' has no descriptionText assigned.", this);
        if (statsText == null)
            Debug.LogWarning($"[AdditiveDetailPanel] '{name}' has no statsText assigned.", this);
    }

#if UNITY_EDITOR
    // OnValidate runs in the Editor even when the GameObject is disabled (unlike
    // OnEnable), so it's the one place that can actually catch and warn about the
    // most common failure mode: this script's own object being turned off.
    private void OnValidate()
    {
        if (!gameObject.activeSelf)
        {
            Debug.LogWarning($"[AdditiveDetailPanel] '{name}' GameObject is disabled. OnEnable() will never run, " +
                              "so the hover/drag event subscriptions never happen and this panel will silently " +
                              "never show. Keep this object active at all times - toggle visibility via the " +
                              "separate 'panelRoot' field instead.", this);
        }
    }
#endif

    private void OnDisable()
    {
        AdditiveCard.OnCardHoverStart -= Show;
        AdditiveCard.OnCardHoverEnd -= Hide;
    }

    private void Start()
    {
        Hide();
    }

    private void Show(AdditiveData data)
    {
        if (data == null) return;
        if (panelRoot == null) return; // already flagged in OnEnable

        panelRoot.SetActive(true);
        if (nameText != null) nameText.text = data.additiveName;
        if (descriptionText != null) descriptionText.text = data.description;

        if (iconImage != null)
        {
            iconImage.enabled = data.icon != null;
            if (data.icon != null) iconImage.sprite = data.icon;
        }

        var sb = new StringBuilder();
        sb.Append("Flavors: ");
        for (int i = 0; i < data.flavors.Count; i++)
        {
            sb.Append(data.flavors[i]);
            if (i < data.flavors.Count - 1) sb.Append(", ");
        }
        sb.AppendLine();
        sb.Append(FormatEffect(data));
        if (data.baseRetriggerCount > 0)
            sb.Append($" (+{data.baseRetriggerCount} retrigger{(data.baseRetriggerCount > 1 ? "s" : "")})");

        if (statsText != null) statsText.text = sb.ToString();
    }

    private void Hide()
    {
        if (panelRoot != null) panelRoot.SetActive(false);
    }

    private string FormatEffect(AdditiveData data)
    {
        switch (data.effectType)
        {
            case EffectType.FlatPoints: return $"+{data.amount} Points";
            case EffectType.FlatMult:   return $"+{data.amount} Mult";
            case EffectType.XMult:      return $"x{data.amount} Mult";
            case EffectType.RetriggerSelf:
                return data.retriggerPayload == RetriggerPayloadType.Mult
                    ? $"+{data.amount} Mult (each fire)"
                    : $"+{data.amount} Points (each fire)";
            case EffectType.BuffRandomAdditivePoints:
                return $"+{data.amount} Points to a random other additive you own (permanent)";
            case EffectType.BuffRandomAdditiveMult:
                return $"+{data.amount} Mult to a random other additive you own (permanent)";
            case EffectType.PointsPerFlavorOnBelt:
                return $"+{data.amount} Points per {data.scalingFlavor} additive on the belt";
            case EffectType.MultPerFlavorOnBelt:
                return $"+{data.amount} Mult per {data.scalingFlavor} additive on the belt";
            case EffectType.AddAdditiveToDeck:
                return data.additiveToAdd != null
                    ? $"Adds '{data.additiveToAdd.additiveName}' to your deck" + (data.addToDeckOnlyOnce ? " (once)" : "")
                    : "Adds an additive to your deck";
            default: return "";
        }
    }
}