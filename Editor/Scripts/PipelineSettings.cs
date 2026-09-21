using System;
using UnityEditor;

namespace AntiGravity.PipelineTool.Editor
{
    internal enum ServerEnvironment { Local, Production, Custom }

    internal static class PipelineSettings
    {
        // Connection
        private const string KeyServerEnv    = "PipelineTool.ServerEnvironment";
        private const string KeyCustomApiUrl = "PipelineTool.CustomApiUrl";
        private const string KeyImportPath   = "PipelineTool.ImportTargetPath";
        private const string KeyAutoRefreshSync = "PipelineTool.AutoRefreshSyncOnStartup";

        // Update ProductionApiUrl after deploying to Vercel
        internal const string LocalApiUrl      = "http://localhost:3000";
        internal const string ProductionApiUrl = "https://pipeline-tool-web.vercel.app";

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

        // Permissões efetivas no projeto selecionado, separadas por vírgula.
        // Cache de conveniência para a UI; o servidor revalida em toda chamada.
        private const string KeyPermissions  = "PipelineTool.Permissions";
        private const string KeyIsAdmin      = "PipelineTool.IsAdmin";
        private const string KeyPermissionsLoaded = "PipelineTool.PermissionsLoaded";

        // Legacy — not shown in UI; kept so the server-side dual-auth (X-Pipeline-Key) still works
        // if the key is pre-configured via EditorPrefs directly.
        private const string KeyPipelineApiKey = "PipelineTool.PipelineApiKey";

        // ------------------------------------------------------------------ //
        // Connection

        public static ServerEnvironment SelectedEnvironment
        {
            get => (ServerEnvironment)EditorPrefs.GetInt(KeyServerEnv, (int)ServerEnvironment.Local);
            set => EditorPrefs.SetInt(KeyServerEnv, (int)value);
        }

        public static string CustomApiUrl
        {
            get => EditorPrefs.GetString(KeyCustomApiUrl, "");
            set => EditorPrefs.SetString(KeyCustomApiUrl, value);
        }

        public static string ApiBaseUrl
        {
            get
            {
                switch (SelectedEnvironment)
                {
                    case ServerEnvironment.Production: return ProductionApiUrl;
                    case ServerEnvironment.Custom:     return CustomApiUrl;
                    default:                           return LocalApiUrl;
                }
            }
        }

        public static string ImportTargetPath
        {
            get => EditorPrefs.GetString(KeyImportPath, "Assets/ImportedAssets");
            set => EditorPrefs.SetString(KeyImportPath, value);
        }

        public static bool AutoRefreshSyncOnStartup
        {
            get => EditorPrefs.GetBool(KeyAutoRefreshSync, true);
            set => EditorPrefs.SetBool(KeyAutoRefreshSync, value);
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
        // Permissões

        public static bool IsAdmin
        {
            get => EditorPrefs.GetBool(KeyIsAdmin, false);
            set => EditorPrefs.SetBool(KeyIsAdmin, value);
        }

        public static void StorePermissions(string[] allowed, bool isAdmin)
        {
            EditorPrefs.SetString(KeyPermissions, allowed == null ? "" : string.Join(",", allowed));
            EditorPrefs.SetBool(KeyPermissionsLoaded, true);
            IsAdmin = isAdmin;
        }

        /// <summary>
        /// Enquanto as permissões não chegaram do servidor (sessão recém-restaurada,
        /// rede fora), devolve true — a UI não some por falta de dado, e quem nega
        /// de fato é o servidor, em toda chamada.
        ///
        /// A flag separada distingue "ainda não carregou" de "carregou e o usuário
        /// não pode nada": sem ela, quem não tem permissão nenhuma veria tudo liberado.
        /// </summary>
        public static bool Can(string action)
        {
            if (IsAdmin) return true;
            if (!EditorPrefs.GetBool(KeyPermissionsLoaded, false)) return true;
            var raw = EditorPrefs.GetString(KeyPermissions, "");
            if (string.IsNullOrEmpty(raw)) return false;
            return Array.IndexOf(raw.Split(','), action) >= 0;
        }

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
            EditorPrefs.DeleteKey(KeyPermissions);
            EditorPrefs.DeleteKey(KeyPermissionsLoaded);
            EditorPrefs.DeleteKey(KeyIsAdmin);
        }
    }
}
