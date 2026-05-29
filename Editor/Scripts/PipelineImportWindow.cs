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

        private Tab _tab = Tab.Assets;
        private List<ApprovedAsset> _assets = new();
        private bool _busy;
        private string _status = "";
        private bool _statusError;
        private Vector2 _scroll;

        // Settings fields (buffered until Save is clicked)
        private string _apiUrl;
        private string _anonKey;
        private string _pipelineKey;
        private string _projectId;
        private string _importPath;

        [MenuItem("Pipeline Tool/Import Window")]
        public static void Open()
        {
            var w = GetWindow<PipelineImportWindow>("Pipeline Tool");
            w.minSize = new Vector2(480, 380);
            w.Show();
        }

        private void OnEnable()
        {
            _apiUrl      = PipelineSettings.ApiBaseUrl;
            _anonKey     = PipelineSettings.SupabaseAnonKey;
            _pipelineKey = PipelineSettings.PipelineApiKey;
            _projectId   = PipelineSettings.ProjectId;
            _importPath  = PipelineSettings.ImportTargetPath;
        }

        private void OnGUI()
        {
            DrawTabBar();
            EditorGUILayout.Space(4);

            if (_tab == Tab.Assets)
                DrawAssetsTab();
            else
                DrawSettingsTab();
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
            if (GUILayout.Button(_busy ? "Loading…" : "Refresh", GUILayout.Width(70)))
                _ = RefreshAsync();
            GUI.enabled = true;
            EditorGUILayout.EndHorizontal();

            DrawStatusBar();

            EditorGUILayout.Space(4);

            if (_assets.Count == 0 && !_busy)
            {
                EditorGUILayout.HelpBox(
                    "No approved assets. Configure the connection in Settings, then click Refresh.",
                    MessageType.Info);
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

            GUI.enabled = !_busy && ver != null;
            if (GUILayout.Button("Import", GUILayout.Width(65)))
                _ = ImportAsync(asset);
            GUI.enabled = true;

            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
        }

        // ------------------------------------------------------------------ //
        // Settings tab

        private void DrawSettingsTab()
        {
            EditorGUILayout.LabelField("API Connection", EditorStyles.boldLabel);
            _apiUrl      = EditorGUILayout.TextField("API Base URL", _apiUrl);
            _anonKey     = EditorGUILayout.PasswordField("Supabase Anon Key", _anonKey);
            _pipelineKey = EditorGUILayout.PasswordField("Pipeline API Key (X-Pipeline-Key)", _pipelineKey);
            _projectId   = EditorGUILayout.TextField("Project ID (optional)", _projectId);

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Import", EditorStyles.boldLabel);
            _importPath = EditorGUILayout.TextField("Target Folder", _importPath);

            EditorGUILayout.Space(12);
            if (GUILayout.Button("Save Settings"))
            {
                PipelineSettings.ApiBaseUrl      = _apiUrl;
                PipelineSettings.SupabaseAnonKey = _anonKey;
                PipelineSettings.PipelineApiKey  = _pipelineKey;
                PipelineSettings.ProjectId       = _projectId;
                PipelineSettings.ImportTargetPath = _importPath;
                SetStatus("Settings saved.", false);
            }
        }

        // ------------------------------------------------------------------ //
        // Async operations

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
                SetStatus($"Refresh failed: {ex.Message}", true);
            }
            finally
            {
                _busy = false;
                Repaint();
            }
        }

        private async Task ImportAsync(ApprovedAsset asset)
        {
            _busy = true;
            var ver = asset.latest_version;
            SetStatus($"Downloading {ver.file_name}…", false);
            Repaint();

            try
            {
                var localPath = await AssetDownloader.DownloadAsync(
                    ver,
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
            if (bytes <= 0) return "—";
            if (bytes < 1024)        return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024f:F1} KB";
            return $"{bytes / (1024f * 1024f):F1} MB";
        }
    }
}
