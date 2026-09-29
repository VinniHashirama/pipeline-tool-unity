using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;

namespace AntiGravity.PipelineTool.Editor
{
    // Ordered from best to worst: a folder shows the worst state of what is inside it.
    public enum SyncState { Synced, Outdated, ModifiedLocally, Missing }

    [Serializable]
    public class ManifestEntry
    {
        public string guid;
        public string asset_id;
        public string version_id;
        public string task_title;
        public string local_hash;
        public int version_number;
        // v0.3 — files that came from an Item. Empty for assets imported by type.
        public string item_id;
        public string engine_path; // as the server sent it, relative to the Target Folder
        // v0.5 — project-relative path the file was written to. Once the file is
        // deleted its GUID resolves to nothing; this is where Reimport falls back to.
        public string local_path;
    }

    [Serializable]
    internal class ManifestFile
    {
        public List<ManifestEntry> entries = new();
    }

    /// <summary>
    /// Local record of which assets in this Unity project came from Hopper,
    /// which version they were downloaded at, and a content hash to detect local edits.
    /// Keyed by asset GUID (not path) so it survives renames/moves — the GUID is only ever
    /// read via AssetDatabase; the one exception is AssetRestorer, which writes a .meta
    /// with the original GUID when bringing back a deleted file.
    /// Stored as a single committed file so the whole team shares sync visibility.
    /// </summary>
    internal static class PipelineManifest
    {
        private static Dictionary<string, ManifestEntry> _cache;
        // Set/Remove with save: false keep changes only in memory (an import in
        // progress, between downloads). Reload must not throw them away.
        private static bool _dirty;

        public static bool HasUnsavedChanges => _dirty;

        private static string ManifestPath =>
            Path.Combine(PipelineSettings.ImportTargetPath, ".pipeline-manifest.json").Replace("\\", "/");

        public static ManifestEntry Get(string guid)
        {
            EnsureLoaded();
            return _cache.TryGetValue(guid, out var entry) ? entry : null;
        }

        public static IEnumerable<ManifestEntry> All()
        {
            EnsureLoaded();
            return _cache.Values;
        }

        /// <summary>Entries of one Hopper asset. More than one only for legacy orphans (a path changed before v0.3).</summary>
        public static List<ManifestEntry> FindByAssetId(string assetId)
        {
            EnsureLoaded();
            var found = new List<ManifestEntry>();
            foreach (var entry in _cache.Values)
                if (entry.asset_id == assetId) found.Add(entry);
            return found;
        }

        /// <param name="save">false when writing several entries in a row; call Save() at the end.</param>
        public static void Set(string guid, ManifestEntry entry, bool save = true)
        {
            EnsureLoaded();
            _cache[guid] = entry;
            if (save) Save();
            else _dirty = true;
        }

        public static void Remove(string guid, bool save = true)
        {
            EnsureLoaded();
            if (!_cache.Remove(guid)) return;
            if (save) Save();
            else _dirty = true;
        }

        /// <summary>Re-reads the file (a git pull may have changed it), unless unsaved changes are pending.</summary>
        public static void Reload()
        {
            if (!_dirty) Load();
        }

        /// <summary>The entry's file exists on disk.</summary>
        public static bool IsPresent(ManifestEntry entry)
        {
            var path = AssetDatabase.GUIDToAssetPath(entry.guid);
            return !string.IsNullOrEmpty(path) && File.Exists(path);
        }

        /// <summary>
        /// Where the entry's file is, or was: the GUID's path (Unity still answers for a
        /// recently deleted asset, until the Editor restarts), else the path recorded at
        /// import, else the one the server gave (Items). Null when unknown.
        /// </summary>
        public static string KnownPath(ManifestEntry entry)
        {
            var path = AssetDatabase.GUIDToAssetPath(entry.guid);
            if (!string.IsNullOrEmpty(path)) return path;
            if (!string.IsNullOrEmpty(entry.local_path)) return entry.local_path;
            if (string.IsNullOrEmpty(entry.engine_path)) return null;
            try { return PathSafety.Combine(PipelineSettings.ImportTargetPath, entry.engine_path); }
            catch (InvalidOperationException) { return null; }
        }

        private static void EnsureLoaded()
        {
            if (_cache == null) Load();
        }

        private static void Load()
        {
            _cache = new Dictionary<string, ManifestEntry>();
            _dirty = false;
            if (!File.Exists(ManifestPath)) return;

            try
            {
                var json = File.ReadAllText(ManifestPath);
                var file = JsonUtility.FromJson<ManifestFile>(json);
                foreach (var entry in file?.entries ?? new List<ManifestEntry>())
                    _cache[entry.guid] = entry;
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning($"[Hopper] Failed to read {ManifestPath}: {ex.Message}");
            }
        }

        public static void Save()
        {
            EnsureLoaded();
            var dir = Path.GetDirectoryName(ManifestPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var entries = new List<ManifestEntry>(_cache.Values);
            entries.Sort((a, b) => string.CompareOrdinal(a.guid, b.guid));

            var file = new ManifestFile { entries = entries };
            File.WriteAllText(ManifestPath, JsonUtility.ToJson(file, true));
            _dirty = false;
            AssetDatabase.Refresh();
        }

        public static string Sha1Hex(byte[] data)
        {
            using var sha1 = SHA1.Create();
            var hash = sha1.ComputeHash(data);
            var hex = new System.Text.StringBuilder(hash.Length * 2);
            foreach (var b in hash)
                hex.Append(b.ToString("x2"));
            return hex.ToString();
        }
    }
}
