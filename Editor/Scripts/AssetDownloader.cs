using System;
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
        /// <summary>
        /// Downloads an asset via the Next.js server proxy (authenticated).
        /// The server fetches the file from Supabase Storage internally,
        /// so the Unity tool never needs direct access to the private bucket.
        /// Returns the local path relative to the project root.
        /// </summary>
        public static async Task<string> DownloadAsync(
            string taskId,
            AssetVersionInfo version,
            Action<float> onProgress = null)
        {
            var targetDir = PipelineSettings.ImportTargetPath;
            if (!Directory.Exists(targetDir))
                Directory.CreateDirectory(targetDir);

            var destPath = Path.Combine(targetDir, version.file_name).Replace("\\", "/");

            // Route through the server proxy — avoids direct Supabase Storage auth complexity.
            var url = $"{PipelineSettings.ApiBaseUrl}/api/assets/{Uri.EscapeDataString(taskId)}/download" +
                      $"?version_id={Uri.EscapeDataString(version.id)}";

            using var req = UnityWebRequest.Get(url);

            // Authenticate with the same JWT used for all other API calls.
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

            return destPath;
        }
    }
}
