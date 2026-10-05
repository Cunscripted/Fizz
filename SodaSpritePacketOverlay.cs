#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Draws a small badge in the corner of every Sprite's thumbnail in the Project window
/// when that sprite is assigned as an AdditiveData's icon (its "packet" artwork) - so you
/// can tell at a glance, while browsing sprites, which ones already have a packet built
/// around them without opening the Soda Editor window or clicking through every asset.
///
/// Note: only covers sprites that are their asset's main object (Sprite Mode = Single, or
/// a standalone .png/.psd import). Individual sub-sprites of a Multiple-mode sprite sheet
/// don't get their own row in the Project window in a way this callback can reliably
/// identify, so those aren't badged.
/// </summary>
[InitializeOnLoad]
public static class SodaSpritePacketOverlay
{
    private static readonly HashSet<Sprite> _packetSprites = new HashSet<Sprite>();
    private static bool _dirty = true;

    static SodaSpritePacketOverlay()
    {
        EditorApplication.projectWindowItemOnGUI += OnProjectWindowItemGUI;
    }

    /// <summary>Called by SodaPacketOverlayPostprocessor whenever an AdditiveData (or its icon) might have changed.</summary>
    public static void MarkDirty()
    {
        _dirty = true;
        EditorApplication.RepaintProjectWindow();
    }

    [MenuItem("Soda/Refresh Packet Sprite Overlay")]
    private static void ForceRefresh()
    {
        MarkDirty();
    }

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
    }

    private static void OnProjectWindowItemGUI(string guid, Rect selectionRect)
    {
        RebuildIfDirty();
        if (_packetSprites.Count == 0) return;

        string path = AssetDatabase.GUIDToAssetPath(guid);
        if (string.IsNullOrEmpty(path)) return;

        var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
        if (sprite == null || !_packetSprites.Contains(sprite)) return;

        DrawBadge(selectionRect);
    }

    private static void DrawBadge(Rect selectionRect)
    {
        var badgeIcon = EditorGUIUtility.ObjectContent(null, typeof(AdditiveData)).image;
        if (badgeIcon == null) return;

        // The Project window shows two shapes: a slim list row (small thumbnail on the
        // left) and a big-icon grid tile (thumbnail fills the whole rect). Corner-place
        // the badge against whichever thumbnail area actually applies.
        bool isListRow = selectionRect.height <= 20f;
        Rect badgeRect;

        if (isListRow)
        {
            const float size = 11f;
            // List-row thumbnails sit in a fixed ~16px column on the left.
            badgeRect = new Rect(selectionRect.x + 16f - size, selectionRect.y + selectionRect.height - size, size, size);
        }
        else
        {
            float size = Mathf.Clamp(selectionRect.width * 0.3f, 10f, 20f);
            badgeRect = new Rect(selectionRect.xMax - size - 1f, selectionRect.yMax - size - 1f, size, size);
        }

        // Faint backing square so the badge reads on both light and dark thumbnails.
        EditorGUI.DrawRect(badgeRect, new Color(0f, 0f, 0f, 0.55f));
        GUI.DrawTexture(new Rect(badgeRect.x + 1f, badgeRect.y + 1f, badgeRect.width - 2f, badgeRect.height - 2f),
            badgeIcon, ScaleMode.ScaleToFit);
    }
}

/// <summary>Invalidates the packet-sprite overlay cache whenever an AdditiveData asset is touched.</summary>
public class SodaPacketOverlayPostprocessor : AssetPostprocessor
{
    private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
    {
        foreach (var path in imported)
        {
            if (path.EndsWith(".asset") && AssetDatabase.LoadAssetAtPath<AdditiveData>(path) != null)
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
