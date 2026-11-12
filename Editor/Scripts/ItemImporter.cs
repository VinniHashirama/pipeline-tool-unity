using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using Avatar = UnityEngine.Avatar;
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
    /// Import by Item (v0.3, Phase 5 of item-architecture.md). The server decides
    /// where each file goes (engine_path); this class checks the path, downloads,
    /// writes, labels and records it in the manifest.
    ///
    /// When a file the manifest knows is somewhere else — someone dragged the
    /// folder by hand, or (once D12 comes back) the Item changed category — it is never
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
                    entry.local_path  = move.To;
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
            // The rig first: Humanoid clips copy the avatar of the SK_ already imported (v0.6).
            var files = (item.files ?? Array.Empty<ItemFile>())
                .OrderBy(f => f.HasAnimation && !f.animation.is_clip ? 0 : 1)
                .ToArray();
            // Every path is checked before the first download: all or nothing.
            var destinations = files.ToDictionary(f => f.asset_id, f => PathSafety.Combine(PipelineSettings.ImportTargetPath, f.engine_path));

            var rigFile = files.FirstOrDefault(f => f.HasAnimation && !f.animation.is_clip);
            var rigPath = rigFile != null ? destinations[rigFile.asset_id] : null;

            foreach (var file in files)
            {
                var dest = destinations[file.asset_id];
                var signature = file.HasAnimation ? file.animation.ClipSignature() : "";
                var known = PipelineManifest.FindByAssetId(file.asset_id)
                    .FirstOrDefault(e => PathSafety.SamePath(AssetDatabase.GUIDToAssetPath(e.guid), dest));
                if (known != null && known.version_id == file.version_id && File.Exists(dest))
                {
                    if (signature == "" || known.clip_signature == signature)
                        continue; // already here, same version, same clips
                    // Same file, clip map edited in Hopper (v0.7): only the importer changes, no download.
                    onStatus?.Invoke($"Applying the clip map of {file.file_name}…");
                    var mapNote = AnimationImport.Apply(dest, file.animation, rigPath);
                    if (!string.IsNullOrEmpty(mapNote)) onStatus?.Invoke(mapNote);
                    known.clip_signature = signature;
                    PipelineManifest.Set(known.guid, known, save: false);
                    continue;
                }

                onStatus?.Invoke($"Downloading {file.file_name}…");
                var data = await PipelineApiClient.DownloadAsync(file.asset_id, file.version_id,
                    p => onStatus?.Invoke($"Downloading {file.file_name}… {p:P0}"));

                // A tracked file deleted from here comes back with its old GUID,
                // so scene and prefab references to it work again.
                var guid = AssetRestorer.WriteAsset(dest, data, AssetRestorer.GuidToKeep(file.asset_id, dest));
                if (file.HasAnimation)
                {
                    var note = AnimationImport.Apply(dest, file.animation, rigPath);
                    if (!string.IsNullOrEmpty(note)) onStatus?.Invoke(note);
                }
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
                    local_path     = dest,
                    clip_signature = signature,
                }, save: false);
            }
            PipelineManifest.Save();

            var toMark = files.Where(f => f.status != "imported").Select(f => f.version_id).ToArray();
            if (toMark.Length == 0)
                return new ItemImportResult { success = true, item_id = item.id, imported = Array.Empty<ItemImportedVersion>() };

            onStatus?.Invoke($"Marking {item.name} as imported…");
            return await PipelineApiClient.MarkItemImportedAsync(item.id, toMark, GitInfo.HeadCommit());
        }

        // Labels group, not identify: Hopper, type and category (l:Industrial).
        // No label per Item — one per object would flood the label list; the
        // Item already has its own folder and a unique name to search by.
        // Labels someone added by hand are kept; SetLabels would replace them.
        private static void SetLabels(string path, PublishedItem item)
        {
            var asset = AssetDatabase.LoadMainAssetAtPath(path);
            if (asset == null) return;
            var ours = new List<string> { "Hopper", item.item_type };
            if (!string.IsNullOrEmpty(item.category?.name)) ours.Add(item.category.name);
            var kept = AssetDatabase.GetLabels(asset)
                .Where(l => l != item.code); // the per-Item label of the first v0.3 builds
            AssetDatabase.SetLabels(asset, kept
                .Concat(ours)
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

    /// <summary>
    /// Import settings of rigs and animation clips (v0.6), from what Hopper says:
    /// Humanoid or Generic, the clip with the action's name and its loop, and — for
    /// Humanoid clips — the avatar of the Item's SK_. Written to the importer and
    /// saved (SaveAndReimport), so they live in the .meta and survive any reimport
    /// on any machine. Only FBX/glTF models; anything else is left alone.
    /// </summary>
    internal static class AnimationImport
    {
        /// <returns>A note for the status line when something could not be applied, else null.</returns>
        public static string Apply(string path, AnimationInfo info, string rigPath)
        {
            if (!(AssetImporter.GetAtPath(path) is ModelImporter importer)) return null;
            var humanoid = info.rig_type == "humanoid";
            importer.animationType = humanoid ? ModelImporterAnimationType.Human : ModelImporterAnimationType.Generic;
            importer.importAnimation = info.is_clip;
            string note = null;

            if (info.is_clip && humanoid)
            {
                // The clip retargets through the rig's avatar: the same bone mapping as the character.
                var avatar = string.IsNullOrEmpty(rigPath)
                    ? null
                    : AssetDatabase.LoadAllAssetsAtPath(rigPath).OfType<Avatar>().FirstOrDefault(a => a.isHuman);
                if (avatar != null)
                {
                    importer.avatarSetup = ModelImporterAvatarSetup.CopyFromOther;
                    importer.sourceAvatar = avatar;
                }
                else
                {
                    importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                    note = $"{Path.GetFileName(path)}: no Humanoid avatar from the rig (SK_) — avatar created from the clip itself.";
                }
            }

            // Saves the model settings first: the clip list depends on them.
            importer.SaveAndReimport();

            if (info.is_clip)
            {
                // v0.7: the clip map decides the clips. Without one (server before 12/nov), v0.6 behaviour.
                string mapNote = null;
                var clips = info.HasClipMap ? ClipsFromMap(importer, info, humanoid, out mapNote) : null;
                note = Join(note, mapNote);
                if (clips == null)
                {
                    clips = importer.clipAnimations;
                    if (clips == null || clips.Length == 0) clips = importer.defaultClipAnimations;
                    if (clips != null)
                    {
                        foreach (var clip in clips)
                        {
                            clip.loopTime = info.loop;
                            clip.loopPose = info.loop && humanoid;
                        }
                        // One take: named after the action (Walk), as Hopper shows it.
                        if (clips.Length == 1 && !string.IsNullOrEmpty(info.clip_name)) clips[0].name = info.clip_name;
                    }
                }
                if (clips != null && clips.Length > 0)
                {
                    importer.clipAnimations = clips;
                    importer.SaveAndReimport();
                }
            }
            return note;
        }

        /// <summary>
        /// One importer clip per row of the map: the take (by name, else by position),
        /// the frames, the loop and the name. Hopper counts frames at the project's fps
        /// from the start of the take; the importer counts at the file's sample rate from
        /// the take's own start, so the frames are converted. Null when no take matched.
        /// </summary>
        private static ModelImporterClipAnimation[] ClipsFromMap(ModelImporter importer, AnimationInfo info, bool humanoid, out string note)
        {
            note = null;
            var takes = importer.importedTakeInfos;
            if (takes == null || takes.Length == 0) return null;
            var fps = info.fps > 0 ? info.fps : 30;
            var result = new List<ModelImporterClipAnimation>();
            var missing = new List<string>();

            foreach (var c in info.clips)
            {
                var index = Array.FindIndex(takes, t => t.name == c.take);
                if (index < 0 && c.take_index >= 0 && c.take_index < takes.Length) index = c.take_index;
                if (index < 0) { missing.Add(c.take); continue; }

                var take = takes[index];
                var rate = take.sampleRate > 0 ? take.sampleRate : fps;
                var takeFirst = take.startTime * rate;
                var takeLast = take.stopTime * rate;
                var first = takeFirst + c.first_frame * rate / fps;
                var last = c.last_frame < 0 ? takeLast : takeFirst + c.last_frame * rate / fps;
                first = Math.Max(takeFirst, Math.Min(first, takeLast));
                last = Math.Max(first, Math.Min(last, takeLast));

                result.Add(new ModelImporterClipAnimation
                {
                    name       = string.IsNullOrEmpty(c.name) ? take.defaultClipName : c.name,
                    takeName   = take.name,
                    firstFrame = first,
                    lastFrame  = last,
                    loopTime   = c.loop,
                    loopPose   = c.loop && humanoid,
                });
            }

            if (missing.Count > 0)
                note = $"{Path.GetFileName(importer.assetPath)}: take(s) not found in the FBX: {string.Join(", ", missing.Distinct())}.";
            return result.Count > 0 ? result.ToArray() : null;
        }

        private static string Join(string a, string b) =>
            string.IsNullOrEmpty(a) ? b : string.IsNullOrEmpty(b) ? a : $"{a} {b}";
    }
}
