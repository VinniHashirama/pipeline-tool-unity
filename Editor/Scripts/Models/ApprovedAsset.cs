using System;

namespace AntiGravity.PipelineTool.Editor.Models
{
    [Serializable]
    public class ApprovedAssetsResponse
    {
        public int count;
        public ApprovedAsset[] assets;
    }

    [Serializable]
    public class ApprovedAsset
    {
        public string id;
        public string title;
        public string asset_type;
        public string status;
        public string project_id;
        public AssetProject project;
        public AssetVersionInfo latest_version;
        public string updated_at;
    }

    [Serializable]
    public class AssetProject
    {
        public string id;
        public string name;
        public string storage_type;
    }

    [Serializable]
    public class AssetVersionInfo
    {
        public string id;
        public int version_number;
        public string file_name;
        public string file_url;
        public string file_format;
        public long file_size_bytes;
        public string storage_type;
        public string uploaded_at;
    }
}
