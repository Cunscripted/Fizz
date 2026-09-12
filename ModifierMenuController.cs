using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Listens for RoundManager's syrup (modifier) offers, spawns a ModifierOfferButton
/// per offer into offerContainer, and shows/hides panel via LerpPanel. Picking one
/// (or calling Skip()) tells RoundManager to continue.
/// </summary>
public class ModifierMenuController : MonoBehaviour
{
    public RoundManager roundManager;
    public LerpPanel panel;
    public ModifierOfferButton offerButtonPrefab;
    public RectTransform offerContainer;

    private readonly List<ModifierOfferButton> _spawned = new List<ModifierOfferButton>();

    private void OnEnable()
    {
        if (roundManager != null)
            roundManager.OnModifierOffersReady.AddListener(ShowOffers);
    }

    private void OnDisable()
    {
        if (roundManager != null)
            roundManager.OnModifierOffersReady.RemoveListener(ShowOffers);
    }

    private void ShowOffers(List<ModifierData> offers)
    {
        ClearSpawned();

        foreach (var data in offers)
        {
            var entry = Instantiate(offerButtonPrefab, offerContainer);
            entry.SetData(data);
            entry.OnPicked += Pick;
            _spawned.Add(entry);
        }

        panel.Show();
    }

    private void Pick(ModifierData chosen)
    {
        panel.Hide();
        roundManager.OnModifierChosen(chosen);
    }

    /// <summary>Call from a "skip"/"no thanks" button if you want to offer that.</summary>
    public void Skip()
    {
        panel.Hide();
        roundManager.OnModifierChosen(null);
    }

    private void ClearSpawned()
    {
        foreach (var e in _spawned)
            if (e != null) Destroy(e.gameObject);
        _spawned.Clear();
    }
}
