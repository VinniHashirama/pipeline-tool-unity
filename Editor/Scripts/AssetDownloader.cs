using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;
using AntiGravity.PipelineTool.Editor.Models;

namespace AntiGravity.PipelineTool.Editor
{
    internal static class AssetDownloader
    {
        private static readonly Dictionary<string, string> TypeFolderNames = new Dictionary<string, string>
        {
            { "prop",         "Props"        },
            { "character",    "Characters"   },
            { "environment",  "Environments" },
            { "vfx",          "VFX"          },
            { "ui",           "UI"           },
            { "audio",        "Audio"        },
            { "other",        "Other"        },
        };

        /// <summary>
        /// Downloads an asset via the Next.js server proxy and saves it under the
        /// hierarchy: [ImportTargetPath]/[TypePlural]/[Category?]/[AssetTitle]/filename
        /// Returns the local path relative to the project root.
        /// </summary>
        public static async Task<string> DownloadAsync(
            ApprovedAsset asset,
            Action<float> onProgress = null)
        {
            var version = asset.latest_version;
            var destPath = BuildDestPath(asset);
            var destDir = Path.GetDirectoryName(destPath);

            if (!Directory.Exists(destDir))
                Directory.CreateDirectory(destDir);

            var url = $"{PipelineSettings.ApiBaseUrl}/api/assets/{Uri.EscapeDataString(asset.id)}/download" +
                      $"?version_id={Uri.EscapeDataString(version.id)}";

            using var req = UnityWebRequest.Get(url);

            var token = PipelineSettings.AccessToken;
            if (!string.IsNullOrEmpty(token))
                req.SetRequestHeader("Authorization", $"Bearer {token}");

            var op = req.SendWebRequest();

            while (!op.isDone)
            {
                onProgress?.Invoke(op.progress);
                await Task.Yield();
            }

            if (req.result != UnityWebRequest.Result.Success)
                throw new Exception($"Download failed ({req.responseCode}): {req.error}");

            var data = req.downloadHandler.data;
            await Task.Run(() => File.WriteAllBytes(destPath, data));

            AssetDatabase.ImportAsset(destPath, ImportAssetOptions.ForceUpdate);

            var guid = AssetDatabase.AssetPathToGUID(destPath);
            PipelineManifest.Set(guid, new ManifestEntry
            {
                guid = guid,
                asset_id = asset.id,
                version_id = version.id,
                version_number = version.version_number,
                task_title = asset.title,
                local_hash = PipelineManifest.Sha1Hex(data),
            });

            return destPath;
        }

        // [ImportTargetPath]/[TypePlural]/[Category?]/[AssetTitle]/filename
        private static string BuildDestPath(ApprovedAsset asset)
        {
            var version = asset.latest_version;
            var dir = PipelineSettings.ImportTargetPath;

            var typeName = TypeFolderNames.TryGetValue(asset.asset_type ?? "", out var t) ? t : "Other";
            dir = Path.Combine(dir, typeName);

            if (asset.category != null && !string.IsNullOrWhiteSpace(asset.category.name))
                dir = Path.Combine(dir, SanitizeSegment(asset.category.name));

            dir = Path.Combine(dir, SanitizeSegment(asset.title));

            return Path.Combine(dir, version.file_name).Replace("\\", "/");
        }

        private static string SanitizeSegment(string name)
        {
            foreach (var c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return name.Trim();
        }
    }
}
