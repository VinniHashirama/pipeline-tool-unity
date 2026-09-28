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
