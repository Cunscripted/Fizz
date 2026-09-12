using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Shows every additive currently in the player's owned collection, opened from
/// the pause menu. Reuses AdditiveCard (draggable=false) for each entry so hover
/// details and icon display work identically to gameplay - just not drag/drop.
/// </summary>
public class DeckStatsPanel : MonoBehaviour
{
    public RoundManager roundManager;
    public LerpPanel panel;
    public AdditiveCard statEntryPrefab;
    public RectTransform entryContainer;

    private readonly List<AdditiveCard> _spawned = new List<AdditiveCard>();

    public void Show()
    {
        Populate();
        panel.Show();
    }

    public void Hide()
    {
        panel.Hide();
    }

    private void Populate()
    {
        foreach (var c in _spawned)
            if (c != null) Destroy(c.gameObject);
        _spawned.Clear();

        if (roundManager == null) return;

        foreach (var instance in roundManager.OwnedAdditives)
        {
            var card = Instantiate(statEntryPrefab, entryContainer);
            card.draggable = false;
            card.instance = instance;
            _spawned.Add(card);
        }
    }
}
