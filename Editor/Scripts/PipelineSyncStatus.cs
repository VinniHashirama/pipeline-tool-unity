using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using AntiGravity.PipelineTool.Editor.Models;

namespace AntiGravity.PipelineTool.Editor
{
    /// <summary>Counts behind the Scene View indicator and the window's "deleted locally" banner.</summary>
    internal class SyncSummary
    {
        public int Synced, Outdated, Modified, Missing;
        public int New;      // published in Hopper, not in the manifest
        public int Unknown;  // in the manifest, not published by this server (unpublished, or another server)
        public readonly List<string> MissingNames = new();
        public bool Checked;          // a server feed backs Outdated/New/Unknown
        public DateTime CheckedAtUtc;
        public string Error;          // last refresh failure; the counts above stay valid
    }

    /// <summary>
    /// Sync state of every file in the manifest, drawn over the Project window and read
    /// by the Scene View overlay and the Import Window. Two halves:
    ///
    /// - RefreshAsync: network. Fetches what the server publishes (both feeds) and keeps
    ///   it in SessionState, so a domain reload (every script compile) does not ask again.
    /// - Recompute: disk and manifest only. Runs after a refresh, on startup, and whenever
    ///   something changes under the Target Folder (PipelineAssetWatcher) — so deleting a
    ///   file shows as Missing right away, with no network call.
    ///
    /// Nothing happens inside the per-repaint paint callback; it only reads the dictionaries.
    /// Deleting a file is never reported to the server: the manifest (committed in the game
    /// repo) plus the disk is the local truth, imported_at in Hopper stays as history.
    /// </summary>
    [InitializeOnLoad]
    internal static class PipelineSyncStatus
    {
        [Serializable]
        private class FeedFile
        {
            public string asset_id;
            public string version_id;
            public int    version_number;
            public string file_name;
            public string path; // where an import would write it, when the path is valid
        }

        [Serializable]
        private class Feed
        {
            public string project_id;
            public string server;
            public long   checked_at_ticks;
            public List<FeedFile> files = new();
        }

        private const string FeedKey = "Hopper.SyncFeed";

        private static Feed _feed;
        private static string _error;
        private static bool _busy;
        private static Dictionary<string, SyncState> _stateByGuid = new();
        private static Dictionary<string, SyncState> _stateByFolderGuid = new();
        private static List<ManifestEntry> _missing = new();
        private static readonly Dictionary<string, (DateTime mtime, long size, string hash)> _hashes = new();

        public static SyncSummary Summary { get; private set; } = new();
        public static bool IsBusy => _busy;

        /// <summary>Raised after every Recompute and when a refresh starts.</summary>
        public static event Action Changed;

        static PipelineSyncStatus()
        {
            EditorApplication.projectWindowItemOnGUI += OnProjectWindowItemGUI;
            RestoreFeed();
            // AssetDatabase is not ready inside an InitializeOnLoad constructor.
            EditorApplication.delayCall += () =>
            {
                Recompute();
                EnsureChecked();
            };
        }

        /// <summary>Asks the server only when nothing is cached for the current project and server.</summary>
        public static void EnsureChecked()
        {
            if (CurrentFeed() != null || !PipelineSettings.IsLoggedIn || !PipelineSettings.AutoRefreshSyncOnStartup) return;
            if (!string.IsNullOrEmpty(PipelineSettings.ProjectId))
                _ = RefreshAsync(PipelineSettings.ProjectId);
        }

        public static async Task RefreshAsync(string projectId)
        {
            if (_busy || string.IsNullOrEmpty(projectId)) return;
            _busy = true;
            Changed?.Invoke();

            try
            {
                var feed = new Feed
                {
                    project_id       = projectId,
                    server           = PipelineSettings.ApiBaseUrl,
                    checked_at_ticks = DateTime.UtcNow.Ticks,
                };

                var response = await PipelineApiClient.GetProjectAssetsForSyncAsync(projectId);
                foreach (var asset in response.assets ?? Array.Empty<ApprovedAsset>())
                {
                    var v = asset.latest_version;
                    if (v == null) continue;
                    feed.files.Add(new FeedFile
                    {
                        asset_id = asset.id, version_id = v.id, version_number = v.version_number,
                        file_name = v.file_name, path = TryPath(() => AssetDownloader.BuildDestPath(asset)),
                    });
                }

                // Files of Items are not in /approved (v0.3 feed).
                var items = await PipelineApiClient.GetPublishedItemsAsync(projectId);
                foreach (var item in items.items)
                foreach (var file in item.files ?? Array.Empty<ItemFile>())
                {
                    feed.files.Add(new FeedFile
                    {
                        asset_id = file.asset_id, version_id = file.version_id, version_number = file.version_number,
                        file_name = file.file_name,
                        path = TryPath(() => PathSafety.Combine(PipelineSettings.ImportTargetPath, file.engine_path)),
                    });
                }

                _feed  = feed;
                _error = null;
                SessionState.SetString(FeedKey, JsonUtility.ToJson(feed));
            }
            catch (Exception ex)
            {
                _error = Describe(ex);
                Debug.LogWarning($"[Hopper] Sync status refresh failed: {ex.Message}");
            }
            finally
            {
                _busy = false;
                Recompute();
            }
        }

        /// <summary>
        /// States from the manifest, the disk and the last feed — no network. Cheap enough to
        /// run on every asset change: a file is re-hashed only when its size or mtime changed.
        /// </summary>
        public static void Recompute()
        {
            PipelineManifest.Reload();
            var feed = CurrentFeed();
            var published = new Dictionary<string, FeedFile>();
            if (feed != null)
                foreach (var f in feed.files) published[f.asset_id] = f;

            var summary = new SyncSummary
            {
                Checked      = feed != null,
                CheckedAtUtc = feed != null ? new DateTime(feed.checked_at_ticks, DateTimeKind.Utc) : default,
                Error        = _error,
            };
            var states  = new Dictionary<string, SyncState>();
            var anchors = new Dictionary<string, (string path, SyncState state)>(); // guid → where to roll up from
            var missing = new List<ManifestEntry>();
            var present = new HashSet<string>(); // asset_ids with at least one file on disk
            var tracked = new HashSet<string>();
            var pathsChanged = false;

            var entries = new List<ManifestEntry>(PipelineManifest.All());
            foreach (var entry in entries)
            {
                tracked.Add(entry.asset_id);
                if (PipelineManifest.IsPresent(entry)) present.Add(entry.asset_id);
            }

            foreach (var entry in entries)
            {
                if (!PipelineManifest.IsPresent(entry))
                {
                    // A legacy orphan (same asset tracked at a path it no longer uses) is not
                    // a deletion: the asset's file is here, at its other entry.
                    if (present.Contains(entry.asset_id)) continue;
                    missing.Add(entry);
                    summary.Missing++;
                    var known = PathFor(entry);
                    summary.MissingNames.Add(!string.IsNullOrEmpty(known) ? Path.GetFileName(known) : entry.task_title);
                    if (!string.IsNullOrEmpty(known)) anchors[entry.guid] = (known, SyncState.Missing);
                    continue;
                }

                var path = AssetDatabase.GUIDToAssetPath(entry.guid);
                if (!PathSafety.SamePath(entry.local_path, path))
                {
                    entry.local_path = path; // follows moves, for a Reimport later
                    pathsChanged = true;
                }

                var modified = HashOf(path) != entry.local_hash;
                published.TryGetValue(entry.asset_id, out var latest);
                var outdated = latest != null && latest.version_number > entry.version_number;

                if (feed != null && latest == null) summary.Unknown++;

                SyncState state;
                if (modified)      { state = SyncState.ModifiedLocally; summary.Modified++; }
                else if (outdated) { state = SyncState.Outdated;        summary.Outdated++; }
                else if (feed != null && latest == null) continue; // no ✓ for what this server does not know
                else               { state = SyncState.Synced;          summary.Synced++; }

                states[entry.guid]  = state;
                anchors[entry.guid] = (path, state);
            }

            if (feed != null)
                foreach (var f in published.Values)
                    if (!tracked.Contains(f.asset_id)) summary.New++;

            // Mid-import the manifest holds unsaved entries; that import saves them itself.
            if (pathsChanged && !PipelineManifest.HasUnsavedChanges)
                PipelineManifest.Save();

            _stateByGuid       = states;
            _stateByFolderGuid = BuildFolderStates(anchors.Values);
            _missing           = missing;
            Summary            = summary;

            EditorApplication.RepaintProjectWindow();
            Changed?.Invoke();
        }

        /// <summary>A feed for the current project and server is cached.</summary>
        public static bool HasFeed => CurrentFeed() != null;

        /// <summary>The version Hopper publishes for an asset, and where an import would write it.</summary>
        public static bool TryGetPublished(string assetId, out string versionId, out int versionNumber, out string path)
        {
            versionId = null; versionNumber = 0; path = null;
            var feed = CurrentFeed();
            if (feed == null) return false;
            foreach (var f in feed.files)
            {
                if (f.asset_id != assetId) continue;
                versionId = f.version_id; versionNumber = f.version_number; path = f.path;
                return true;
            }
            return false;
        }

        /// <summary>Entries whose file is gone, as of the last Recompute.</summary>
        public static List<ManifestEntry> MissingEntries() => new(_missing);

        /// <summary>
        /// Where an entry's file is or belongs. The manifest's own record first; for assets
        /// by type imported before v0.5 (no local_path), the path an import would use now.
        /// </summary>
        public static string PathFor(ManifestEntry entry)
        {
            var known = PipelineManifest.KnownPath(entry);
            if (!string.IsNullOrEmpty(known)) return known;
            var feed = CurrentFeed();
            if (feed == null) return null;
            foreach (var f in feed.files)
                if (f.asset_id == entry.asset_id && !string.IsNullOrEmpty(f.path)) return f.path;
            return null;
        }

        // ------------------------------------------------------------------ //

        // Only valid for the project and server it was fetched from.
        private static Feed CurrentFeed()
        {
            if (_feed == null) return null;
            return _feed.project_id == PipelineSettings.ProjectId && _feed.server == PipelineSettings.ApiBaseUrl
                ? _feed
                : null;
        }

        private static void RestoreFeed()
        {
            var json = SessionState.GetString(FeedKey, "");
            if (string.IsNullOrEmpty(json)) return;
            try { _feed = JsonUtility.FromJson<Feed>(json); }
            catch (Exception) { _feed = null; }
        }

        private static string TryPath(Func<string> build)
        {
            try { return build(); }
            catch (Exception) { return null; } // PathSafety refused it; the import would refuse too
        }

        private static string Describe(Exception ex)
        {
            var msg = ex.Message ?? "";
            if (msg.StartsWith("HTTP 401", StringComparison.Ordinal)) return "Session expired — sign in again";
            if (msg.StartsWith("HTTP 0", StringComparison.Ordinal))   return "Offline — could not reach Hopper";
            var firstLine = msg.Split('\n')[0];
            return $"Check failed: {firstLine}";
        }

        private static string HashOf(string path)
        {
            try
            {
                var info = new FileInfo(path);
                if (_hashes.TryGetValue(path, out var cached) &&
                    cached.mtime == info.LastWriteTimeUtc && cached.size == info.Length)
                    return cached.hash;
                var hash = PipelineManifest.Sha1Hex(File.ReadAllBytes(path));
                _hashes[path] = (info.LastWriteTimeUtc, info.Length, hash);
                return hash;
            }
            catch (IOException)
            {
                return null; // locked or vanished meanwhile: reads as modified until the next pass
            }
        }

        // Rolls each tracked file's state up into every existing ancestor folder between it
        // and PipelineSettings.ImportTargetPath, keeping the worst state per folder. A deleted
        // file shows on the nearest folder that still exists.
        private static Dictionary<string, SyncState> BuildFolderStates(IEnumerable<(string path, SyncState state)> anchors)
        {
            var folderStates = new Dictionary<string, SyncState>();
            var importRoot = PipelineSettings.ImportTargetPath.Replace("\\", "/").TrimEnd('/');

            foreach (var (path, state) in anchors)
            {
                var dir = Path.GetDirectoryName(path)?.Replace("\\", "/");

                while (!string.IsNullOrEmpty(dir) && (dir == importRoot || dir.StartsWith(importRoot + "/")))
                {
                    if (AssetDatabase.IsValidFolder(dir))
                    {
                        var folderGuid = AssetDatabase.AssetPathToGUID(dir);
                        if (!string.IsNullOrEmpty(folderGuid))
                        {
                            folderStates[folderGuid] = folderStates.TryGetValue(folderGuid, out var existing)
                                ? Worse(existing, state)
                                : state;
                        }
                    }
                    if (dir == importRoot) break;
                    dir = Path.GetDirectoryName(dir)?.Replace("\\", "/");
                }
            }

            return folderStates;
        }

        private static SyncState Worse(SyncState a, SyncState b) => (SyncState)Math.Max((int)a, (int)b);

        public static Color ColorOf(SyncState state) => state switch
        {
            SyncState.Outdated        => new Color(1f, 0.65f, 0f),
            SyncState.ModifiedLocally => new Color(0.55f, 0.55f, 1f),
            SyncState.Missing         => new Color(0.95f, 0.35f, 0.35f),
            _                         => new Color(0.3f, 0.85f, 0.4f),
        };

        private static void OnProjectWindowItemGUI(string guid, Rect selectionRect)
        {
            if (!_stateByGuid.TryGetValue(guid, out var state) &&
                !_stateByFolderGuid.TryGetValue(guid, out state))
                return;

            var label = state switch
            {
                SyncState.Outdated        => "!",
                SyncState.ModifiedLocally => "M",
                // "×" (Latin-1), not "✕": the Editor font has no glyph for the dingbat.
                SyncState.Missing         => "×",
                _                         => "✓",
            };

            var iconRect = new Rect(selectionRect.xMax - 14, selectionRect.y, 14, 14);
            var prevColor = GUI.color;
            GUI.color = ColorOf(state);
            GUI.Label(iconRect, label, EditorStyles.boldLabel);
            GUI.color = prevColor;
        }
    }
}
