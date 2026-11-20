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

    [Header("Primary Flavor")]
    [Tooltip("Where the primary flavor's color comes from. Leave empty to use the built-in default colors.")]
    public FlavorPalette palette;
    [Tooltip("Size of the primary flavor's name, as a percent of the normal text size (100 = same size).")]
    public float primaryFlavorSizePercent = 130f;
    [Tooltip("Also make the primary flavor bold.")]
    public bool primaryFlavorBold = true;

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
        AppendFlavors(sb, data);
        sb.AppendLine();
        sb.Append(FormatEffect(data));
        if (data.baseRetriggerCount > 0)
            sb.Append($" (+{data.baseRetriggerCount} retrigger{(data.baseRetriggerCount > 1 ? "s" : "")})");

        if (instance.hasTimelessSeal)
            sb.AppendLine().Append("<color=#E8D9A8>Timeless Seal</color> - never goes Stale or Overdone, and doesn't add heat");

        var popularity = PopularityManager.Instance;
        var popFlavors = popularity != null ? popularity.GetPopularityFlavors(instance) : null;
        bool hasPopFlavor = popFlavors != null && popFlavors.Count > 0;
        if (hasPopFlavor && PopularityManager.IsCardExempt(instance))
        {
            sb.AppendLine().Append("<color=#A0A0A0>Unaffected by popularity</color>");
        }
        else if (hasPopFlavor)
        {
            float eff = popularity.GetEffectiveness(instance);
            if (eff > 1.001f)
                sb.AppendLine().Append($"<color=#C58CFF>Trending! Payouts x{eff:0.##}</color>");
            else if (eff < 0.999f)
                sb.AppendLine().Append($"<color=#A0A0A0>{popularity.GetDominantStatus(instance)} - payouts x{eff:0.##}</color>");
        }

        if (data.extraPoints != 0f)
            sb.AppendLine().Append($"{ScoreFormat.Signed(data.extraPoints)} Points");
        if (data.extraMult != 0f)
            sb.AppendLine().Append($"{ScoreFormat.Signed(data.extraMult)} Mult");
        if (instance.HasExtraXMult)
            sb.AppendLine().Append($"x{data.extraXMult:0.##} Mult");

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
        // Syrup bonus (BoostAmountGrowth) only applies to cards whose change isn't 0.
        float perUse = data.amountChangePerUse != 0f && ModifierManager.Instance != null
            ? ModifierManager.Instance.ModifyAmountChange(instance, data.amountChangePerUse) : data.amountChangePerUse;
        string bonusNote = !Mathf.Approximately(perUse, data.amountChangePerUse)
            ? $" (base {ScoreFormat.Signed(data.amountChangePerUse)}, changed by syrups)" : "";
        if (data.amountChangePerUse != 0f && perUse > 0f)
            sb.AppendLine().Append($"Grows: +{perUse:0.#} amount per use{bonusNote} (currently effective: {ScoreFormat.Signed(instance.EffectiveAmount)})");
        else if (data.amountChangePerUse != 0f && perUse < 0f)
            sb.AppendLine().Append($"Depreciates: -{-perUse:0.#} amount per use{bonusNote} (currently effective: {ScoreFormat.Signed(instance.EffectiveAmount)})");
        else if (data.amountChangePerUse != 0f)
            sb.AppendLine().Append($"No longer changes per use{bonusNote}");
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

    /// <summary>
    /// Writes the flavor list. A primary flavor goes first, in its palette color and bigger;
    /// the rest follow as normal text (the primary isn't repeated).
    /// </summary>
    private void AppendFlavors(StringBuilder sb, AdditiveData data)
    {
        bool first = true;
        if (data.hasPrimaryFlavor)
        {
            Color c = palette != null ? palette.GetColor(data.primaryFlavor) : FlavorPalette.DefaultColor(data.primaryFlavor);
            string hex = ColorUtility.ToHtmlStringRGB(c);
            string name = primaryFlavorBold ? $"<b>{data.primaryFlavor}</b>" : data.primaryFlavor.ToString();
            sb.Append($"<size={primaryFlavorSizePercent:0}%><color=#{hex}>{name}</color></size>");
            first = false;
        }

        foreach (var f in data.flavors)
        {
            if (data.hasPrimaryFlavor && f == data.primaryFlavor) continue;
            if (!first) sb.Append(", ");
            sb.Append(f);
            first = false;
        }
    }

    private void Hide()
    {
        Debug.Log($"[AdditiveDetailPanel] Hide() CALLED on '{name}'.", this);
        if (panelRoot != null) panelRoot.SetActive(false);
    }

    private static string FormatFlavorList(System.Collections.Generic.List<FlavorType> flavors)
    {
        if (flavors == null || flavors.Count == 0) return "(no flavor set)";
        return string.Join("/", flavors);
    }

    /// <summary>One-line description of an additive's main effect. Also used by the editor's Additive Collection window.</summary>
    public static string FormatEffect(AdditiveData data)
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
            case EffectType.RetriggerAllOtherAdditives:
            {
                int times = Mathf.Max(1, Mathf.RoundToInt(data.amount));
                string heat = data.retriggerAllHeatReduction >= 0.999f ? ", after resetting all flavor heat"
                            : data.retriggerAllHeatReduction > 0f ? $", after cooling all flavors by {data.retriggerAllHeatReduction * 100f:0}%"
                            : "";
                return (times > 1
                    ? $"Retriggers every other additive in the soda {times} times"
                    : "Retriggers every other additive in the soda") + heat;
            }
            case EffectType.AddCupCapacityPermanent:
                return $"Permanently lets the soda hold {Mathf.Max(1, Mathf.RoundToInt(data.amount))} more additive(s)";
            case EffectType.RetriggerSelectedFlavors:
            {
                if (data.retriggerFlavors == null || data.retriggerFlavors.Count == 0)
                    return "Retriggers nothing (no flavors selected)";
                int times = Mathf.Max(1, Mathf.RoundToInt(data.amount));
                string flavorList = FormatFlavorList(data.retriggerFlavors);
                return times > 1
                    ? $"Retriggers every other {flavorList} additive in the soda {times} times"
                    : $"Retriggers every other {flavorList} additive in the soda";
            }
            case EffectType.ScaleWithBottle:
            {
                string per = data.bottleCountSource == BottleCountSource.EmptySlots
                    ? "per empty slot in the soda"
                    : (data.bottleCountIncludesSelf ? "per additive in the soda" : "per other additive in the soda");
                return data.bottlePayload switch
                {
                    BeltPassivePayloadType.Points => $"{ScoreFormat.Signed(data.amount)} Points {per}",
                    BeltPassivePayloadType.Mult => $"{ScoreFormat.Signed(data.amount)} Mult {per}",
                    _ => $"x{data.amount} Mult {per}"
                };
            }
            case EffectType.ApplyTimelessSeal:
            {
                int n = Mathf.Max(1, Mathf.RoundToInt(data.amount));
                string which = data.sealFlavorFilter != null && data.sealFlavorFilter.Count > 0
                    ? $"{FormatFlavorList(data.sealFlavorFilter)} additive" : "additive";
                return n > 1
                    ? $"Puts a Timeless Seal on {n} random unsealed {which}s you own"
                    : $"Puts a Timeless Seal on a random unsealed {which} you own";
            }
            case EffectType.ReduceHeatAtCost:
            {
                string target = data.heatOnlyOneFlavor ? $"{data.heatTargetFlavor} heat" : "all flavor heat";
                string action = data.heatReduction >= 0.999f ? $"Resets {target}" : $"Cools {target} by {data.heatReduction * 100f:0}%";
                string cost = data.heatCostType == HeatCostType.Mult ? "Mult" : "Points";
                return $"{action}, costing {Mathf.Max(0f, data.amount):0.#} {cost} from this soda";
            }
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