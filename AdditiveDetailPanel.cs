using System.Text;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Subscribes to AdditiveCard's hover/drag events and fills in a detail box.
/// Attach to a UI panel GameObject with the referenced TMP_Text fields as children.
/// Keep the GameObject this script is on always active; toggle visibility via
/// panelRoot (a separate child object) instead.
/// </summary>
public class AdditiveDetailPanel : MonoBehaviour
{
    public GameObject panelRoot;
    public TMP_Text nameText;
    public TMP_Text descriptionText;
    public TMP_Text statsText;
    [Tooltip("Small preview of the additive's icon. Hidden automatically if the additive has no icon set.")]
    public Image iconImage;

    private void Awake()
    {
        Debug.Log($"[AdditiveDetailPanel] Awake on '{name}'. panelRoot={(panelRoot != null ? panelRoot.name : "NULL")}, " +
                  $"nameText={(nameText != null ? "set" : "NULL")}, descriptionText={(descriptionText != null ? "set" : "NULL")}, " +
                  $"statsText={(statsText != null ? "set" : "NULL")}.", this);
    }

    private void OnEnable()
    {
        AdditiveCard.OnCardHoverStart += Show;
        AdditiveCard.OnCardHoverEnd += Hide;
        Debug.Log($"[AdditiveDetailPanel] OnEnable on '{name}' - subscribed to hover/drag events.", this);
    }

    private void OnDisable()
    {
        AdditiveCard.OnCardHoverStart -= Show;
        AdditiveCard.OnCardHoverEnd -= Hide;
        Debug.Log($"[AdditiveDetailPanel] OnDisable on '{name}' - UNSUBSCRIBED from hover/drag events. " +
                  "If this prints unexpectedly during normal play, something is disabling this object.", this);
    }

    private void Start()
    {
        Debug.Log($"[AdditiveDetailPanel] Start on '{name}' - calling Hide() so the panel begins hidden.", this);
        Hide();
    }

    private void Show(AdditiveInstance instance)
    {
        Debug.Log($"[AdditiveDetailPanel] Show() CALLED on '{name}' with data=" +
                  $"{(instance?.template != null ? instance.template.additiveName : "NULL")}.", this);

        var data = instance?.template;
        if (data == null)
        {
            Debug.Log("[AdditiveDetailPanel] Show() bailing - instance/template was null.", this);
            return;
        }
        if (panelRoot == null)
        {
            Debug.Log("[AdditiveDetailPanel] Show() bailing - panelRoot is null.", this);
            return;
        }

        panelRoot.SetActive(true);
        Debug.Log($"[AdditiveDetailPanel] panelRoot '{panelRoot.name}' SetActive(true) called. " +
                  $"activeSelf={panelRoot.activeSelf}, activeInHierarchy={panelRoot.activeInHierarchy}.", this);

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

        if (instance.bonusPoints != 0f)
            sb.AppendLine().Append($"Bonus: {ScoreFormat.Signed(instance.bonusPoints)} Points");
        if (instance.bonusMult != 0f)
            sb.AppendLine().Append($"Bonus: {ScoreFormat.Signed(instance.bonusMult)} Mult");
        if (instance.bonusRetrigger != 0) // always >= 0 in practice (BuffRandomAdditiveRetrigger clamps to +1 minimum), but Signed() is safe either way
            sb.AppendLine().Append($"Bonus: {ScoreFormat.Signed(instance.bonusRetrigger)} Retrigger{(instance.bonusRetrigger > 1 ? "s" : "")}");
        if (instance.extraBuffTargets > 0) // only meaningful on a Buff* card - see BuffRandomBufferTargetCount
            sb.AppendLine().Append($"Bonus: buffs {1 + instance.extraBuffTargets} additives per fire instead of 1");
        if (instance.buffTargetProgress > 0f) // partial progress banked toward the NEXT extra buff target - see AmplifyRandomBuffer
            sb.AppendLine().Append($"Progress: {instance.buffTargetProgress:P0} of the way to its next extra buff target");

        if (data.deleteSelfAfterUse)
        {
            string when = data.deleteSelfOnlyOnce ? "after first use" : "after use";
            string scope = data.deleteSelfOnlyWhenInCup ? " (only when played, not while on the belt)" : "";
            sb.AppendLine().Append($"Deletes itself {when}{scope}");
        }
        if (data.amountChangePerUse > 0f)
            sb.AppendLine().Append($"Grows: +{data.amountChangePerUse:0.#} amount per belt use (currently effective: {ScoreFormat.Signed(instance.EffectiveAmount)})");
        else if (data.amountChangePerUse < 0f)
            sb.AppendLine().Append($"Depreciates: -{-data.amountChangePerUse:0.#} amount per belt use (currently effective: {ScoreFormat.Signed(instance.EffectiveAmount)})");
        if (data.useSeparatePlayedAmountChange)
        {
            sb.AppendLine().Append(Mathf.Approximately(data.amountChangeWhenPlayed, 0f)
                ? "Playing it doesn't change its value further"
                : $"When played instead: {ScoreFormat.Signed(data.amountChangeWhenPlayed)} amount");
        }
        if (data.alsoCreateAdditiveAfterUse && (data.effectType == EffectType.BuffRandomAdditivePoints ||
            data.effectType == EffectType.BuffRandomAdditiveMult || data.effectType == EffectType.BuffRandomAdditiveRetrigger))
        {
            string chance = data.createAdditiveChance < 1f ? $" ({data.createAdditiveChance:P0} chance)" : "";
            sb.AppendLine().Append((data.createRandomAdditive
                ? "Also creates a random additive when used"
                : (data.additiveToAdd != null ? $"Also creates '{data.additiveToAdd.additiveName}' when used" : "Also creates an additive when used"))
                + chance);
        }

        if (statsText != null) statsText.text = sb.ToString();
    }

    private void Hide()
    {
        Debug.Log($"[AdditiveDetailPanel] Hide() CALLED on '{name}'.", this);
        if (panelRoot != null) panelRoot.SetActive(false);
    }

    private string FormatFlavorList(System.Collections.Generic.List<FlavorType> flavors)
    {
        if (flavors == null || flavors.Count == 0) return "(no flavor set)";
        return string.Join("/", flavors);
    }

    private string FormatEffect(AdditiveData data)
    {
        switch (data.effectType)
        {
            case EffectType.FlatPoints: return $"{ScoreFormat.Signed(data.amount)} Points";
            case EffectType.FlatMult:   return $"{ScoreFormat.Signed(data.amount)} Mult";
            case EffectType.XMult:      return $"x{data.amount} Mult";
            case EffectType.RetriggerSelf:
                return data.retriggerPayload == RetriggerPayloadType.Mult
                    ? $"{ScoreFormat.Signed(data.amount)} Mult (each fire)"
                    : $"{ScoreFormat.Signed(data.amount)} Points (each fire)";
            case EffectType.BuffRandomAdditivePoints:
                return $"{ScoreFormat.Signed(data.amount)} Points to a random other additive you own (permanent)";
            case EffectType.BuffRandomAdditiveMult:
                return $"{ScoreFormat.Signed(data.amount)} Mult to a random other additive you own (permanent)";
            case EffectType.BuffRandomAdditiveRetrigger:
                return $"+{Mathf.Max(1, Mathf.RoundToInt(data.amount))} Retrigger(s) to a random other additive you own (permanent)"; // always >= 1, clamped
            case EffectType.BuffRandomBufferTargetCount:
                return data.amount >= 1f
                    ? $"Makes a random other Buff Random Additive card you own permanently buff " +
                      $"{Mathf.FloorToInt(data.amount)} additional additive(s) every time it fires" +
                      (data.amount % 1f != 0f ? $" (+{data.amount % 1f:P0} progress toward one more)" : "")
                    : $"Builds {data.amount:P0} progress per fire toward making a random other Buff Random Additive " +
                      $"card you own permanently buff 1 additional additive";
            case EffectType.PointsPerFlavorOnBelt:
                return $"{ScoreFormat.Signed(data.amount)} Points per {FormatFlavorList(data.scalingFlavors)} additive on the belt";
            case EffectType.MultPerFlavorOnBelt:
                return $"{ScoreFormat.Signed(data.amount)} Mult per {FormatFlavorList(data.scalingFlavors)} additive on the belt";
            case EffectType.XMultPerFlavorOnBelt:
                return $"x{data.amount} Mult per {FormatFlavorList(data.scalingFlavors)} additive on the belt";
            case EffectType.AddAdditiveToDeck:
                return data.additiveToAdd != null
                    ? $"Adds '{data.additiveToAdd.additiveName}' to your deck" + (data.addToDeckOnlyOnce ? " (once)" : "")
                    : "Adds an additive to your deck";
            case EffectType.AddRandomAdditiveToDeck:
                return data.filterByFlavor
                    ? $"Adds a random {data.randomFlavorFilter} additive to your deck"
                    : "Adds a random additive to your deck";
            case EffectType.AddRandomClawToDeck:
                return "Adds a random claw to your deck, weighted, restricted to claws that share a flavor you already own";
            case EffectType.DeleteCreatorThenSelf:
                return $"Deletes a card that creates {data.targetCreatorFlavor} type cards, then deletes itself";
            case EffectType.DeleteRandomUnmodifiedFlavorThenSelf:
                switch (data.deleteFilterMode)
                {
                    case DeleteFilterMode.SpecificFlavor:
                        return $"Deletes a random unmodified {data.targetDeleteFlavor} additive you own, then deletes itself";
                    case DeleteFilterMode.NoFlavor:
                        return "Deletes a random unmodified additive with no flavor of its own, then deletes itself";
                    case DeleteFilterMode.FromPool:
                        return "Deletes a random unmodified additive from a specific pool, then deletes itself";
                    default:
                        return "Deletes a random unmodified additive you own (any flavor), then deletes itself";
                }
            case EffectType.DeleteSpecificAdditiveThenSelf:
                return data.additiveToDelete != null
                    ? $"Deletes '{data.additiveToDelete.additiveName}' if you own one, then deletes itself"
                    : "Deletes a specific additive if you own one, then deletes itself";
            case EffectType.DeletePlayerChosenThenSelf:
                return "Lets you choose an owned additive to delete, then deletes itself";
            case EffectType.AddBeltSizePermanent:
                return $"Permanently adds {Mathf.Max(1, Mathf.RoundToInt(data.amount))} additive slot(s) to the belt each round";
            case EffectType.PassiveOnBelt:
                return data.beltPassivePayload switch
                {
                    BeltPassivePayloadType.Points => $"{ScoreFormat.Signed(data.amount)} Points just by sitting on the belt (no need to mix it in)",
                    BeltPassivePayloadType.Mult => $"{ScoreFormat.Signed(data.amount)} Mult just by sitting on the belt (no need to mix it in)",
                    _ => $"x{data.amount} Mult just by sitting on the belt (no need to mix it in)"
                };
            default: return "";
        }
    }
}