using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEditor;

namespace AntiGravity.PipelineTool.Editor
{
    /// <summary>
    /// Brings back files deleted locally (Reimport) or drops them from the manifest when
    /// the deletion was on purpose (Forget). A deletion is never reported to the server:
    /// imported_at in Hopper is history.
    ///
    /// Reimport downloads what Hopper publishes now, where Hopper says it belongs — not
    /// the version and path the manifest remembers, which may be gone (a version deleted
    /// in Hopper, a title or category that changed). It keeps the file's GUID by writing
    /// a minimal .meta with it before Unity imports the file: scene and prefab references
    /// point at that GUID, so they work again, and the manifest (keyed by GUID) stays
    /// valid. Import settings go back to their defaults, like any fresh import;
    /// `git checkout` of the deleted file and its .meta, when the deletion is not
    /// committed yet, brings those back too.
    /// </summary>
    internal static class AssetRestorer
    {
        /// <summary>
        /// Writes the bytes and imports them. With a GUID to keep and no .meta at dest,
        /// the .meta is written first with it. Returns the GUID the file ended up with.
        /// </summary>
        public static string WriteAsset(string dest, byte[] data, string keepGuid)
        {
            var dir = Path.GetDirectoryName(dest);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var meta = dest + ".meta";
            var writeMeta = !string.IsNullOrEmpty(keepGuid) && !File.Exists(meta) && !File.Exists(dest);

            // Synchronous and back to back: an Editor refresh between the two would see a
            // .meta without its file (and delete it) or a file without .meta (new GUID).
            File.WriteAllBytes(dest, data);
            if (writeMeta)
                File.WriteAllText(meta, $"fileFormatVersion: 2\nguid: {keepGuid}\n");
            AssetDatabase.ImportAsset(dest, ImportAssetOptions.ForceUpdate);

            return AssetDatabase.AssetPathToGUID(dest);
        }

        /// <summary>
        /// GUID of this asset's deleted file, if the manifest has one and no other asset
        /// took that GUID meanwhile. The entry at dest wins over one at another path.
        /// </summary>
        public static string GuidToKeep(string assetId, string dest)
        {
            string any = null;
            foreach (var entry in PipelineManifest.FindByAssetId(assetId))
            {
                if (string.IsNullOrEmpty(entry.guid) || PipelineManifest.IsPresent(entry)) continue;
                if (PathSafety.SamePath(PipelineManifest.KnownPath(entry), dest)) return entry.guid;
                any ??= entry.guid;
            }
            return any;
        }

        /// <summary>
        /// Downloads each entry's published version to where Hopper says it belongs. An
        /// asset Hopper no longer publishes falls back to the version and path the manifest
        /// recorded. Returns one message per file that failed.
        /// </summary>
        public static async Task<List<string>> ReimportAsync(IEnumerable<ManifestEntry> entries, Action<string> onStatus)
        {
            var errors = new List<string>();

            // The published version and path come from the feed: fetch it if this
            // session has not checked yet.
            if (!PipelineSyncStatus.HasFeed)
            {
                onStatus?.Invoke("Checking what Hopper publishes…");
                await PipelineSyncStatus.RefreshAsync(PipelineSettings.ProjectId);
            }

            foreach (var entry in entries)
            {
                var name = entry.task_title;
                try
                {
                    var versionId     = entry.version_id;
                    var versionNumber = entry.version_number;
                    var path          = PipelineSyncStatus.PathFor(entry);
                    var published     = PipelineSyncStatus.TryGetPublished(entry.asset_id,
                        out var pubVersionId, out var pubVersionNumber, out var pubPath);
                    if (published)
                    {
                        versionId     = pubVersionId;
                        versionNumber = pubVersionNumber;
                        if (!string.IsNullOrEmpty(pubPath)) path = pubPath; // Hopper decides the path
                    }

                    var dest = SafeDestination(path);
                    name = Path.GetFileName(dest);
                    if (File.Exists(dest))
                    {
                        errors.Add($"{dest} already exists — move it away or use Forget.");
                        continue;
                    }

                    onStatus?.Invoke($"Reimporting {name}…");
                    var data = await PipelineApiClient.DownloadAsync(entry.asset_id, versionId,
                        p => onStatus?.Invoke($"Reimporting {name}… {p:P0}"));

                    var guid = WriteAsset(dest, data, entry.guid);
                    if (guid != entry.guid)
                    {
                        // Unity would not take the old GUID: references stay broken, but the
                        // manifest must follow the file it now tracks.
                        PipelineManifest.Remove(entry.guid, save: false);
                        entry.guid = guid;
                    }
                    var newerVersion = versionId != entry.version_id;
                    entry.version_id     = versionId;
                    entry.version_number = versionNumber;
                    entry.local_path     = dest;
                    entry.local_hash     = PipelineManifest.Sha1Hex(data);
                    if (published && !string.IsNullOrEmpty(entry.item_id))
                        entry.engine_path = RelativeToTarget(dest);
                    PipelineManifest.Set(entry.guid, entry, save: false);

                    // A newer version than the one deleted is an import like any other.
                    if (newerVersion)
                        await MarkImportedAsync(entry, versionId, name, errors);
                }
                catch (Exception ex)
                {
                    errors.Add(ex.Message.Contains("404")
                        ? $"{name}: this version is no longer in Hopper — use Forget, then import again."
                        : $"{name}: {ex.Message.Split('\n')[0]}");
                }
            }
            PipelineManifest.Save();
            PipelineSyncStatus.Recompute();
            return errors;
        }

        /// <summary>The files were deleted on purpose: stop tracking them.</summary>
        public static void Forget(IEnumerable<ManifestEntry> entries)
        {
            foreach (var entry in entries)
                PipelineManifest.Remove(entry.guid, save: false);
            PipelineManifest.Save();
            PipelineSyncStatus.Recompute();
        }

        // The file is back on disk either way: a failure here is reported, not undone.
        private static async Task MarkImportedAsync(ManifestEntry entry, string versionId, string name, List<string> errors)
        {
            try
            {
                if (!string.IsNullOrEmpty(entry.item_id))
                    await PipelineApiClient.MarkItemImportedAsync(entry.item_id, new[] { versionId }, GitInfo.HeadCommit());
                else
                    await PipelineApiClient.MarkImportedAsync(entry.asset_id, versionId, GitInfo.HeadCommit());
            }
            catch (Exception ex)
            {
                errors.Add($"{name} is back, but Hopper was not told it was imported: {ex.Message.Split('\n')[0]}");
            }
        }

        // The recorded path came from this machine or from the committed manifest:
        // checked like a server path, it must stay inside the Target Folder.
        private static string SafeDestination(string path)
        {
            if (string.IsNullOrEmpty(path))
                throw new InvalidOperationException("Hopper does not know where this file goes — use Forget, then import again.");
            var root = PathSafety.NormalizeTarget(PipelineSettings.ImportTargetPath);
            return PathSafety.Combine(root, RelativeToTarget(path));
        }

        private static string RelativeToTarget(string path)
        {
            var root = PathSafety.NormalizeTarget(PipelineSettings.ImportTargetPath);
            var p = path.Replace('\\', '/');
            if (!p.StartsWith(root + "/", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"{path} is outside the Target Folder ({root}).");
            return p.Substring(root.Length + 1);
        }
    }
}
