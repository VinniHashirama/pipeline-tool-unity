using UnityEditor;

namespace AntiGravity.PipelineTool.Editor
{
    internal static class PipelineSettings
    {
        // Connection
        private const string KeyApiUrl       = "PipelineTool.ApiBaseUrl";
        private const string KeyImportPath   = "PipelineTool.ImportTargetPath";

        // Selected project (set after login via dropdown)
        private const string KeyProjectId    = "PipelineTool.ProjectId";
        private const string KeyProjectName  = "PipelineTool.ProjectName";

        // Auth session
        private const string KeyAccessToken  = "PipelineTool.AccessToken";
        private const string KeyRefreshToken = "PipelineTool.RefreshToken";
        private const string KeyTokenExpiry  = "PipelineTool.TokenExpiry";  // stored as UTC Unix seconds string
        private const string KeyUserId       = "PipelineTool.UserId";
        private const string KeyUserEmail    = "PipelineTool.UserEmail";
        private const string KeyUserFullName = "PipelineTool.UserFullName";

        // Legacy — not shown in UI; kept so the server-side dual-auth (X-Pipeline-Key) still works
        // if the key is pre-configured via EditorPrefs directly.
        private const string KeyPipelineApiKey = "PipelineTool.PipelineApiKey";

        // ------------------------------------------------------------------ //
        // Connection

        public static string ApiBaseUrl
        {
            get => EditorPrefs.GetString(KeyApiUrl, "http://localhost:3000");
            set => EditorPrefs.SetString(KeyApiUrl, value);
        }

        public static string ImportTargetPath
        {
            get => EditorPrefs.GetString(KeyImportPath, "Assets/ImportedAssets");
            set => EditorPrefs.SetString(KeyImportPath, value);
        }

        // ------------------------------------------------------------------ //
        // Project

        public static string ProjectId
        {
            get => EditorPrefs.GetString(KeyProjectId, "");
            set => EditorPrefs.SetString(KeyProjectId, value);
        }

        public static string ProjectName
        {
            get => EditorPrefs.GetString(KeyProjectName, "");
            set => EditorPrefs.SetString(KeyProjectName, value);
        }

        // ------------------------------------------------------------------ //
        // Auth session

        public static string AccessToken
        {
            get => EditorPrefs.GetString(KeyAccessToken, "");
            set => EditorPrefs.SetString(KeyAccessToken, value);
        }

        public static string RefreshToken
        {
            get => EditorPrefs.GetString(KeyRefreshToken, "");
            set => EditorPrefs.SetString(KeyRefreshToken, value);
        }

        // UTC Unix timestamp (seconds) after which the access token should be refreshed
        public static long TokenExpiryUtc
        {
            get => long.TryParse(EditorPrefs.GetString(KeyTokenExpiry, "0"), out var v) ? v : 0;
            set => EditorPrefs.SetString(KeyTokenExpiry, value.ToString());
        }

        public static string UserId
        {
            get => EditorPrefs.GetString(KeyUserId, "");
            set => EditorPrefs.SetString(KeyUserId, value);
        }

        public static string UserEmail
        {
            get => EditorPrefs.GetString(KeyUserEmail, "");
            set => EditorPrefs.SetString(KeyUserEmail, value);
        }

        public static string UserFullName
        {
            get => EditorPrefs.GetString(KeyUserFullName, "");
            set => EditorPrefs.SetString(KeyUserFullName, value);
        }

        public static bool IsLoggedIn => !string.IsNullOrEmpty(AccessToken);

        // Legacy dual-auth fallback
        public static string PipelineApiKey
        {
            get => EditorPrefs.GetString(KeyPipelineApiKey, "");
            set => EditorPrefs.SetString(KeyPipelineApiKey, value);
        }

        // ------------------------------------------------------------------ //

        public static void ClearSession()
        {
            EditorPrefs.DeleteKey(KeyAccessToken);
            EditorPrefs.DeleteKey(KeyRefreshToken);
            EditorPrefs.DeleteKey(KeyTokenExpiry);
            EditorPrefs.DeleteKey(KeyUserId);
            EditorPrefs.DeleteKey(KeyUserEmail);
            EditorPrefs.DeleteKey(KeyUserFullName);
            EditorPrefs.DeleteKey(KeyProjectId);
            EditorPrefs.DeleteKey(KeyProjectName);
        }
    }
}
