using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using AntiGravity.PipelineTool.Editor.Models;

namespace AntiGravity.PipelineTool.Editor
{
    /// <summary>A file the manifest knows at one path while the server says another.</summary>
    internal class PlannedMove
    {
        public string ItemName;
        public string AssetId;
        public string Guid;
        public string From;
        public string To;
        public string EnginePath;
    }

    /// <summary>
    /// Import by Item (v0.3, D12 of item-architecture.md). The server decides
    /// where each file goes (engine_path); this class checks the path, downloads,
    /// writes, labels and records it in the manifest.
    ///
    /// When a file the manifest knows is somewhere else — the Item changed
    /// category in Hopper, or someone dragged the folder by hand — it is never
    /// moved silently: PlanMoves lists it, the window asks, ApplyMoves moves it
    /// with AssetDatabase.MoveAsset, which keeps the GUID and every scene and
    /// prefab reference. The move reaches other machines through Git (files,
    /// .meta and the manifest), so they do not move again.
    /// </summary>
    internal static class ItemImporter
    {
        public static List<PlannedMove> PlanMoves(IEnumerable<PublishedItem> items)
        {
            var moves = new List<PlannedMove>();
            foreach (var item in items)
            foreach (var file in item.files ?? Array.Empty<ItemFile>())
            {
                var to = PathSafety.Combine(PipelineSettings.ImportTargetPath, file.engine_path);
                foreach (var entry in PipelineManifest.FindByAssetId(file.asset_id))
                {
                    var from = AssetDatabase.GUIDToAssetPath(entry.guid);
                    if (string.IsNullOrEmpty(from) || !File.Exists(from)) continue; // gone: a plain import brings it back
                    if (PathSafety.SamePath(from, to)) continue;
                    moves.Add(new PlannedMove
                    {
                        ItemName = item.name, AssetId = file.asset_id, Guid = entry.guid,
                        From = from, To = to, EnginePath = file.engine_path,
                    });
                }
            }
            return moves;
        }

        /// <summary>Moves the files. Returns one message per file that could not be moved.</summary>
        public static List<string> ApplyMoves(IEnumerable<PlannedMove> moves)
        {
            var errors = new List<string>();
            var emptied = new HashSet<string>();
            // One by one, without StartAssetEditing: CreateFolder inside a batch is not
            // visible to IsValidFolder/MoveAsset until the batch ends. Items have few files.
            foreach (var move in moves)
            {
                if (File.Exists(move.To))
                {
                    errors.Add($"{move.To} already exists — move or delete it, then sync again.");
                    continue;
                }
                EnsureFolder(Path.GetDirectoryName(move.To)?.Replace('\\', '/'));
                var error = AssetDatabase.MoveAsset(move.From, move.To);
                if (!string.IsNullOrEmpty(error))
                {
                    errors.Add($"{move.From}: {error}");
                    continue;
                }
                var entry = PipelineManifest.Get(move.Guid);
                if (entry != null)
                {
                    entry.engine_path = move.EnginePath;
                    PipelineManifest.Set(move.Guid, entry, save: false);
                }
                emptied.Add(Path.GetDirectoryName(move.From)?.Replace('\\', '/'));
            }
            PipelineManifest.Save();
            foreach (var dir in emptied) DeleteEmptyFolders(dir);
            return errors;
        }

        /// <summary>
        /// Downloads and writes every file of the Item that is missing or behind,
        /// then marks on the server the versions not yet imported. Call after
        /// ApplyMoves: a file still at its old path would be written twice.
        /// </summary>
        public static async Task<ItemImportResult> ImportAsync(PublishedItem item, Action<string> onStatus)
        {
            var files = item.files ?? Array.Empty<ItemFile>();
            // Every path is checked before the first download: all or nothing.
            var destinations = files.ToDictionary(f => f.asset_id, f => PathSafety.Combine(PipelineSettings.ImportTargetPath, f.engine_path));

            foreach (var file in files)
            {
                var dest = destinations[file.asset_id];
                var known = PipelineManifest.FindByAssetId(file.asset_id)
                    .FirstOrDefault(e => PathSafety.SamePath(AssetDatabase.GUIDToAssetPath(e.guid), dest));
                if (known != null && known.version_id == file.version_id && File.Exists(dest))
                    continue; // already here, same version

                onStatus?.Invoke($"Downloading {file.file_name}…");
                var data = await PipelineApiClient.DownloadAsync(file.asset_id, file.version_id,
                    p => onStatus?.Invoke($"Downloading {file.file_name}… {p:P0}"));

                var dir = Path.GetDirectoryName(dest);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                await Task.Run(() => File.WriteAllBytes(dest, data));
                AssetDatabase.ImportAsset(dest, ImportAssetOptions.ForceUpdate);

                var guid = AssetDatabase.AssetPathToGUID(dest);
                SetLabels(dest, item);
                // Entries of this asset left at another path (legacy orphans) are dropped:
                // the file the manifest tracks is the one at engine_path.
                foreach (var stale in PipelineManifest.FindByAssetId(file.asset_id))
                    if (stale.guid != guid) PipelineManifest.Remove(stale.guid, save: false);
                PipelineManifest.Set(guid, new ManifestEntry
                {
                    guid           = guid,
                    asset_id       = file.asset_id,
                    version_id     = file.version_id,
                    version_number = file.version_number,
                    task_title     = file.file_name,
                    local_hash     = PipelineManifest.Sha1Hex(data),
                    item_id        = item.id,
                    engine_path    = file.engine_path,
                }, save: false);
            }
            PipelineManifest.Save();

            var toMark = files.Where(f => f.status != "imported").Select(f => f.version_id).ToArray();
            if (toMark.Length == 0)
                return new ItemImportResult { success = true, item_id = item.id, imported = Array.Empty<ItemImportedVersion>() };

            onStatus?.Invoke($"Marking {item.name} as imported…");
            return await PipelineApiClient.MarkItemImportedAsync(item.id, toMark, GitInfo.HeadCommit());
        }

        // Labels are free: type, category and code make the Item searchable in
        // the Project window (l:PR001) without depending on the folder.
        private static void SetLabels(string path, PublishedItem item)
        {
            var asset = AssetDatabase.LoadMainAssetAtPath(path);
            if (asset == null) return;
            var labels = new List<string> { "Hopper", item.code, item.item_type };
            if (!string.IsNullOrEmpty(item.category?.name)) labels.Add(item.category.name);
            AssetDatabase.SetLabels(asset, labels
                .Where(l => !string.IsNullOrWhiteSpace(l))
                .Select(l => l.Replace(' ', '_'))
                .Distinct()
                .ToArray());
        }

        private static void EnsureFolder(string folder)
        {
            if (string.IsNullOrEmpty(folder) || AssetDatabase.IsValidFolder(folder)) return;
            var parent = Path.GetDirectoryName(folder)?.Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
        }

        // After a move, the old category folder is usually empty. Removed up to
        // (not including) the Target Folder, and only when truly empty.
        private static void DeleteEmptyFolders(string folder)
        {
            string root;
            try { root = PathSafety.NormalizeTarget(PipelineSettings.ImportTargetPath); }
            catch (InvalidOperationException) { return; }

            while (!string.IsNullOrEmpty(folder) && folder.StartsWith(root + "/", StringComparison.OrdinalIgnoreCase)
                   && AssetDatabase.IsValidFolder(folder))
            {
                if (Directory.EnumerateFileSystemEntries(folder).Any()) return;
                AssetDatabase.DeleteAsset(folder);
                folder = Path.GetDirectoryName(folder)?.Replace('\\', '/');
            }
        }
    }
}
