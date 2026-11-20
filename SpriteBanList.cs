using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Project-wide list of sprites you DON'T intend to build an additive packet around (UI art, backgrounds,
/// unused drafts...). Completely separate from AdditiveBanList - this one never touches the shop or any
/// additive pool.
///
/// What it does (editor only - nothing at runtime reads it):
///   - Banned sprites get a red ✕ badge in the Project window instead of being treated as "missing a packet".
///   - Soda > Sprite Packets > Move Unpacketed Sprites To Top never moves a banned sprite to the top.
///
/// Add/remove sprites by right-clicking them in the Project window > Soda > Ban Sprite / Unban Sprite, or by
/// editing the list directly (Soda > Sprite Packets > Open Sprite Ban List). Only one is needed.
/// </summary>
[CreateAssetMenu(fileName = "SpriteBanList", menuName = "Soda/Sprite Ban List")]
public class SpriteBanList : ScriptableObject
{
    [Tooltip("Sprites that intentionally have no additive packet. They get a red ✕ badge and are never moved to the " +
             "top of their folder by the unpacketed-sprite sort.")]
    public List<Sprite> banned = new List<Sprite>();

    public bool IsBanned(Sprite sprite) => sprite != null && banned != null && banned.Contains(sprite);
}
