using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Listens for RoundManager's shop offers, spawns a clickable AdditiveCard
/// (draggable=false) per offer into offerContainer, and shows/hides panel via
/// LerpPanel. Picking a card (or calling Skip()) tells RoundManager to continue.
/// </summary>
public class ShopMenuController : MonoBehaviour
{
    public RoundManager roundManager;
    public LerpPanel panel;
    public AdditiveCard offerCardPrefab;
    public RectTransform offerContainer;

    private readonly List<AdditiveCard> _spawned = new List<AdditiveCard>();

    private void OnEnable()
    {
        if (roundManager != null)
            roundManager.OnShopOffersReady.AddListener(ShowOffers);
    }

    private void OnDisable()
    {
        if (roundManager != null)
            roundManager.OnShopOffersReady.RemoveListener(ShowOffers);
    }

    private void ShowOffers(List<AdditiveData> offers)
    {
        ClearSpawned();

        foreach (var data in offers)
        {
            var card = Instantiate(offerCardPrefab, offerContainer);
            card.draggable = false;
            card.instance = new AdditiveInstance(data);

            var captured = data; // local copy for the closure below
            card.OnCardClicked += _ => Pick(captured);

            _spawned.Add(card);
        }

        panel.Show();
    }

    private void Pick(AdditiveData chosen)
    {
        panel.Hide();
        roundManager.OnShopPickChosen(chosen);
    }

    /// <summary>Call from a "skip"/"no thanks" button if you want to offer that.</summary>
    public void Skip()
    {
        panel.Hide();
        roundManager.OnShopPickChosen(null);
    }

    private void ClearSpawned()
    {
        foreach (var c in _spawned)
            if (c != null) Destroy(c.gameObject);
        _spawned.Clear();
    }
}
