#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Draws one combo reward (ComboEffect) showing only the fields its type actually uses - so a "+3 Mult"
/// effect stays three lines, while e.g. a Scale Per Flavor On Belt effect also shows its flavor list.
/// Applies everywhere a ComboEffect list is drawn (the combo inspector and the Soda editor window).
/// </summary>
[CustomPropertyDrawer(typeof(ComboEffect))]
public class ComboEffectDrawer : PropertyDrawer
{
    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        float height = 0f;
        foreach (var (prop, _) in VisibleFields(property))
            height += EditorGUI.GetPropertyHeight(prop, true) + EditorGUIUtility.standardVerticalSpacing;
        return height;
    }

    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        EditorGUI.BeginProperty(position, label, property);
        var rect = new Rect(position.x, position.y, position.width, 0f);
        foreach (var (prop, content) in VisibleFields(property))
        {
            rect.height = EditorGUI.GetPropertyHeight(prop, true);
            EditorGUI.PropertyField(rect, prop, content, true);
            rect.y += rect.height + EditorGUIUtility.standardVerticalSpacing;
        }
        EditorGUI.EndProperty();
    }

    /// <summary>The fields to show for this effect's type, in order, each with its label.</summary>
    private static IEnumerable<(SerializedProperty, GUIContent)> VisibleFields(SerializedProperty p)
    {
        var typeProp = p.FindPropertyRelative("type");
        var type = (ComboBonusType)typeProp.enumValueIndex;

        yield return (typeProp, new GUIContent("Type"));
        yield return (p.FindPropertyRelative("amount"), new GUIContent(AmountLabel(type, p), AmountTooltip(type)));
        yield return (p.FindPropertyRelative("amountPerLevel"), new GUIContent("Amount Per Level"));

        if (ComboEffect.HasDuration(type))
            yield return (p.FindPropertyRelative("duration"), new GUIContent("Duration"));

        switch (type)
        {
            case ComboBonusType.BoostComboRule:
            case ComboBonusType.RetriggerComboRule:
                yield return (p.FindPropertyRelative("targetComboRule"), new GUIContent("Target Combo (empty = this one)"));
                break;

            case ComboBonusType.RetriggerForFlavor:
            case ComboBonusType.BoostFlavorPoints:
            case ComboBonusType.BoostFlavorMult:
                yield return (p.FindPropertyRelative("targetFlavor"), new GUIContent("Flavor"));
                break;

            case ComboBonusType.ScalePerFlavorOnBelt:
                yield return (p.FindPropertyRelative("scalingFlavors"), new GUIContent("Belt Flavors Counted"));
                yield return (p.FindPropertyRelative("scaleIsMult"), new GUIContent("Give Mult (not Points)"));
                break;

            case ComboBonusType.RemoveAdditiveFromDeck:
                yield return (p.FindPropertyRelative("additiveToRemove"), new GUIContent("Additive To Remove"));
                break;

            case ComboBonusType.RemoveRandomAdditiveFromDeck:
            {
                var filter = p.FindPropertyRelative("filterByFlavor");
                yield return (filter, new GUIContent("Filter By Flavor"));
                if (filter.boolValue) yield return (p.FindPropertyRelative("targetFlavor"), new GUIContent("Flavor"));
                break;
            }

            case ComboBonusType.BoostAmountGrowth:
            {
                var mode = p.FindPropertyRelative("amountGrowthMode");
                yield return (mode, new GUIContent("Growth Mode"));
                if (mode.enumValueIndex == (int)AmountGrowthMode.FlipNegativeToPositive)
                    yield return (p.FindPropertyRelative("flipRate"), new GUIContent("Flip Rate"));
                var filter = p.FindPropertyRelative("filterByFlavor");
                yield return (filter, new GUIContent("Filter By Flavor"));
                if (filter.boolValue) yield return (p.FindPropertyRelative("targetFlavor"), new GUIContent("Flavor"));
                break;
            }

            case ComboBonusType.ReduceHeatPercent:
                yield return (p.FindPropertyRelative("repeatEveryRound"), new GUIContent("Also Every Round After"));
                break;
        }

        yield return (p.FindPropertyRelative("onlyOncePerRun"), new GUIContent("Only Once Per Run"));
    }

    private static string AmountLabel(ComboBonusType type, SerializedProperty p)
    {
        switch (type)
        {
            case ComboBonusType.BonusXMult: return "Multiplier";
            case ComboBonusType.RetriggerMatchingAdditives:
            case ComboBonusType.RetriggerForFlavor: return "Extra Fires";
            case ComboBonusType.RetriggerComboRule: return "Extra Triggers";
            case ComboBonusType.ReduceHeatPercent: return "Heat Removed (0-1)";
            case ComboBonusType.RemoveAdditiveFromDeck:
            case ComboBonusType.RemoveRandomAdditiveFromDeck: return "How Many";
            case ComboBonusType.AddAttempt: return "Attempts";
            case ComboBonusType.AddBeltSize: return "Belt Slots";
            case ComboBonusType.AddCupCapacity: return "Cup Slots";
            case ComboBonusType.ScalePerFlavorOnBelt: return "Amount Per Belt Match";
            case ComboBonusType.BoostAmountGrowth:
                return p.FindPropertyRelative("amountGrowthMode").enumValueIndex == (int)AmountGrowthMode.FlipNegativeToPositive
                    ? "Amount (unused in Flip mode)" : "Extra Change Per Use";
            default: return "Amount";
        }
    }

    private static string AmountTooltip(ComboBonusType type)
    {
        switch (type)
        {
            case ComboBonusType.AddAdditiveToDeck: return "Above 1 adds extra copies on top of the rule's How Many To Add.";
            case ComboBonusType.BoostComboRule: return "Added to the target combo's FIRST effect, permanently.";
            case ComboBonusType.GlobalMult: return "Mult added to every soda for the rest of the run (including this one).";
            case ComboBonusType.BoostFlavorPoints:
            case ComboBonusType.BoostFlavorMult: return "Per cup additive of the flavor, scaled by popularity like the syrup.";
            default: return "Rounded to a whole number for count-style effects (attempts, slots, removals, retriggers).";
        }
    }
}
#endif
