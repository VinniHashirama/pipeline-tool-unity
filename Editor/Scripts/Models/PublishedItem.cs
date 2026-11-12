using System;

namespace AntiGravity.PipelineTool.Editor.Models
{
    /// <summary>
    /// Response of GET /api/items/published — the Unity v0.3 feed, one entry per
    /// Item with the published version of each file that goes to the engine.
    /// The server builds engine_path; the plugin only checks it stays inside the
    /// target folder (PathSafety).
    /// </summary>
    [Serializable]
    public class PublishedItemsResponse
    {
        public int count;
        public PublishedItem[] items;
    }

    [Serializable]
    public class PublishedItem
    {
        public string id;
        public string code;          // PR001
        public string name;          // PR001_Chair — frozen, unique in the project
        public string display_name;
        public string item_type;
        public ItemCategory category;
        public string engine_folder; // Props/Industrial/PR001_Chair
        public string status;        // approved | imported
        // v0.4 — relative path (/api/assets/{asset_id}/thumbnail), fetched with the
        // same auth headers. Empty when the Item has none or the server is older.
        public string thumbnail_url;
        public ItemFile[] files;
    }

    [Serializable]
    public class ItemCategory
    {
        public string id;
        public string name;
    }

    [Serializable]
    public class ItemFile
    {
        public string asset_id;
        public string version_id;
        public int version_number;
        public string file_name;
        public string engine_path;   // Props/Industrial/PR001_Chair/SM_PR001_Chair.fbx
        public string file_format;
        public long file_size_bytes;
        public string stage;
        public string kind;
        public string status;        // approved | imported
        public string uploaded_at;
        // v0.6 — only on rigging and animation files (servers before it omit it). JsonUtility
        // fills an absent object with defaults, so "present" means rig_type is not empty.
        public AnimationInfo animation;

        public bool HasAnimation => animation != null && !string.IsNullOrEmpty(animation.rig_type);
    }

    /// <summary>
    /// How to import a rig or an animation clip (v0.6): Humanoid or Generic, loop,
    /// clip name. The values come from Hopper (project settings, the Item's override
    /// and what the upload read from the file).
    /// </summary>
    [Serializable]
    public class AnimationInfo
    {
        public string rig_type;   // humanoid | generic
        public int fps;
        public bool is_clip;      // true = animation file; false = the rig (SK_)
        public bool loop;
        public string clip_name;  // the action: Walk, Idle…
        public int frames;
        // v0.7 — the file's clip map: which clips Unity creates from it (one action,
        // a pack with one clip per take, or ranges cut from one take). Servers before
        // 12/nov omit it; JsonUtility then leaves an empty (or null) array.
        public EngineClip[] clips;

        public bool HasClipMap => is_clip && clips != null && clips.Length > 0;

        /// <summary>
        /// What the clip map says, as one string. Stored in the manifest at import: a map
        /// edited in Hopper (same version) shows as Update and re-applies without downloading.
        /// </summary>
        public string ClipSignature()
        {
            if (!HasClipMap) return "";
            var parts = new string[clips.Length];
            for (var i = 0; i < clips.Length; i++)
            {
                var c = clips[i];
                parts[i] = $"{c.name}|{c.take}|{c.take_index}|{c.first_frame}|{c.last_frame}|{(c.loop ? 1 : 0)}";
            }
            return $"{rig_type}|{fps}#" + string.Join(";", parts);
        }
    }

    /// <summary>
    /// One clip of the map (v0.7). Frames are counted at the Hopper project's fps,
    /// from the start of the take, both ends included. last_frame -1 = the end of the take.
    /// </summary>
    [Serializable]
    public class EngineClip
    {
        public string name;        // the clip name in Unity (Jump_Loop)
        public string take;        // the take in the FBX (Take 001, Jump)
        public int take_index;     // the take's position in the file, if the name does not match
        public int first_frame;
        public int last_frame;
        public bool loop;
    }

    /// <summary>Response of POST /api/items/{id}/mark-imported.</summary>
    [Serializable]
    public class ItemImportResult
    {
        public bool success;
        public string item_id;
        public ItemImportedVersion[] imported;
        public string[] already_imported;
        public string imported_by;
        public string commit_hash;
    }

    [Serializable]
    public class ItemImportedVersion
    {
        public string asset_id;
        public string version_id;
        public int version_number;
    }
}
