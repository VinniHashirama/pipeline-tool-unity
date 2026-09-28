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
        private bool                _showUpToDate;

        // What each Item row offers (v0.4), computed against the manifest once per
        // Refresh/import instead of on every repaint. Cleared → recomputed lazily.
        private enum RowAction { Import, Update, UpToDate }

        private class ItemRowState
        {
            public RowAction Action;
            public string    Subtitle;
            public string    Tooltip;
        }

        private readonly Dictionary<string, ItemRowState> _itemStates = new();

        // Thumbnails (v0.4), keyed by thumbnail_url. A null value is a failed load:
        // placeholder until the next Refresh retries it. _thumbGeneration drops the
        // result of a request that finished after the cache was cleared.
        private readonly Dictionary<string, Texture2D> _thumbs        = new();
        private readonly HashSet<string>               _thumbsLoading = new();
        private string                                 _thumbsProjectId;
        private int                                    _thumbGeneration;

        private const float ThumbSize = 40f;

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

        // Also runs before a domain reload: textures made at runtime are not
        // collected on their own (HideAndDontSave), so they go here.
        private void OnDisable()
        {
            ClearThumbnails();
            HopperStyles.Release();
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
            // Items needing Import/Update first; up-to-date ones fold away below.
            var pending  = new List<PublishedItem>();
            var upToDate = new List<PublishedItem>();
            var anyUpdate = false;
            foreach (var item in _items)
            {
                var state = StateOf(item);
                if (state.Action == RowAction.UpToDate) { upToDate.Add(item); continue; }
                pending.Add(item);
                anyUpdate |= state.Action == RowAction.Update;
            }

            EditorGUILayout.BeginHorizontal();
            var project = string.IsNullOrEmpty(PipelineSettings.ProjectName) ? "Hopper" : PipelineSettings.ProjectName;
            GUILayout.Label($"{project}  ·  {pending.Count + _assets.Count} to import", HopperStyles.Header);
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

            if (_assets.Count == 0 && _items.Count == 0 && !_busy)
            {
                var msg = string.IsNullOrEmpty(PipelineSettings.ProjectId)
                    ? "Select a project in Settings, then click Refresh."
                    : "No approved assets for this project.";
                EditorGUILayout.HelpBox(msg, MessageType.Info);
                return;
            }

            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            DrawItemCard(pending);

            if (anyUpdate)
                GUILayout.Label(
                    "Update replaces the file in place: scene and prefab references keep working.",
                    HopperStyles.Note);

            if (upToDate.Count > 0)
            {
                _showUpToDate = EditorGUILayout.Foldout(_showUpToDate, $"Up to date ({upToDate.Count})", true);
                if (_showUpToDate)
                    DrawItemCard(upToDate);
            }

            if (_assets.Count > 0)
            {
                EditorGUILayout.Space(4);
                EditorGUILayout.LabelField("Assets", EditorStyles.miniBoldLabel);
                EditorGUILayout.BeginVertical(HopperStyles.Card);
                for (var i = 0; i < _assets.Count; i++)
                {
                    if (i > 0) DrawSeparator();
                    DrawAssetRow(_assets[i]);
                }
                EditorGUILayout.EndVertical();
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawItemCard(List<PublishedItem> items)
        {
            if (items.Count == 0) return;
            EditorGUILayout.BeginVertical(HopperStyles.Card);
            for (var i = 0; i < items.Count; i++)
            {
                if (i > 0) DrawSeparator();
                DrawItemRow(items[i]);
            }
            EditorGUILayout.EndVertical();
        }

        private static void DrawSeparator()
        {
            var rect = GUILayoutUtility.GetRect(1f, 1f, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(rect, HopperStyles.Separator);
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
            var state = StateOf(item);
            var type  = HopperStyles.Type(item.item_type);

            BeginRow();
            DrawThumbnail(item.thumbnail_url, type);
            DrawRowText(type, item.name, state.Subtitle, state.Tooltip);

            if (state.Action == RowAction.UpToDate)
            {
                EditorGUILayout.BeginVertical(GUILayout.Width(80));
                GUILayout.Space(9);
                GUI.enabled = false;
                GUILayout.Button("✓ Imported", GUILayout.Width(80), GUILayout.Height(22));
                GUI.enabled = true;
                EditorGUILayout.EndVertical();
            }
            else
            {
                // Both go through ImportItemAsync: moves first, then files written
                // in place at engine_path (same GUID, so references survive).
                var label = state.Action == RowAction.Update ? "Update" : "Import";
                if (DrawActionButton(label, files.Length > 0))
                    _ = ImportItemAsync(item);
            }
            EndRow();
        }

        private void DrawAssetRow(ApprovedAsset asset)
        {
            var ver  = asset.latest_version;
            var type = HopperStyles.Type(asset.asset_type);
            var subtitle = ver != null
                ? $"{type.Label}  ·  v{ver.version_number}  ·  {ver.file_format}  ·  {FormatBytes(ver.file_size_bytes)}"
                : $"{type.Label}  ·  no version";
            var tooltip = $"{asset.project?.name ?? asset.project_id}" + (ver != null ? $"\n{ver.file_name}" : "");

            BeginRow();
            DrawThumbnail(null, type);
            DrawRowText(type, asset.title, subtitle, tooltip);
            if (DrawActionButton("Import", ver != null))
                _ = ImportAsync(asset);
            EndRow();
        }

        // ------------------------------------------------------------------ //
        // Row pieces — thumb | badge + name / subtitle | action

        private static void BeginRow()
        {
            EditorGUILayout.BeginHorizontal(GUILayout.MinHeight(ThumbSize + 12));
            GUILayout.Space(8);
            EditorGUILayout.BeginVertical();
            GUILayout.Space(6);
            EditorGUILayout.BeginHorizontal();
        }

        private static void EndRow()
        {
            EditorGUILayout.EndHorizontal();
            GUILayout.Space(6);
            EditorGUILayout.EndVertical();
            GUILayout.Space(8);
            EditorGUILayout.EndHorizontal();
        }

        private static void DrawRowText(TypeInfo type, string name, string subtitle, string tooltip)
        {
            GUILayout.Space(8);
            EditorGUILayout.BeginVertical(GUILayout.ExpandWidth(true));
            GUILayout.Space(3);
            EditorGUILayout.BeginHorizontal();
            var badge   = HopperStyles.Badge(type);
            var content = new GUIContent(type.Label);
            GUILayout.Label(content, badge, GUILayout.Width(badge.CalcSize(content).x));
            GUILayout.Label(new GUIContent(name, tooltip), HopperStyles.Title, GUILayout.MinWidth(40));
            EditorGUILayout.EndHorizontal();
            GUILayout.Label(new GUIContent(subtitle, tooltip), HopperStyles.Subtitle, GUILayout.MinWidth(40));
            EditorGUILayout.EndVertical();
        }

        private bool DrawActionButton(string label, bool available)
        {
            var canImport = PipelineSettings.Can("unity.import");
            EditorGUILayout.BeginVertical(GUILayout.Width(80));
            GUILayout.Space(9);
            GUI.enabled = !_busy && available && canImport;
            var prevBg = GUI.backgroundColor;
            if (GUI.enabled)
                GUI.backgroundColor = label == "Update" ? HopperStyles.Amber : new Color(0.55f, 0.75f, 1f);
            var clicked = GUILayout.Button(
                new GUIContent(label, canImport ? null : "Your role cannot import in this project"),
                GUILayout.Width(80), GUILayout.Height(22));
            GUI.backgroundColor = prevBg;
            GUI.enabled = true;
            EditorGUILayout.EndVertical();
            return clicked;
        }

        private void DrawThumbnail(string url, TypeInfo type)
        {
            var rect = GUILayoutUtility.GetRect(ThumbSize, ThumbSize,
                GUILayout.Width(ThumbSize), GUILayout.Height(ThumbSize));
            var tex = ThumbnailFor(url);
            if (tex != null)
            {
                GUI.DrawTexture(rect, tex, ScaleMode.ScaleAndCrop);
                return;
            }

            EditorGUI.DrawRect(rect, HopperStyles.Placeholder);
            var prev = GUI.contentColor;
            GUI.contentColor = type.Color;
            GUI.Label(rect, type.Glyph, HopperStyles.Glyph);
            GUI.contentColor = prev;
        }

        private void DrawStatusBar()
        {
            if (string.IsNullOrEmpty(_status)) return;
            var prev = GUI.color;
            GUI.color = _statusError ? new Color(1f, 0.4f, 0.4f) : new Color(0.5f, 1f, 0.5f);
            EditorGUILayout.LabelField(_status, EditorStyles.helpBox);
            GUI.color = prev;
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
            _itemStates.Clear();
            ClearThumbnails();
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
                _itemStates.Clear();
                PruneThumbnails(projectId);
                _moves = ItemImporter.PlanMoves(_items);

                var pendingItems = _items.FindAll(i => StateOf(i).Action != RowAction.UpToDate).Count;
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
                // The manifest changed, even on a partial failure: recompute the rows.
                _itemStates.Clear();
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
        // Item row state — Import / Update / up to date, against the local manifest

        private ItemRowState StateOf(PublishedItem item)
        {
            var key = item.id ?? item.name ?? "";
            if (_itemStates.TryGetValue(key, out var cached)) return cached;
            var state = ComputeState(item);
            _itemStates[key] = state;
            return state;
        }

        // Import: this machine has none of the Item's files. Update: it has some,
        // and a file is new to the server's import record or the server published
        // a higher version than the one downloaded. Anything else is up to date.
        private static ItemRowState ComputeState(PublishedItem item)
        {
            var files = item.files ?? Array.Empty<ItemFile>();
            var type  = HopperStyles.Type(item.item_type);
            var known   = 0;
            var changes = new List<string>();
            var lines   = new List<string>();

            foreach (var file in files)
            {
                var entries = PipelineManifest.FindByAssetId(file.asset_id);
                var local = 0;
                foreach (var e in entries) local = Math.Max(local, e.version_number);
                if (entries.Count > 0) known++;

                var behind = entries.Count > 0 && file.version_number > local;
                if (behind || file.status != "imported")
                {
                    changes.Add(entries.Count == 0
                        ? $"{file.file_name} (new)"
                        : local == file.version_number
                            ? $"{file.file_name} v{file.version_number}"
                            : $"{file.file_name} v{local} → v{file.version_number}");
                }
                var mark = entries.Count > 0 && !behind ? "✓" : "•";
                lines.Add($"{mark} {file.file_name}  v{file.version_number}  ·  {FormatBytes(file.file_size_bytes)}");
            }

            var state = new ItemRowState { Tooltip = $"{item.engine_folder}/\n" + string.Join("\n", lines) };
            if (known == 0)
            {
                state.Action   = RowAction.Import;
                var noun       = files.Length == 1 ? "new file" : "new files";
                state.Subtitle = $"{type.Label}  ·  {files.Length} {noun}  ·  {item.engine_folder}/";
            }
            else if (changes.Count > 0)
            {
                state.Action   = RowAction.Update;
                var more       = changes.Count > 1 ? $"  +{changes.Count - 1} more" : "";
                state.Subtitle = $"{type.Label}  ·  {changes[0]}{more}";
            }
            else
            {
                state.Action   = RowAction.UpToDate;
                var noun       = files.Length == 1 ? "file" : "files";
                state.Subtitle = $"{type.Label}  ·  {files.Length} {noun}  ·  {item.engine_folder}/";
            }
            return state;
        }

        // ------------------------------------------------------------------ //
        // Thumbnails — lazy, one request per url, destroyed with the window

        private Texture2D ThumbnailFor(string url)
        {
            if (string.IsNullOrEmpty(url)) return null;
            if (_thumbs.TryGetValue(url, out var tex)) return tex; // null = failed, placeholder
            if (_thumbsLoading.Add(url))
                _ = LoadThumbnailAsync(url, _thumbGeneration);
            return null;
        }

        private async Task LoadThumbnailAsync(string url, int generation)
        {
            var tex = await PipelineApiClient.DownloadTextureAsync(url);
            if (this == null || generation != _thumbGeneration)
            {
                // The window closed or the cache was cleared meanwhile.
                if (tex != null) DestroyImmediate(tex);
                return;
            }
            _thumbsLoading.Remove(url);
            _thumbs[url] = tex;
            if (tex != null) Repaint();
        }

        private void ClearThumbnails()
        {
            foreach (var tex in _thumbs.Values)
                if (tex != null) DestroyImmediate(tex);
            _thumbs.Clear();
            _thumbsLoading.Clear();
            _thumbsProjectId = null;
            _thumbGeneration++;
        }

        // On Refresh: another project drops everything; the same one keeps what
        // loaded and retries what failed.
        private void PruneThumbnails(string projectId)
        {
            if (_thumbsProjectId != projectId)
            {
                ClearThumbnails();
                _thumbsProjectId = projectId;
                return;
            }
            var failed = new List<string>();
            foreach (var pair in _thumbs)
                if (pair.Value == null) failed.Add(pair.Key);
            foreach (var url in failed) _thumbs.Remove(url);
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
