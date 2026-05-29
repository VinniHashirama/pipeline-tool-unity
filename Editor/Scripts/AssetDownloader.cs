using System;
using System.IO;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine.Networking;
using AntiGravity.PipelineTool.Editor.Models;

namespace AntiGravity.PipelineTool.Editor
{
    internal static class AssetDownloader
    {
        /// <summary>
        /// Downloads the file described by <paramref name="version"/> into the configured
        /// import folder, then triggers an AssetDatabase refresh.
        /// Returns the local path relative to the project root (e.g. "Assets/ImportedAssets/SM_Rock_v1.fbx").
        /// </summary>
        public static async Task<string> DownloadAsync(AssetVersionInfo version, Action<float> onProgress = null)
        {
            var targetDir = PipelineSettings.ImportTargetPath;
            if (!Directory.Exists(targetDir))
                Directory.CreateDirectory(targetDir);

            var destPath = Path.Combine(targetDir, version.file_name).Replace("\\", "/");

            using var req = UnityWebRequest.Get(version.file_url);
            var op = req.SendWebRequest();

            while (!op.isDone)
            {
                onProgress?.Invoke(op.progress);
                await Task.Yield();
            }

            if (req.result != UnityWebRequest.Result.Success)
                throw new Exception($"Download failed ({req.responseCode}): {req.error}");

            // Write on a thread pool thread to avoid blocking the main thread.
            var data = req.downloadHandler.data;
            await Task.Run(() => File.WriteAllBytes(destPath, data));

            AssetDatabase.ImportAsset(destPath, ImportAssetOptions.ForceUpdate);

            return destPath;
        }
    }
}
