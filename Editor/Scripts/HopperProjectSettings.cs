using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace AntiGravity.PipelineTool.Editor
{
    [Serializable]
    internal class HopperSettingsFile
    {
        // Field order is the order in the JSON: keep it stable, it is diffed in the game repo.
        public int    version            = 1;
        public string project_id         = "";
        public string project_name       = "";
        public string import_target_path = HopperProjectSettings.DefaultTargetPath;
    }

    /// <summary>
    /// Settings that belong to the Unity project, not to the person (v0.8):
    /// which Hopper project it imports from and the Target Folder (where the
    /// manifest lives). Stored in ProjectSettings/HopperSettings.json, committed
    /// with the game, so whoever clones the repo gets the same values.
    ///
    /// Never put anything personal or secret here — tokens, e-mail and the dev
    /// server override stay in EditorPrefs (PipelineSettings).
    /// </summary>
    internal static class HopperProjectSettings
    {
        public const string RelativePath      = "ProjectSettings/HopperSettings.json";
        public const string DefaultTargetPath = "Assets/ImportedAssets";

        private static HopperSettingsFile _data;
        private static bool     _exists;
        private static DateTime _mtime;
        private static double   _checkedAt = -1;

        // Getters run on many repaints; the disk is checked at most this often.
        // A git pull that changes the file is picked up within the interval.
        private const double CheckInterval = 2.0;

        private static string FullPath =>
            Path.GetFullPath(Path.Combine(Application.dataPath, "..", RelativePath));

        /// <summary>The file exists in this Unity project (someone already linked it to Hopper).</summary>
        public static bool Exists { get { EnsureFresh(); return _exists; } }

        public static string ProjectId   { get { EnsureFresh(); return _data.project_id   ?? ""; } }
        public static string ProjectName { get { EnsureFresh(); return _data.project_name ?? ""; } }

        /// <summary>
        /// As written in the file. Not normalized here: the file comes from Git, so every
        /// consumer already passes it through PathSafety, which refuses it with a message.
        /// </summary>
        public static string ImportTargetPath
        {
            get
            {
                EnsureFresh();
                return string.IsNullOrWhiteSpace(_data.import_target_path) ? DefaultTargetPath : _data.import_target_path;
            }
        }

        public static void SetProject(string id, string name)
        {
            EnsureFresh(force: true);
            var d = Copy(_data);
            d.project_id   = id ?? "";
            d.project_name = name ?? "";
            Save(d);
        }

        /// <param name="normalizedTarget">Already through PathSafety.NormalizeTarget.</param>
        public static void SetImportTargetPath(string normalizedTarget)
        {
            EnsureFresh(force: true);
            var d = Copy(_data);
            d.import_target_path = normalizedTarget;
            Save(d);
        }

        /// <summary>One write for both (migration from the v0.7 EditorPrefs).</summary>
        public static void SetAll(string id, string name, string normalizedTarget)
        {
            EnsureFresh(force: true);
            var d = Copy(_data);
            d.project_id         = id ?? "";
            d.project_name       = name ?? "";
            d.import_target_path = normalizedTarget;
            Save(d);
        }

        private static void EnsureFresh(bool force = false)
        {
            var now = EditorApplication.timeSinceStartup;
            if (!force && _data != null && _checkedAt >= 0 && now - _checkedAt < CheckInterval) return;
            _checkedAt = now;

            var path   = FullPath;
            var exists = File.Exists(path);
            var mtime  = exists ? File.GetLastWriteTimeUtc(path) : default;
            if (_data != null && exists == _exists && mtime == _mtime) return;

            _exists = exists;
            _mtime  = mtime;
            _data   = exists ? Read(path) : new HopperSettingsFile();
        }

        private static HopperSettingsFile Read(string path)
        {
            try
            {
                var parsed = JsonUtility.FromJson<HopperSettingsFile>(File.ReadAllText(path));
                if (parsed != null) return parsed;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Hopper] {RelativePath} could not be read ({ex.Message}). " +
                               "Using defaults until it is fixed or saved again from Hopper > Settings.");
            }
            return new HopperSettingsFile();
        }

        private static void Save(HopperSettingsFile d)
        {
            d.version = 1;
            var path = FullPath;
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, JsonUtility.ToJson(d, true).Replace("\r\n", "\n") + "\n", new UTF8Encoding(false));
            _data      = d;
            _exists    = true;
            _mtime     = File.GetLastWriteTimeUtc(path);
            _checkedAt = EditorApplication.timeSinceStartup;
        }

        private static HopperSettingsFile Copy(HopperSettingsFile d) => new()
        {
            version            = d.version,
            project_id         = d.project_id,
            project_name       = d.project_name,
            import_target_path = d.import_target_path,
        };
    }
}
