using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Project-wide list of additives that the editor's "Add All Additives In Project" buttons skip
/// (shop pool, syrup/additive/combo random pools, delete pools). Useful for test cards, cards that
/// should only ever be CREATED by other effects (e.g. Fries from a combo), unfinished work, etc.
///
/// Only one is needed - make it via Soda > Additive Ban List (or Assets > Create > Soda > Additive
/// Ban List). Editor-only in effect: nothing at runtime reads it, so it never changes gameplay by itself.
/// </summary>
[CreateAssetMenu(fileName = "AdditiveBanList", menuName = "Soda/Additive Ban List")]
public class AdditiveBanList : ScriptableObject
{
    [Tooltip("These are never added by an \"Add All Additives In Project\" button. They can still be dragged into " +
             "any pool by hand.")]
    public List<AdditiveData> banned = new List<AdditiveData>();

    public bool IsBanned(AdditiveData additive) => additive != null && banned != null && banned.Contains(additive);
}
