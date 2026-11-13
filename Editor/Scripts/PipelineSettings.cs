using System;
using UnityEditor;
using UnityEditor.PackageManager;

namespace AntiGravity.PipelineTool.Editor
{
    internal enum ServerEnvironment { Local, Production, Custom }

    /// <summary>
    /// Two kinds of settings, kept apart on purpose (v0.8):
    ///
    /// - **Shared with the team (Git):** Hopper project and Target Folder. They live in
    ///   ProjectSettings/HopperSettings.json (HopperProjectSettings) and are only proxied here,
    ///   so callers keep reading PipelineSettings.ProjectId / ImportTargetPath.
    /// - **This machine only (EditorPrefs):** session tokens, user, permission cache, the
    ///   auto-refresh preference and the dev server override. EditorPrefs is per machine and
    ///   shared by every Unity project on it — nothing that belongs to one game goes here.
    ///
    /// The server is production, fixed in code. Local/Custom exist only when the package is
    /// installed from disk (file:), for internal testing.
    /// </summary>
    internal static class PipelineSettings
    {
        internal const string LocalApiUrl      = "http://localhost:3000";
        internal const string ProductionApiUrl = "https://pipeline-tool-web.vercel.app";

        // Dev server override — read only on a local (file:) install.
        private const string KeyServerEnv    = "PipelineTool.ServerEnvironment";
        private const string KeyCustomApiUrl = "PipelineTool.CustomApiUrl";

        private const string KeyAutoRefreshSync = "PipelineTool.AutoRefreshSyncOnStartup";

        // v0.7 kept these per machine. Read only to offer the migration (LegacyProjectSettings).
        private const string KeyLegacyProjectId   = "PipelineTool.ProjectId";
        private const string KeyLegacyProjectName = "PipelineTool.ProjectName";
        private const string KeyLegacyImportPath  = "PipelineTool.ImportTargetPath";

        // Auth session
        private const string KeyAccessToken  = "PipelineTool.AccessToken";
        private const string KeyRefreshToken = "PipelineTool.RefreshToken";
        private const string KeyTokenExpiry  = "PipelineTool.TokenExpiry";  // stored as UTC Unix seconds string
        private const string KeyUserId       = "PipelineTool.UserId";
        private const string KeyUserEmail    = "PipelineTool.UserEmail";
        private const string KeyUserFullName = "PipelineTool.UserFullName";

        // Permissões efetivas no projeto, separadas por vírgula, e de qual projeto vieram.
        // Cache de conveniência para a UI; o servidor revalida em toda chamada.
        private const string KeyPermissions        = "PipelineTool.Permissions";
        private const string KeyPermissionsProject = "PipelineTool.PermissionsProject";
        private const string KeyIsAdmin            = "PipelineTool.IsAdmin";
        private const string KeyPermissionsLoaded  = "PipelineTool.PermissionsLoaded";

        // Legacy — not shown in UI; kept so the server-side dual-auth (X-Pipeline-Key) still works
        // if the key is pre-configured via EditorPrefs directly. A secret: never in the shared file.
        private const string KeyPipelineApiKey = "PipelineTool.PipelineApiKey";

        // ------------------------------------------------------------------ //
        // Connection

        private static bool? _isDevInstall;

        /// <summary>
        /// The package is installed from disk (file: in manifest.json) — the internal dev setup.
        /// A Git URL install never sees the server override, even if the EditorPrefs key exists.
        /// </summary>
        public static bool IsDevInstall
        {
            get
            {
                if (_isDevInstall == null)
                {
                    var info = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(PipelineSettings).Assembly);
                    _isDevInstall = info != null && info.source == PackageSource.Local;
                }
                return _isDevInstall.Value;
            }
        }

        /// <summary>The dev override. Production on any install that is not local.</summary>
        public static ServerEnvironment SelectedEnvironment
        {
            get => IsDevInstall
                ? (ServerEnvironment)EditorPrefs.GetInt(KeyServerEnv, (int)ServerEnvironment.Production)
                : ServerEnvironment.Production;
            set { if (IsDevInstall) EditorPrefs.SetInt(KeyServerEnv, (int)value); }
        }

        public static string CustomApiUrl
        {
            get => EditorPrefs.GetString(KeyCustomApiUrl, "");
            set => EditorPrefs.SetString(KeyCustomApiUrl, value);
        }

        /// <summary>A dev install is pointing somewhere other than production.</summary>
        public static bool IsDevServerActive => SelectedEnvironment != ServerEnvironment.Production;

        public static string ApiBaseUrl
        {
            get
            {
                switch (SelectedEnvironment)
                {
                    case ServerEnvironment.Local:  return LocalApiUrl;
                    case ServerEnvironment.Custom: return CustomApiUrl;
                    default:                       return ProductionApiUrl;
                }
            }
        }

        public static bool AutoRefreshSyncOnStartup
        {
            get => EditorPrefs.GetBool(KeyAutoRefreshSync, true);
            set => EditorPrefs.SetBool(KeyAutoRefreshSync, value);
        }

        // ------------------------------------------------------------------ //
        // Shared with the team — ProjectSettings/HopperSettings.json

        public static string ProjectId   => HopperProjectSettings.ProjectId;
        public static string ProjectName => HopperProjectSettings.ProjectName;

        public static string ImportTargetPath => HopperProjectSettings.ImportTargetPath;

        // ------------------------------------------------------------------ //
        // Migration from v0.7 (per-machine project and Target Folder)

        /// <summary>
        /// What v0.7 left in this machine's EditorPrefs, when this Unity project has no shared
        /// file yet. Null otherwise. Never applied on its own: EditorPrefs is shared by every
        /// Unity project on the machine, so the values may belong to another game.
        /// </summary>
        public static (string id, string name, string target)? LegacyProjectSettings
        {
            get
            {
                if (HopperProjectSettings.Exists) return null;
                var id = EditorPrefs.GetString(KeyLegacyProjectId, "");
                if (string.IsNullOrEmpty(id)) return null;
                return (id,
                        EditorPrefs.GetString(KeyLegacyProjectName, ""),
                        EditorPrefs.GetString(KeyLegacyImportPath, HopperProjectSettings.DefaultTargetPath));
            }
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

        /// <param name="projectId">The project the permissions were fetched for.</param>
        public static void StorePermissions(string projectId, string[] allowed, bool isAdmin)
        {
            EditorPrefs.SetString(KeyPermissions, allowed == null ? "" : string.Join(",", allowed));
            EditorPrefs.SetString(KeyPermissionsProject, projectId ?? "");
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
        ///
        /// O cache é da máquina, e dois projetos Unity abertos podem estar ligados a
        /// projetos Hopper diferentes: cache de outro projeto conta como "não carregou".
        /// </summary>
        public static bool Can(string action)
        {
            if (IsAdmin) return true;
            if (!EditorPrefs.GetBool(KeyPermissionsLoaded, false)) return true;
            if (EditorPrefs.GetString(KeyPermissionsProject, "") != ProjectId) return true;
            var raw = EditorPrefs.GetString(KeyPermissions, "");
            if (string.IsNullOrEmpty(raw)) return false;
            return Array.IndexOf(raw.Split(','), action) >= 0;
        }

        /// <summary>
        /// Sign out: only what belongs to the person. The project and Target Folder belong to
        /// the Unity project (shared file) and stay.
        /// </summary>
        public static void ClearSession()
        {
            EditorPrefs.DeleteKey(KeyAccessToken);
            EditorPrefs.DeleteKey(KeyRefreshToken);
            EditorPrefs.DeleteKey(KeyTokenExpiry);
            EditorPrefs.DeleteKey(KeyUserId);
            EditorPrefs.DeleteKey(KeyUserEmail);
            EditorPrefs.DeleteKey(KeyUserFullName);
            EditorPrefs.DeleteKey(KeyPermissions);
            EditorPrefs.DeleteKey(KeyPermissionsProject);
            EditorPrefs.DeleteKey(KeyPermissionsLoaded);
            EditorPrefs.DeleteKey(KeyIsAdmin);
        }
    }
}
