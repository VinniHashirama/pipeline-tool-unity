using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using AntiGravity.PipelineTool.Editor.Models;

namespace AntiGravity.PipelineTool.Editor
{
    public class PipelineImportWindow : EditorWindow
    {
        private enum Tab { Assets, Settings }

        private Tab    _tab = Tab.Assets;
        private bool   _busy;
        private string _status = "";
        private bool   _statusError;

        // Assets tab
        private List<ApprovedAsset> _assets = new();
        private Vector2             _scroll;

        // Login form
        private string _email    = "";
        private string _password = "";

        // Settings (buffered — saved explicitly)
        private ServerEnvironment _serverEnv;
        private string            _customUrl  = "";
        private string            _importPath;

        // Project dropdown
        private ProjectInfo[] _projects       = Array.Empty<ProjectInfo>();
        private int           _projectIndex;
        private bool          _projectsLoaded;

        // ------------------------------------------------------------------ //

        [MenuItem("Pipeline Tool/Import Window")]
        public static void Open()
        {
            var w = GetWindow<PipelineImportWindow>("Pipeline Tool");
            w.minSize = new Vector2(480, 400);
            w.Show();
        }

        private void OnEnable()
        {
            _serverEnv  = PipelineSettings.SelectedEnvironment;
            _customUrl  = PipelineSettings.CustomApiUrl;
            _importPath = PipelineSettings.ImportTargetPath;

            if (PipelineSettings.IsLoggedIn && !_projectsLoaded)
                _ = LoadProjectsAsync();

            // Sessão restaurada: reidrata as permissões, senão a UI ficaria com
            // o cache da última vez (ou liberada, se nunca tiver carregado).
            if (PipelineSettings.IsLoggedIn)
                _ = LoadPermissionsAsync();

            if (PipelineSettings.IsLoggedIn && PipelineSettings.AutoRefreshSyncOnStartup &&
                !string.IsNullOrEmpty(PipelineSettings.ProjectId))
                _ = PipelineSyncStatus.RefreshAsync(PipelineSettings.ProjectId);
        }

        private void OnGUI()
        {
            if (!PipelineSettings.IsLoggedIn)
            {
                DrawLoginScreen();
                return;
            }

            DrawTabBar();
            EditorGUILayout.Space(4);

            if (_tab == Tab.Assets)
                DrawAssetsTab();
            else
                DrawSettingsTab();
        }

        // ------------------------------------------------------------------ //
        // Login screen

        private void DrawLoginScreen()
        {
            GUILayout.Space(40);

            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(24);
            EditorGUILayout.BeginVertical();

            EditorGUILayout.LabelField("Pipeline Tool", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Sign in to access the import tool.", EditorStyles.miniLabel);
            EditorGUILayout.Space(16);

            DrawServerPicker();
            EditorGUILayout.Space(12);

            _email    = EditorGUILayout.TextField("Email", _email);
            _password = EditorGUILayout.PasswordField("Password", _password);

            EditorGUILayout.Space(8);
            DrawStatusBar();
            EditorGUILayout.Space(4);

            GUI.enabled = !_busy;
            if (GUILayout.Button(_busy ? "Signing in…" : "Sign In"))
                _ = LoginAsync();
            GUI.enabled = true;

            EditorGUILayout.EndVertical();
            GUILayout.Space(24);
            EditorGUILayout.EndHorizontal();
        }

        // ------------------------------------------------------------------ //
        // Server picker (login screen + settings tab)

        private static readonly string[] ServerLabels = { "Local  (localhost:3000)", "Production", "Custom" };

        private void DrawServerPicker()
        {
            EditorGUI.BeginChangeCheck();
            var newEnv = (ServerEnvironment)EditorGUILayout.Popup("Server", (int)_serverEnv, ServerLabels);
            if (EditorGUI.EndChangeCheck())
            {
                _serverEnv = newEnv;
                PipelineSettings.SelectedEnvironment = newEnv;
            }

            if (_serverEnv == ServerEnvironment.Custom)
            {
                EditorGUI.BeginChangeCheck();
                _customUrl = EditorGUILayout.TextField("URL", _customUrl);
                if (EditorGUI.EndChangeCheck())
                    PipelineSettings.CustomApiUrl = _customUrl;
            }
            else
            {
                var url = _serverEnv == ServerEnvironment.Production
                    ? PipelineSettings.ProductionApiUrl
                    : PipelineSettings.LocalApiUrl;
                EditorGUI.BeginDisabledGroup(true);
                EditorGUILayout.TextField("URL", url);
                EditorGUI.EndDisabledGroup();
            }
        }

        // ------------------------------------------------------------------ //
        // Tab bar

        private void DrawTabBar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            if (GUILayout.Toggle(_tab == Tab.Assets,   "Assets",   EditorStyles.toolbarButton))
                _tab = Tab.Assets;
            if (GUILayout.Toggle(_tab == Tab.Settings, "Settings", EditorStyles.toolbarButton))
                _tab = Tab.Settings;
            EditorGUILayout.EndHorizontal();
        }

        // ------------------------------------------------------------------ //
        // Assets tab

        private void DrawAssetsTab()
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Approved Assets", EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            GUI.enabled = !_busy;
            if (GUILayout.Button("Sync Status", GUILayout.Width(85)))
                _ = SyncStatusAsync();
            if (GUILayout.Button(_busy ? "Loading…" : "Refresh", GUILayout.Width(70)))
                _ = RefreshAsync();
            GUI.enabled = true;
            EditorGUILayout.EndHorizontal();

            DrawStatusBar();
            EditorGUILayout.Space(4);

            if (_assets.Count == 0 && !_busy)
            {
                var msg = string.IsNullOrEmpty(PipelineSettings.ProjectId)
                    ? "Select a project in Settings, then click Refresh."
                    : "No approved assets for this project.";
                EditorGUILayout.HelpBox(msg, MessageType.Info);
                return;
            }

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            foreach (var asset in _assets)
                DrawAssetRow(asset);
            EditorGUILayout.EndScrollView();
        }

        private void DrawStatusBar()
        {
            if (string.IsNullOrEmpty(_status)) return;
            var prev = GUI.color;
            GUI.color = _statusError ? new Color(1f, 0.4f, 0.4f) : new Color(0.5f, 1f, 0.5f);
            EditorGUILayout.LabelField(_status, EditorStyles.helpBox);
            GUI.color = prev;
        }

        private void DrawAssetRow(ApprovedAsset asset)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();

            EditorGUILayout.BeginVertical();
            EditorGUILayout.LabelField(asset.title, EditorStyles.boldLabel);

            var ver  = asset.latest_version;
            var info = ver != null
                ? $"{asset.asset_type}  ·  {asset.project?.name ?? asset.project_id}  ·  v{ver.version_number}  ·  {ver.file_format}  ·  {FormatBytes(ver.file_size_bytes)}"
                : $"{asset.asset_type}  ·  {asset.project?.name ?? asset.project_id}  ·  no version";
            EditorGUILayout.LabelField(info, EditorStyles.miniLabel);
            EditorGUILayout.EndVertical();

            GUILayout.FlexibleSpace();

            var canImport = PipelineSettings.Can("unity.import");
            GUI.enabled = !_busy && ver != null && canImport;
            if (GUILayout.Button(new GUIContent("Import",
                    canImport ? null : "Sua função não tem permissão para importar neste projeto"),
                    GUILayout.Width(65)))
                _ = ImportAsync(asset);
            GUI.enabled = true;

            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
        }

        // ------------------------------------------------------------------ //
        // Settings tab

        private void DrawSettingsTab()
        {
            // Account
            EditorGUILayout.LabelField("Account", EditorStyles.boldLabel);
            var displayName = !string.IsNullOrEmpty(PipelineSettings.UserFullName)
                ? PipelineSettings.UserFullName
                : PipelineSettings.UserEmail;
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField($"Signed in as:  {displayName}", EditorStyles.miniLabel);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Sign Out", GUILayout.Width(75)))
                SignOut();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(12);

            // Connection
            EditorGUILayout.LabelField("API Connection", EditorStyles.boldLabel);
            DrawServerPicker();

            EditorGUILayout.Space(8);

            // Project
            EditorGUILayout.LabelField("Project", EditorStyles.boldLabel);
            if (_projects.Length == 0)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(
                    _projectsLoaded ? "No projects found for this account." : "Loading projects…",
                    EditorStyles.miniLabel);
                if (_projectsLoaded && GUILayout.Button("Reload", GUILayout.Width(60)))
                    _ = LoadProjectsAsync();
                EditorGUILayout.EndHorizontal();
            }
            else
            {
                var names = new string[_projects.Length];
                for (var i = 0; i < _projects.Length; i++)
                    names[i] = _projects[i].name;

                var newIndex = EditorGUILayout.Popup("Project", _projectIndex, names);
                if (newIndex != _projectIndex)
                {
                    _projectIndex = newIndex;
                    PipelineSettings.ProjectId   = _projects[newIndex].id;
                    PipelineSettings.ProjectName = _projects[newIndex].name;
                    // As permissões são por projeto — trocar de projeto pode mudar a função.
                    _ = LoadPermissionsAsync();
                }
            }

            EditorGUILayout.Space(8);

            // Import path
            EditorGUILayout.LabelField("Import", EditorStyles.boldLabel);
            _importPath = EditorGUILayout.TextField("Target Folder", _importPath);

            EditorGUILayout.Space(8);

            // Sync status
            EditorGUILayout.LabelField("Sync Status", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            var autoRefreshSync = EditorGUILayout.Toggle(
                "Auto-refresh on Editor start", PipelineSettings.AutoRefreshSyncOnStartup);
            if (EditorGUI.EndChangeCheck())
                PipelineSettings.AutoRefreshSyncOnStartup = autoRefreshSync;

            EditorGUILayout.Space(12);
            if (GUILayout.Button("Save Settings"))
            {
                PipelineSettings.ImportTargetPath = _importPath;
                SetStatus("Settings saved.", false);
            }
        }

        // ------------------------------------------------------------------ //
        // Async operations

        private async Task LoginAsync()
        {
            _busy = true;
            SetStatus("Signing in…", false);
            Repaint();

            try
            {
                await PipelineApiClient.LoginAsync(_email, _password);
                _password = ""; // clear from memory immediately
                SetStatus("", false);
                await LoadProjectsAsync();
                await LoadPermissionsAsync();
            }
            catch (Exception ex)
            {
                SetStatus($"Sign in failed: {ex.Message}", true);
            }
            finally
            {
                _busy = false;
                Repaint();
            }
        }

        private async Task LoadPermissionsAsync()
        {
            try
            {
                var perms = await PipelineApiClient.GetPermissionsAsync(PipelineSettings.ProjectId);
                PipelineSettings.StorePermissions(perms?.allowed, perms?.is_admin ?? false);
            }
            catch (Exception ex)
            {
                // Falha aqui não deve travar a janela: o botão segue habilitado e
                // o servidor recusa se for o caso.
                Debug.LogWarning($"[Pipeline Tool] Não foi possível carregar permissões: {ex.Message}");
            }
            finally
            {
                Repaint();
            }
        }

        private async Task LoadProjectsAsync()
        {
            try
            {
                _projects       = await PipelineApiClient.GetUserProjectsAsync();
                _projectsLoaded = true;

                // Restore previously selected project index
                var savedId = PipelineSettings.ProjectId;
                _projectIndex = 0;
                for (var i = 0; i < _projects.Length; i++)
                {
                    if (_projects[i].id == savedId) { _projectIndex = i; break; }
                }

                // If only one project, auto-select it
                if (_projects.Length == 1 && string.IsNullOrEmpty(PipelineSettings.ProjectId))
                {
                    PipelineSettings.ProjectId   = _projects[0].id;
                    PipelineSettings.ProjectName = _projects[0].name;
                }
            }
            catch (Exception ex)
            {
                SetStatus($"Failed to load projects: {ex.Message}", true);
            }
            finally
            {
                Repaint();
            }
        }

        private void SignOut()
        {
            PipelineSettings.ClearSession();
            _assets         = new List<ApprovedAsset>();
            _projects       = Array.Empty<ProjectInfo>();
            _projectsLoaded = false;
            _projectIndex   = 0;
            SetStatus("", false);
            Repaint();
        }

        private async Task RefreshAsync()
        {
            _busy = true;
            SetStatus("Fetching approved assets…", false);

            try
            {
                var projectId = string.IsNullOrEmpty(PipelineSettings.ProjectId)
                    ? null
                    : PipelineSettings.ProjectId;

                var response = await PipelineApiClient.GetApprovedAssetsAsync(projectId);
                _assets = new List<ApprovedAsset>(response.assets ?? Array.Empty<ApprovedAsset>());
                SetStatus($"{_assets.Count} approved asset(s) ready to import.", false);
            }
            catch (Exception ex)
            {
                if (ex.Message.Contains("401"))
                {
                    SignOut();
                    SetStatus("Session expired. Please sign in again.", true);
                }
                else
                {
                    SetStatus($"Refresh failed: {ex.Message}", true);
                }
            }
            finally
            {
                _busy = false;
                Repaint();
            }
        }

        private async Task SyncStatusAsync()
        {
            SetStatus("Checking sync status…", false);
            Repaint();

            try
            {
                await PipelineSyncStatus.RefreshAsync(PipelineSettings.ProjectId);
                SetStatus("Sync status updated.", false);
            }
            catch (Exception ex)
            {
                SetStatus($"Sync status check failed: {ex.Message}", true);
            }
            finally
            {
                Repaint();
            }
        }

        private async Task ImportAsync(ApprovedAsset asset)
        {
            // Defesa em profundidade: o botão já fica desabilitado, mas o método
            // também é alcançável por outro caminho de código.
            if (!PipelineSettings.Can("unity.import"))
            {
                SetStatus("Sua função não tem permissão para importar neste projeto.", true);
                return;
            }

            _busy = true;
            var ver = asset.latest_version;
            SetStatus($"Downloading {ver.file_name}…", false);
            Repaint();

            try
            {
                var localPath = await AssetDownloader.DownloadAsync(
                    asset,
                    progress =>
                    {
                        _status = $"Downloading {ver.file_name}… {progress:P0}";
                        Repaint();
                    });

                var result = await PipelineApiClient.MarkImportedAsync(asset.id, ver.id);

                if (result.success)
                {
                    _assets.RemoveAll(a => a.id == asset.id);
                    SetStatus($"Imported: {Path.GetFileName(localPath)}", false);
                }
                else
                {
                    SetStatus("Server returned success=false. Check the Pipeline Tool dashboard.", true);
                }
            }
            catch (Exception ex)
            {
                SetStatus($"Import failed: {ex.Message}", true);
            }
            finally
            {
                _busy = false;
                Repaint();
            }
        }

        // ------------------------------------------------------------------ //
        // Helpers

        private void SetStatus(string msg, bool isError)
        {
            _status      = msg;
            _statusError = isError;
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes <= 0)          return "—";
            if (bytes < 1024)        return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024f:F1} KB";
            return $"{bytes / (1024f * 1024f):F1} MB";
        }
    }
}
