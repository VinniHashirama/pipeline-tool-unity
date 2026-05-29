using UnityEditor;

namespace AntiGravity.PipelineTool.Editor
{
    internal static class PipelineSettings
    {
        private const string KeyApiUrl      = "PipelineTool.ApiBaseUrl";
        private const string KeyAnonKey     = "PipelineTool.SupabaseAnonKey";
        private const string KeyProjectId   = "PipelineTool.ProjectId";
        private const string KeyImportPath  = "PipelineTool.ImportTargetPath";
        private const string KeyApiKeyHeader = "PipelineTool.PipelineApiKey";

        public static string ApiBaseUrl
        {
            get => EditorPrefs.GetString(KeyApiUrl, "http://localhost:3000");
            set => EditorPrefs.SetString(KeyApiUrl, value);
        }

        // Supabase anon key — public by design, safe to store in EditorPrefs.
        // Used as Bearer token; the server validates RLS via this key.
        public static string SupabaseAnonKey
        {
            get => EditorPrefs.GetString(KeyAnonKey, "");
            set => EditorPrefs.SetString(KeyAnonKey, value);
        }

        // Optional: dedicated pipeline API key sent as X-Pipeline-Key header.
        // Requires server-side validation against UNITY_TOOL_API_KEY env var.
        public static string PipelineApiKey
        {
            get => EditorPrefs.GetString(KeyApiKeyHeader, "");
            set => EditorPrefs.SetString(KeyApiKeyHeader, value);
        }

        public static string ProjectId
        {
            get => EditorPrefs.GetString(KeyProjectId, "");
            set => EditorPrefs.SetString(KeyProjectId, value);
        }

        public static string ImportTargetPath
        {
            get => EditorPrefs.GetString(KeyImportPath, "Assets/ImportedAssets");
            set => EditorPrefs.SetString(KeyImportPath, value);
        }
    }
}
