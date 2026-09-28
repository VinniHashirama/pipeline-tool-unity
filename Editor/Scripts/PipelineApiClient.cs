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
        // ------------------------------------------------------------------ //
        // Auth

        public static async Task LoginAsync(string email, string password)
        {
            var url     = $"{PipelineSettings.ApiBaseUrl}/api/auth/login";
            var payload = JsonUtility.ToJson(new LoginRequest { email = email, password = password });
            var json    = await PostAsync(url, payload, requiresAuth: false);
            var resp    = JsonUtility.FromJson<AuthResponse>(json);
            StoreSession(resp);
        }

        public static async Task RefreshAsync()
        {
            var url     = $"{PipelineSettings.ApiBaseUrl}/api/auth/refresh";
            var payload = JsonUtility.ToJson(new RefreshRequest { refresh_token = PipelineSettings.RefreshToken });
            var json    = await PostAsync(url, payload, requiresAuth: false);
            var resp    = JsonUtility.FromJson<AuthResponse>(json);
            StoreSession(resp);
        }

        public static async Task<ProjectInfo[]> GetUserProjectsAsync()
        {
            await EnsureValidTokenAsync();
            var url  = $"{PipelineSettings.ApiBaseUrl}/api/user/projects";
            var json = await GetAsync(url);
            var resp = JsonUtility.FromJson<ProjectsResponse>(json);
            return resp.projects ?? Array.Empty<ProjectInfo>();
        }

        /// <summary>
        /// Permissões efetivas do usuário no projeto selecionado. Usado só para
        /// esconder controles — quem decide de verdade é o servidor, em cada rota.
        /// </summary>
        public static async Task<UserPermissions> GetPermissionsAsync(string projectId = null)
        {
            await EnsureValidTokenAsync();
            var url = $"{PipelineSettings.ApiBaseUrl}/api/user/permissions";
            if (!string.IsNullOrEmpty(projectId))
                url += $"?project_id={Uri.EscapeDataString(projectId)}";

            var json = await GetAsync(url);
            return JsonUtility.FromJson<UserPermissions>(json);
        }

        // ------------------------------------------------------------------ //
        // Assets

        public static async Task<ApprovedAssetsResponse> GetApprovedAssetsAsync(string projectId = null)
        {
            await EnsureValidTokenAsync();
            var url = $"{PipelineSettings.ApiBaseUrl}/api/assets/approved";
            if (!string.IsNullOrEmpty(projectId))
                url += $"?project_id={Uri.EscapeDataString(projectId)}";

            var json = await GetAsync(url);
            return JsonUtility.FromJson<ApprovedAssetsResponse>(json);
        }

        /// <summary>
        /// Lists approved + imported assets for a project — used to build the sync-status
        /// cache (latest server version per asset), not the "assets to import" list.
        /// </summary>
        public static async Task<ApprovedAssetsResponse> GetProjectAssetsForSyncAsync(string projectId)
        {
            await EnsureValidTokenAsync();
            var url = $"{PipelineSettings.ApiBaseUrl}/api/assets/approved?include_imported=true";
            if (!string.IsNullOrEmpty(projectId))
                url += $"&project_id={Uri.EscapeDataString(projectId)}";

            var json = await GetAsync(url);
            return JsonUtility.FromJson<ApprovedAssetsResponse>(json);
        }

        public static async Task<ImportResult> MarkImportedAsync(string taskId, string versionId, string commitHash = null)
        {
            await EnsureValidTokenAsync();
            var url     = $"{PipelineSettings.ApiBaseUrl}/api/assets/{Uri.EscapeDataString(taskId)}/mark-imported";
            var payload = JsonUtility.ToJson(new MarkImportedPayload
            {
                version_id  = versionId ?? "",
                commit_hash = commitHash ?? "",
            });
            var json = await PatchAsync(url, payload);
            return JsonUtility.FromJson<ImportResult>(json);
        }

        // ------------------------------------------------------------------ //
        // Items (v0.3 — projects organized by Item)

        /// <summary>
        /// Items with the published version of each file that goes to the engine.
        /// Always asks for imported ones too: they are needed to detect files whose
        /// engine_path changed (category change) and to compute sync status.
        /// Returns an empty response on a server without the endpoint (404).
        /// </summary>
        public static async Task<PublishedItemsResponse> GetPublishedItemsAsync(string projectId)
        {
            if (string.IsNullOrEmpty(projectId))
                return new PublishedItemsResponse { items = Array.Empty<PublishedItem>() };

            await EnsureValidTokenAsync();
            var url = $"{PipelineSettings.ApiBaseUrl}/api/items/published?include_imported=true" +
                      $"&project_id={Uri.EscapeDataString(projectId)}";
            try
            {
                var json = await GetAsync(url);
                var resp = JsonUtility.FromJson<PublishedItemsResponse>(json);
                resp.items ??= Array.Empty<PublishedItem>();
                return resp;
            }
            catch (Exception ex) when (ex.Message.StartsWith("HTTP 404", StringComparison.Ordinal))
            {
                return new PublishedItemsResponse { items = Array.Empty<PublishedItem>() };
            }
        }

        public static async Task<ItemImportResult> MarkItemImportedAsync(string itemId, string[] versionIds, string commitHash)
        {
            await EnsureValidTokenAsync();
            var url     = $"{PipelineSettings.ApiBaseUrl}/api/items/{Uri.EscapeDataString(itemId)}/mark-imported";
            var payload = JsonUtility.ToJson(new MarkItemImportedPayload
            {
                version_ids = versionIds,
                commit_hash = commitHash ?? "",
            });
            var json = await PostAsync(url, payload);
            return JsonUtility.FromJson<ItemImportResult>(json);
        }

        /// <summary>
        /// File bytes via the server proxy — Unity never talks to the storage
        /// directly. Refreshes the token first: a long session would otherwise
        /// fail the download and only then refresh.
        /// </summary>
        public static async Task<byte[]> DownloadAsync(string assetId, string versionId, Action<float> onProgress = null)
        {
            await EnsureValidTokenAsync();
            var url = $"{PipelineSettings.ApiBaseUrl}/api/assets/{Uri.EscapeDataString(assetId)}/download" +
                      $"?version_id={Uri.EscapeDataString(versionId)}";

            using var req = UnityWebRequest.Get(url);
            ApplyAuthHeaders(req);
            var op = req.SendWebRequest();
            while (!op.isDone)
            {
                onProgress?.Invoke(op.progress);
                await Task.Yield();
            }

            if (req.result != UnityWebRequest.Result.Success)
                throw new Exception($"Download failed ({req.responseCode}): {req.error}");
            return req.downloadHandler.data;
        }

        /// <summary>
        /// Image at a relative API path (an Item's thumbnail_url), with the same auth
        /// headers as every call. Returns null instead of throwing: a thumbnail that
        /// fails — offline, 404, a format Unity cannot decode such as WebP — just
        /// leaves the placeholder. The caller owns (and destroys) the texture.
        /// </summary>
        public static async Task<Texture2D> DownloadTextureAsync(string relativePath)
        {
            if (string.IsNullOrEmpty(relativePath)) return null;
            try
            {
                await EnsureValidTokenAsync();
                var url = relativePath.StartsWith("/", StringComparison.Ordinal)
                    ? PipelineSettings.ApiBaseUrl + relativePath
                    : $"{PipelineSettings.ApiBaseUrl}/{relativePath}";

                using var req = UnityWebRequestTexture.GetTexture(url, nonReadable: true);
                ApplyAuthHeaders(req);
                var op = req.SendWebRequest();
                while (!op.isDone)
                    await Task.Yield();

                if (req.result != UnityWebRequest.Result.Success) return null;
                var tex = DownloadHandlerTexture.GetContent(req);
                if (tex != null) tex.hideFlags = HideFlags.HideAndDontSave;
                return tex;
            }
            catch (Exception)
            {
                return null;
            }
        }

        // ------------------------------------------------------------------ //
        // Internals

        private static async Task EnsureValidTokenAsync()
        {
            if (NowUtcSeconds() >= PipelineSettings.TokenExpiryUtc)
                await RefreshAsync();
        }

        private static void StoreSession(AuthResponse resp)
        {
            PipelineSettings.AccessToken    = resp.access_token  ?? "";
            PipelineSettings.RefreshToken   = resp.refresh_token ?? "";
            // Refresh 60s before actual expiry to avoid edge-case 401s
            PipelineSettings.TokenExpiryUtc = NowUtcSeconds() + resp.expires_in - 60;

            if (resp.user != null)
            {
                PipelineSettings.UserId       = resp.user.id        ?? "";
                PipelineSettings.UserEmail    = resp.user.email      ?? "";
                PipelineSettings.UserFullName = resp.user.full_name  ?? "";
            }
        }

        private static long NowUtcSeconds() =>
            (long)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;

        // ------------------------------------------------------------------ //
        // HTTP helpers

        private static Task<string> GetAsync(string url) =>
            SendAsync(BuildRequest(url, "GET", null));

        private static Task<string> PostAsync(string url, string jsonBody, bool requiresAuth = true) =>
            SendAsync(BuildRequest(url, "POST", jsonBody, requiresAuth));

        private static Task<string> PatchAsync(string url, string jsonBody) =>
            SendAsync(BuildRequest(url, "PATCH", jsonBody));

        private static UnityWebRequest BuildRequest(string url, string method, string jsonBody, bool requiresAuth = true)
        {
            UnityWebRequest req;

            if (jsonBody != null)
            {
                var bytes = Encoding.UTF8.GetBytes(jsonBody);
                req = new UnityWebRequest(url, method)
                {
                    uploadHandler   = new UploadHandlerRaw(bytes),
                    downloadHandler = new DownloadHandlerBuffer(),
                };
                req.SetRequestHeader("Content-Type", "application/json");
            }
            else
            {
                req = UnityWebRequest.Get(url);
            }

            if (requiresAuth)
                ApplyAuthHeaders(req);

            return req;
        }

        private static void ApplyAuthHeaders(UnityWebRequest req)
        {
            var token = PipelineSettings.AccessToken;
            if (!string.IsNullOrEmpty(token))
                req.SetRequestHeader("Authorization", $"Bearer {token}");

            // Legacy dual-auth fallback: if a Pipeline API Key is configured,
            // send it alongside — the server accepts either auth method.
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

        [Serializable]
        private class MarkItemImportedPayload
        {
            public string[] version_ids;
            public string commit_hash;
        }
    }
}
