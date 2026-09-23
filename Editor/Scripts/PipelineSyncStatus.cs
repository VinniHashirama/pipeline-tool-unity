using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using AntiGravity.PipelineTool.Editor.Models;

namespace AntiGravity.PipelineTool.Editor
{
    /// <summary>
    /// Draws a sync-status icon over items in the Project window for assets tracked in
    /// PipelineManifest, comparing the locally recorded version/hash against the server's
    /// latest version. All network and disk I/O happens in RefreshAsync, triggered explicitly
    /// (Editor startup + manual button) — never inside the per-repaint paint callback.
    /// </summary>
    [InitializeOnLoad]
    internal static class PipelineSyncStatus
    {
        private static Dictionary<string, int> _latestVersionByAssetId = new();
        private static Dictionary<string, SyncState> _stateByGuid = new();
        private static Dictionary<string, SyncState> _stateByFolderGuid = new();
        private static bool _busy;

        static PipelineSyncStatus()
        {
            EditorApplication.projectWindowItemOnGUI += OnProjectWindowItemGUI;
        }

        public static async Task RefreshAsync(string projectId)
        {
            if (_busy || string.IsNullOrEmpty(projectId)) return;
            _busy = true;

            try
            {
                var response = await PipelineApiClient.GetProjectAssetsForSyncAsync(projectId);

                var latest = new Dictionary<string, int>();
                foreach (var asset in response.assets ?? Array.Empty<ApprovedAsset>())
                    if (asset.latest_version != null)
                        latest[asset.id] = asset.latest_version.version_number;
                _latestVersionByAssetId = latest;

                PipelineManifest.Reload();
                var states = new Dictionary<string, SyncState>();
                foreach (var entry in PipelineManifest.All())
                {
                    var path = AssetDatabase.GUIDToAssetPath(entry.guid);
                    var modifiedLocally = File.Exists(path) &&
                        PipelineManifest.Sha1Hex(File.ReadAllBytes(path)) != entry.local_hash;
                    var outdated = latest.TryGetValue(entry.asset_id, out var latestVersion) &&
                        latestVersion > entry.version_number;

                    states[entry.guid] = modifiedLocally
                        ? SyncState.ModifiedLocally
                        : outdated
                            ? SyncState.Outdated
                            : SyncState.Synced;
                }
                _stateByGuid = states;
                _stateByFolderGuid = BuildFolderStates(states);

                EditorApplication.RepaintProjectWindow();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Hopper] Sync status refresh failed: {ex.Message}");
            }
            finally
            {
                _busy = false;
            }
        }

        // Rolls each tracked asset's state up into every ancestor folder between it and
        // PipelineSettings.ImportTargetPath, keeping the worst state per folder. Runs once
        // per RefreshAsync (on-demand), never during painting.
        private static Dictionary<string, SyncState> BuildFolderStates(Dictionary<string, SyncState> assetStates)
        {
            var folderStates = new Dictionary<string, SyncState>();
            var importRoot = PipelineSettings.ImportTargetPath.Replace("\\", "/").TrimEnd('/');

            foreach (var kvp in assetStates)
            {
                var assetGuid = kvp.Key;
                var state = kvp.Value;
                var path = AssetDatabase.GUIDToAssetPath(assetGuid);
                var dir = Path.GetDirectoryName(path)?.Replace("\\", "/");

                while (!string.IsNullOrEmpty(dir) && (dir == importRoot || dir.StartsWith(importRoot + "/")))
                {
                    var folderGuid = AssetDatabase.AssetPathToGUID(dir);
                    if (!string.IsNullOrEmpty(folderGuid))
                    {
                        folderStates[folderGuid] = folderStates.TryGetValue(folderGuid, out var existing)
                            ? Worse(existing, state)
                            : state;
                    }
                    if (dir == importRoot) break;
                    dir = Path.GetDirectoryName(dir)?.Replace("\\", "/");
                }
            }

            return folderStates;
        }

        private static SyncState Worse(SyncState a, SyncState b) => (SyncState)Math.Max((int)a, (int)b);

        private static void OnProjectWindowItemGUI(string guid, Rect selectionRect)
        {
            if (!_stateByGuid.TryGetValue(guid, out var state) &&
                !_stateByFolderGuid.TryGetValue(guid, out state))
                return;

            var (color, label) = state switch
            {
                SyncState.Outdated => (new Color(1f, 0.65f, 0f), "!"),
                SyncState.ModifiedLocally => (new Color(0.55f, 0.55f, 1f), "M"),
                _ => (new Color(0.3f, 0.85f, 0.4f), "✓"),
            };

            var iconRect = new Rect(selectionRect.xMax - 14, selectionRect.y, 14, 14);
            var prevColor = GUI.color;
            GUI.color = color;
            GUI.Label(iconRect, label, EditorStyles.boldLabel);
            GUI.color = prevColor;
        }
    }
}
