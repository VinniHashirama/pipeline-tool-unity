using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;

namespace AntiGravity.PipelineTool.Editor
{
    public enum SyncState { Synced, Outdated, ModifiedLocally }

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
    /// read via AssetDatabase, never written into the asset's own .meta file.
    /// Stored as a single committed file so the whole team shares sync visibility.
    /// </summary>
    internal static class PipelineManifest
    {
        private static Dictionary<string, ManifestEntry> _cache;

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
        }

        public static void Remove(string guid, bool save = true)
        {
            EnsureLoaded();
            if (_cache.Remove(guid) && save) Save();
        }

        public static void Reload() => Load();

        private static void EnsureLoaded()
        {
            if (_cache == null) Load();
        }

        private static void Load()
        {
            _cache = new Dictionary<string, ManifestEntry>();
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
