using System;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
using AntiGravity.PipelineTool.Editor.Models;

namespace AntiGravity.PipelineTool.Editor
{
    internal static class PipelineApiClient
    {
        public static async Task<ApprovedAssetsResponse> GetApprovedAssetsAsync(string projectId = null)
        {
            var url = $"{PipelineSettings.ApiBaseUrl}/api/assets/approved";
            if (!string.IsNullOrEmpty(projectId))
                url += $"?project_id={Uri.EscapeDataString(projectId)}";

            var json = await GetAsync(url);
            return JsonUtility.FromJson<ApprovedAssetsResponse>(json);
        }

        public static async Task<ImportResult> MarkImportedAsync(string taskId, string versionId, string commitHash = null)
        {
            var url = $"{PipelineSettings.ApiBaseUrl}/api/assets/{Uri.EscapeDataString(taskId)}/mark-imported";
            var payload = JsonUtility.ToJson(new MarkImportedPayload
            {
                version_id  = versionId ?? "",
                commit_hash = commitHash ?? "",
            });
            var json = await PatchAsync(url, payload);
            return JsonUtility.FromJson<ImportResult>(json);
        }

        // ------------------------------------------------------------------ //

        private static Task<string> GetAsync(string url)
        {
            var req = UnityWebRequest.Get(url);
            ApplyHeaders(req);
            return SendAsync(req);
        }

        private static Task<string> PatchAsync(string url, string jsonBody)
        {
            var bytes = Encoding.UTF8.GetBytes(jsonBody);
            var req   = new UnityWebRequest(url, "PATCH")
            {
                uploadHandler   = new UploadHandlerRaw(bytes),
                downloadHandler = new DownloadHandlerBuffer(),
            };
            req.SetRequestHeader("Content-Type", "application/json");
            ApplyHeaders(req);
            return SendAsync(req);
        }

        private static void ApplyHeaders(UnityWebRequest req)
        {
            var anonKey = PipelineSettings.SupabaseAnonKey;
            if (!string.IsNullOrEmpty(anonKey))
                req.SetRequestHeader("Authorization", $"Bearer {anonKey}");

            // Optional dedicated API key (see PipelineSettings.PipelineApiKey).
            var apiKey = PipelineSettings.PipelineApiKey;
            if (!string.IsNullOrEmpty(apiKey))
                req.SetRequestHeader("X-Pipeline-Key", apiKey);
        }

        private static Task<string> SendAsync(UnityWebRequest req)
        {
            var tcs = new TaskCompletionSource<string>();
            req.SendWebRequest().completed += _ =>
            {
                if (req.result == UnityWebRequest.Result.Success)
                    tcs.SetResult(req.downloadHandler.text);
                else
                    tcs.SetException(new Exception($"HTTP {req.responseCode}: {req.error}\n{req.downloadHandler?.text}"));
                req.Dispose();
            };
            return tcs.Task;
        }

        [Serializable]
        private class MarkImportedPayload
        {
            public string version_id;
            public string commit_hash;
        }
    }
}
