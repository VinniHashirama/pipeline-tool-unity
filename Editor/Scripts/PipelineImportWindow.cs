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

        // Items (v0.3). _items has every published Item, imported ones included:
        // the list shows the pending ones, _moves is computed over all of them.
        private List<PublishedItem> _items = new();
        private List<PlannedMove>   _moves = new();
        private bool                _showMoves;

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

        // O ícone entra no pacote junto com a arte final. Até lá, a aba usa o
        // ícone genérico do Editor em vez de quebrar.
        private const string IconPath =
            "Packages/com.antigravity.pipeline-tool/Editor/Icons/hopper-icon.png";

        [MenuItem("Hopper/Import Window")]
        public static void Open()
        {
            var w = GetWindow<PipelineImportWindow>("Hopper");

            var icon = AssetDatabase.LoadAssetAtPath<Texture2D>(IconPath);
            w.titleContent = icon != null
                ? new GUIContent("Hopper", icon)
                : new GUIContent("Hopper");

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

            EditorGUILayout.LabelField("Hopper", EditorStyles.boldLabel);
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

            DrawMovesBanner();

            var pendingItems = _items.FindAll(i => i.status != "imported");
            if (_assets.Count == 0 && pendingItems.Count == 0 && !_busy)
            {
                var msg = string.IsNullOrEmpty(PipelineSettings.ProjectId)
                    ? "Select a project in Settings, then click Refresh."
                    : "No approved assets for this project.";
                EditorGUILayout.HelpBox(msg, MessageType.Info);
                return;
            }

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            if (pendingItems.Count > 0)
            {
                EditorGUILayout.LabelField("Items", EditorStyles.miniBoldLabel);
                foreach (var item in pendingItems)
                    DrawItemRow(item);
                if (_assets.Count > 0)
                {
                    EditorGUILayout.Space(6);
                    EditorGUILayout.LabelField("Assets", EditorStyles.miniBoldLabel);
                }
            }
            foreach (var asset in _assets)
                DrawAssetRow(asset);
            EditorGUILayout.EndScrollView();
        }

        // Files the manifest knows at one path while Hopper says another: the
        // Item changed category, or its folder was dragged by hand. Never moved
        // without a click (D12).
        private void DrawMovesBanner()
        {
            if (_moves.Count == 0) return;

            var itemCount = new HashSet<string>(_moves.ConvertAll(m => m.ItemName)).Count;
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField(
                $"{itemCount} Item(s) changed place in Hopper — {_moves.Count} file(s) to move.",
                EditorStyles.wordWrappedLabel);

            _showMoves = EditorGUILayout.Foldout(_showMoves, "From → To", true);
            if (_showMoves)
            {
                foreach (var move in _moves)
                {
                    EditorGUILayout.LabelField(move.From, EditorStyles.miniLabel);
                    EditorGUILayout.LabelField($"  → {move.To}", EditorStyles.miniLabel);
                }
            }

            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            GUI.enabled = !_busy && PipelineSettings.Can("unity.import");
            if (GUILayout.Button("Move", GUILayout.Width(70)))
                MoveAll();
            GUI.enabled = true;
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(4);
        }

        private void DrawItemRow(PublishedItem item)
        {
            var files = item.files ?? Array.Empty<ItemFile>();
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();

            EditorGUILayout.BeginVertical();
            EditorGUILayout.LabelField(item.name, EditorStyles.boldLabel);
            var pending = Array.FindAll(files, f => f.status != "imported").Length;
            EditorGUILayout.LabelField(
                $"{item.item_type}  ·  {files.Length} file(s), {pending} new  ·  {item.engine_folder}/",
                EditorStyles.miniLabel);
            foreach (var file in files)
            {
                var mark = file.status == "imported" ? "✓" : "•";
                EditorGUILayout.LabelField(
                    $"  {mark} {file.file_name}  v{file.version_number}  ·  {FormatBytes(file.file_size_bytes)}",
                    EditorStyles.miniLabel);
            }
            EditorGUILayout.EndVertical();

            GUILayout.FlexibleSpace();

            var canImport = PipelineSettings.Can("unity.import");
            GUI.enabled = !_busy && files.Length > 0 && canImport;
            if (GUILayout.Button(new GUIContent("Import",
                    canImport ? null : "Your role cannot import in this project"),
                    GUILayout.Width(65)))
                _ = ImportItemAsync(item);
            GUI.enabled = true;

            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
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
                    canImport ? null : "Your role cannot import in this project"),
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
                try
                {
                    PipelineSettings.ImportTargetPath = PathSafety.NormalizeTarget(_importPath);
                    _importPath = PipelineSettings.ImportTargetPath;
                    SetStatus("Settings saved.", false);
                }
                catch (InvalidOperationException ex)
                {
                    SetStatus(ex.Message, true);
                }
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
                Debug.LogWarning($"[Hopper] Could not load permissions: {ex.Message}");
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
            _items          = new List<PublishedItem>();
            _moves          = new List<PlannedMove>();
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

                var items = await PipelineApiClient.GetPublishedItemsAsync(projectId);
                _items = new List<PublishedItem>(items.items);
                PipelineManifest.Reload();
                _moves = ItemImporter.PlanMoves(_items);

                var pendingItems = _items.FindAll(i => i.status != "imported").Count;
                SetStatus(pendingItems > 0
                    ? $"{pendingItems} Item(s) and {_assets.Count} asset(s) ready to import."
                    : $"{_assets.Count} approved asset(s) ready to import.", false);
            }
            catch (InvalidOperationException ex)
            {
                // PathSafety: a path from the server (or the Target Folder) was refused.
                _moves = new List<PlannedMove>();
                SetStatus(ex.Message, true);
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
                SetStatus("Your role cannot import in this project.", true);
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

                var result = await PipelineApiClient.MarkImportedAsync(asset.id, ver.id, GitInfo.HeadCommit());

                if (result.success)
                {
                    _assets.RemoveAll(a => a.id == asset.id);
                    SetStatus($"Imported: {Path.GetFileName(localPath)}", false);
                }
                else
                {
                    SetStatus("Server returned success=false. Check the Hopper dashboard.", true);
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

        private async Task ImportItemAsync(PublishedItem item)
        {
            if (!PipelineSettings.Can("unity.import"))
            {
                SetStatus("Your role cannot import in this project.", true);
                return;
            }

            // A file of this Item still at its old place must move first,
            // otherwise the import would write a second copy at the new path.
            var moves = _moves.FindAll(m => m.ItemName == item.name);
            if (moves.Count > 0)
            {
                if (!ConfirmMoves(moves)) return;
                var errors = ItemImporter.ApplyMoves(moves);
                _moves.RemoveAll(m => moves.Contains(m));
                if (errors.Count > 0)
                {
                    SetStatus($"Could not move: {string.Join(" · ", errors)}", true);
                    Repaint();
                    return;
                }
            }

            _busy = true;
            Repaint();
            try
            {
                var result = await ItemImporter.ImportAsync(item, msg =>
                {
                    _status = msg;
                    _statusError = false;
                    Repaint();
                });

                if (result.success)
                {
                    foreach (var file in item.files ?? Array.Empty<ItemFile>())
                        file.status = "imported";
                    item.status = "imported";
                    SetStatus($"Imported: {item.name} → {item.engine_folder}/. Commit the files, the .meta and the manifest.", false);
                }
                else
                {
                    SetStatus("Server returned success=false. Check the Hopper dashboard.", true);
                }
            }
            catch (Exception ex)
            {
                SetStatus(ex.Message.Contains("409")
                    ? $"{item.name} changed in Hopper since the list was loaded — click Refresh and import again."
                    : $"Import failed: {ex.Message}", true);
            }
            finally
            {
                _busy = false;
                Repaint();
            }
        }

        private void MoveAll()
        {
            if (!ConfirmMoves(_moves)) return;
            var count = _moves.Count;
            var errors = ItemImporter.ApplyMoves(_moves);
            _moves = new List<PlannedMove>();
            SetStatus(errors.Count == 0
                ? $"Moved {count} file(s). Commit the moves (files, .meta and manifest) so other machines get them."
                : $"Could not move: {string.Join(" · ", errors)}", errors.Count > 0);
            Repaint();
        }

        private static bool ConfirmMoves(List<PlannedMove> moves)
        {
            var lines = moves.ConvertAll(m => $"{m.From}\n  → {m.To}");
            var shown = lines.Count > 8 ? lines.GetRange(0, 8) : lines;
            var more  = lines.Count > 8 ? $"\n… and {lines.Count - 8} more" : "";
            return EditorUtility.DisplayDialog(
                "Move files to their new place?",
                "These files are not where Hopper says they belong — the Item changed category, or its folder " +
                "was moved by hand (Hopper is the source of truth for the path). Moving keeps their GUIDs, so scene " +
                "and prefab references survive; code that loads by path (Resources.Load) will break.\n\n" +
                string.Join("\n", shown) + more +
                "\n\nThe move becomes a commit in this repo.",
                "Move", "Cancel");
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
