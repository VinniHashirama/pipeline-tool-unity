using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEditor;
using AntiGravity.PipelineTool.Editor.Models;

namespace AntiGravity.PipelineTool.Editor
{
    /// <summary>
    /// Import of assets by type (projects organized by type, /api/assets/approved).
    /// Items use ItemImporter, where the server decides the path.
    /// </summary>
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
            { "texture",      "Textures"     },
            { "material",     "Materials"    },
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
            // Validated before any byte arrives: nothing lands outside the Target Folder.
            var destPath = BuildDestPath(asset);

            var data = await PipelineApiClient.DownloadAsync(asset.id, version.id, onProgress);

            // A tracked file deleted from this path comes back with its old GUID.
            var guid = AssetRestorer.WriteAsset(destPath, data, AssetRestorer.GuidToKeep(asset.id, destPath));
            PipelineManifest.Set(guid, new ManifestEntry
            {
                guid = guid,
                asset_id = asset.id,
                version_id = version.id,
                version_number = version.version_number,
                task_title = asset.title,
                local_hash = PipelineManifest.Sha1Hex(data),
                local_path = destPath,
            });

            return destPath;
        }

        // [ImportTargetPath]/[TypePlural]/[Category?]/[AssetTitle]/filename
        // Also used by PipelineSyncStatus to know where a deleted file belongs.
        internal static string BuildDestPath(ApprovedAsset asset)
        {
            var typeName = TypeFolderNames.TryGetValue(asset.asset_type ?? "", out var t) ? t : "Other";
            var segments = new List<string> { typeName };

            if (asset.category != null && !string.IsNullOrWhiteSpace(asset.category.name))
                segments.Add(PathSafety.SanitizeSegment(asset.category.name));

            segments.Add(PathSafety.SanitizeSegment(asset.title));
            // file_name is the stable name from the server; it is a name, never a path.
            segments.Add(asset.latest_version.file_name);

            return PathSafety.Combine(PipelineSettings.ImportTargetPath, string.Join("/", segments));
        }
    }
}
