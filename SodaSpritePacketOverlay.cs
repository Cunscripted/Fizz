#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Project window helpers for packet sprites (the sprites used as AdditiveData icons):
///
/// 1. Badges - a small additive icon in the corner of every sprite that's already some additive's icon,
///    and a red ✕ on sprites in the SpriteBanList (sprites you don't plan to make packets for).
///
/// 2. Unpacketed sprites at the top - Unity's Project window always sorts alphabetically and can't be
///    given a custom order, so "Move Unpacketed Sprites To Top" renames every sprite in the chosen folder
///    that has NO packet (and isn't banned) to start with UnpacketedPrefix ("!"), which sorts before letters
///    and numbers. Run it again any time - sprites that have since gained a packet, or been banned, get the
///    prefix taken back off. References to sprites are by GUID, so renaming never breaks an additive's icon.
///
/// 3. Sprite ban list - right-click sprites > Soda > Ban Sprite / Unban Sprite. Separate from the additive
///    shop ban list (AdditiveBanList); this one only affects these Project window helpers.
///
/// Note: badges and sorting cover sprites that are their asset's main object (Sprite Mode = Single). A
/// Multiple-mode sprite sheet counts as "has a packet" if any of its sub-sprites is an icon, and is banned
/// if any of its sub-sprites is banned.
/// </summary>
[InitializeOnLoad]
public static class SodaSpritePacketOverlay
{
    public const string UnpacketedPrefix = "!";
    private const string SpriteBanListPath = "Assets/Soda/SpriteBanList.asset";

    private static readonly HashSet<Sprite> _packetSprites = new HashSet<Sprite>();
    private static readonly HashSet<Sprite> _bannedSprites = new HashSet<Sprite>();
    private static bool _dirty = true;
    private static GUIStyle _banStyle;

    static SodaSpritePacketOverlay()
    {
        EditorApplication.projectWindowItemOnGUI += OnProjectWindowItemGUI;
    }

    /// <summary>Called by SodaPacketOverlayPostprocessor whenever an AdditiveData, its icon, or the ban list might have changed.</summary>
    public static void MarkDirty()
    {
        _dirty = true;
        EditorApplication.RepaintProjectWindow();
    }

    [MenuItem("Soda/Sprite Packets/Refresh Packet Sprite Overlay")]
    private static void ForceRefresh() => MarkDirty();

    private static void RebuildIfDirty()
    {
        if (!_dirty) return;
        _dirty = false;

        _packetSprites.Clear();
        foreach (var guid in AssetDatabase.FindAssets("t:AdditiveData"))
        {
            var data = AssetDatabase.LoadAssetAtPath<AdditiveData>(AssetDatabase.GUIDToAssetPath(guid));
            if (data != null && data.icon != null)
                _packetSprites.Add(data.icon);
        }

        _bannedSprites.Clear();
        var banList = FindSpriteBanList();
        if (banList != null && banList.banned != null)
            foreach (var s in banList.banned)
                if (s != null) _bannedSprites.Add(s);
    }

    // ---------- Badges ----------

    private static void OnProjectWindowItemGUI(string guid, Rect selectionRect)
    {
        RebuildIfDirty();
        if (_packetSprites.Count == 0 && _bannedSprites.Count == 0) return;

        string path = AssetDatabase.GUIDToAssetPath(guid);
        if (string.IsNullOrEmpty(path)) return;

        var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
        if (sprite == null) return;

        if (_bannedSprites.Contains(sprite)) DrawBanBadge(selectionRect);
        else if (_packetSprites.Contains(sprite)) DrawPacketBadge(selectionRect);
    }

    private static Rect BadgeRect(Rect selectionRect)
    {
        // The Project window shows two shapes: a slim list row (small thumbnail on the
        // left) and a big-icon grid tile (thumbnail fills the whole rect). Corner-place
        // the badge against whichever thumbnail area actually applies.
        if (selectionRect.height <= 20f)
        {
            const float size = 11f;
            // List-row thumbnails sit in a fixed ~16px column on the left.
            return new Rect(selectionRect.x + 16f - size, selectionRect.y + selectionRect.height - size, size, size);
        }
        float s = Mathf.Clamp(selectionRect.width * 0.3f, 10f, 20f);
        return new Rect(selectionRect.xMax - s - 1f, selectionRect.yMax - s - 1f, s, s);
    }

    private static void DrawPacketBadge(Rect selectionRect)
    {
        var badgeIcon = EditorGUIUtility.ObjectContent(null, typeof(AdditiveData)).image;
        if (badgeIcon == null) return;

        var badgeRect = BadgeRect(selectionRect);
        // Faint backing square so the badge reads on both light and dark thumbnails.
        EditorGUI.DrawRect(badgeRect, new Color(0f, 0f, 0f, 0.55f));
        GUI.DrawTexture(new Rect(badgeRect.x + 1f, badgeRect.y + 1f, badgeRect.width - 2f, badgeRect.height - 2f),
            badgeIcon, ScaleMode.ScaleToFit);
    }

    private static void DrawBanBadge(Rect selectionRect)
    {
        var r = BadgeRect(selectionRect);
        EditorGUI.DrawRect(r, new Color(0.75f, 0.12f, 0.12f, 0.9f));
        if (_banStyle == null)
            _banStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                padding = new RectOffset(0, 0, 0, 0),
                normal = { textColor = Color.white }
            };
        _banStyle.fontSize = Mathf.Max(8, Mathf.RoundToInt(r.height * 0.8f));
        GUI.Label(r, "✕", _banStyle);
    }

    // ---------- Sprite ban list ----------

    /// <summary>The project's SpriteBanList asset, or null if there isn't one yet.</summary>
    public static SpriteBanList FindSpriteBanList()
    {
        var guid = AssetDatabase.FindAssets("t:SpriteBanList").FirstOrDefault();
        return guid != null ? AssetDatabase.LoadAssetAtPath<SpriteBanList>(AssetDatabase.GUIDToAssetPath(guid)) : null;
    }

    /// <summary>Finds the sprite ban list, creating it at Assets/Soda/SpriteBanList.asset if missing.</summary>
    public static SpriteBanList FindOrCreateSpriteBanList()
    {
        var list = FindSpriteBanList();
        if (list != null) return list;

        if (!AssetDatabase.IsValidFolder("Assets/Soda")) AssetDatabase.CreateFolder("Assets", "Soda");
        list = ScriptableObject.CreateInstance<SpriteBanList>();
        AssetDatabase.CreateAsset(list, SpriteBanListPath);
        AssetDatabase.SaveAssets();
        Debug.Log($"[SpriteBanList] Created {SpriteBanListPath}.", list);
        return list;
    }

    [MenuItem("Soda/Sprite Packets/Open Sprite Ban List")]
    private static void OpenSpriteBanList()
    {
        var list = FindOrCreateSpriteBanList();
        Selection.activeObject = list;
        EditorGUIUtility.PingObject(list);
    }

    /// <summary>Every sprite in the current Project window selection (including sub-sprites of selected sheets).</summary>
    private static List<Sprite> SelectedSprites()
    {
        var result = new List<Sprite>();
        foreach (var obj in Selection.objects)
        {
            if (obj is Sprite s) { result.Add(s); continue; }
            string path = AssetDatabase.GetAssetPath(obj);
            if (string.IsNullOrEmpty(path) || AssetDatabase.IsValidFolder(path)) continue;
            result.AddRange(AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>());
        }
        return result.Distinct().ToList();
    }

    [MenuItem("Assets/Soda/Ban Sprite", false, 2000)]
    [MenuItem("Soda/Sprite Packets/Ban Selected Sprites")]
    private static void BanSelected()
    {
        var sprites = SelectedSprites();
        if (sprites.Count == 0) { Debug.LogWarning("[SpriteBanList] No sprites selected."); return; }

        var list = FindOrCreateSpriteBanList();
        Undo.RecordObject(list, "Ban Sprites");
        int added = 0;
        foreach (var s in sprites)
            if (!list.banned.Contains(s)) { list.banned.Add(s); added++; }
        EditorUtility.SetDirty(list);
        AssetDatabase.SaveAssets();
        MarkDirty();
        Debug.Log($"[SpriteBanList] Banned {added} sprite(s).", list);
    }

    [MenuItem("Assets/Soda/Unban Sprite", false, 2001)]
    [MenuItem("Soda/Sprite Packets/Unban Selected Sprites")]
    private static void UnbanSelected()
    {
        var list = FindSpriteBanList();
        if (list == null) return;
        var sprites = SelectedSprites();

        Undo.RecordObject(list, "Unban Sprites");
        int removed = list.banned.RemoveAll(s => s == null || sprites.Contains(s));
        EditorUtility.SetDirty(list);
        AssetDatabase.SaveAssets();
        MarkDirty();
        Debug.Log($"[SpriteBanList] Unbanned {removed} sprite(s).", list);
    }

    [MenuItem("Assets/Soda/Ban Sprite", true)]
    [MenuItem("Assets/Soda/Unban Sprite", true)]
    [MenuItem("Soda/Sprite Packets/Ban Selected Sprites", true)]
    [MenuItem("Soda/Sprite Packets/Unban Selected Sprites", true)]
    private static bool ValidateSpriteSelection() => SelectedSprites().Count > 0;

    // ---------- Unpacketed sprites to the top ----------

    /// <summary>The folders to act on: selected folders, or the folder holding each selected asset.</summary>
    private static List<string> SelectedFolders()
    {
        var folders = new List<string>();
        foreach (var obj in Selection.GetFiltered<Object>(SelectionMode.Assets))
        {
            string path = AssetDatabase.GetAssetPath(obj);
            if (string.IsNullOrEmpty(path)) continue;
            string folder = AssetDatabase.IsValidFolder(path) ? path : Path.GetDirectoryName(path)?.Replace('\\', '/');
            if (!string.IsNullOrEmpty(folder) && !folders.Contains(folder)) folders.Add(folder);
        }
        return folders;
    }

    [MenuItem("Assets/Soda/Move Unpacketed Sprites To Top", false, 2010)]
    [MenuItem("Soda/Sprite Packets/Move Unpacketed Sprites To Top (Selected Folder)")]
    private static void MoveUnpacketedToTop() => RunOnSelectedFolders(removeAll: false);

    [MenuItem("Assets/Soda/Remove Top Prefixes", false, 2011)]
    [MenuItem("Soda/Sprite Packets/Remove Top Prefixes (Selected Folder)")]
    private static void RemoveTopPrefixes() => RunOnSelectedFolders(removeAll: true);

    [MenuItem("Assets/Soda/Move Unpacketed Sprites To Top", true)]
    [MenuItem("Assets/Soda/Remove Top Prefixes", true)]
    [MenuItem("Soda/Sprite Packets/Move Unpacketed Sprites To Top (Selected Folder)", true)]
    [MenuItem("Soda/Sprite Packets/Remove Top Prefixes (Selected Folder)", true)]
    private static bool ValidateFolderSelection() => SelectedFolders().Count > 0;

    private static void RunOnSelectedFolders(bool removeAll)
    {
        var folders = SelectedFolders();
        if (folders.Count == 0)
        {
            Debug.LogWarning("[SpritePackets] Select a folder (or a sprite inside one) in the Project window first.");
            return;
        }

        _dirty = true;
        RebuildIfDirty();

        int prefixed = 0, unprefixed = 0, skipped = 0;
        AssetDatabase.StartAssetEditing();
        try
        {
            foreach (var folder in folders)
            {
                // Only this folder's own files - not subfolders, so you control exactly which folder gets sorted.
                foreach (var guid in AssetDatabase.FindAssets("t:Sprite", new[] { folder }))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    if (Path.GetDirectoryName(path)?.Replace('\\', '/') != folder) continue;

                    var sprites = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().ToList();
                    if (sprites.Count == 0) continue;

                    bool hasPacket = sprites.Any(_packetSprites.Contains);
                    bool banned = sprites.Any(_bannedSprites.Contains);
                    bool wantPrefix = !removeAll && !hasPacket && !banned;

                    string name = Path.GetFileNameWithoutExtension(path);
                    bool hasPrefix = name.StartsWith(UnpacketedPrefix);
                    if (wantPrefix == hasPrefix) continue;

                    string newName = wantPrefix ? UnpacketedPrefix + name : name.Substring(UnpacketedPrefix.Length);
                    string error = AssetDatabase.RenameAsset(path, newName);
                    if (!string.IsNullOrEmpty(error))
                    {
                        skipped++;
                        Debug.LogWarning($"[SpritePackets] Couldn't rename '{path}' to '{newName}': {error}");
                        continue;
                    }
                    if (wantPrefix) prefixed++; else unprefixed++;
                }
            }
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
            AssetDatabase.Refresh();
        }

        MarkDirty();
        Debug.Log(removeAll
            ? $"[SpritePackets] Removed the '{UnpacketedPrefix}' prefix from {unprefixed} sprite(s)" + (skipped > 0 ? $" ({skipped} couldn't be renamed)." : ".")
            : $"[SpritePackets] Moved {prefixed} unpacketed sprite(s) to the top and put {unprefixed} back in normal order" +
              (skipped > 0 ? $" ({skipped} couldn't be renamed - see warnings)." : "."));
    }
}

/// <summary>Invalidates the packet-sprite overlay cache whenever an AdditiveData asset or the sprite ban list is touched.</summary>
public class SodaPacketOverlayPostprocessor : AssetPostprocessor
{
    private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
    {
        foreach (var path in imported)
        {
            if (!path.EndsWith(".asset")) continue;
            if (AssetDatabase.LoadAssetAtPath<AdditiveData>(path) != null ||
                AssetDatabase.LoadAssetAtPath<SpriteBanList>(path) != null)
            {
                SodaSpritePacketOverlay.MarkDirty();
                return;
            }
        }
        if (deleted.Length > 0 || moved.Length > 0)
            SodaSpritePacketOverlay.MarkDirty();
    }
}
#endif