using System;

namespace AntiGravity.PipelineTool.Editor.Models
{
    [Serializable]
    internal class LoginRequest
    {
        public string email;
        public string password;
    }

    [Serializable]
    internal class RefreshRequest
    {
        public string refresh_token;
    }

    [Serializable]
    internal class AuthResponse
    {
        public string access_token;
        public string refresh_token;
        public int    expires_in;
        public AuthUser user;
    }

    [Serializable]
    internal class AuthUser
    {
        public string id;
        public string email;
        public string full_name;
    }

    [Serializable]
    internal class ProjectsResponse
    {
        public ProjectInfo[] projects;
    }

    [Serializable]
    internal class ProjectInfo
    {
        public string id;
        public string name;
    }

    /// <summary>
    /// Resposta de GET /api/user/permissions.
    /// Achatada de propósito: JsonUtility não desserializa Dictionary nem array
    /// na raiz, então a lista de permissões vem como string[] dentro do objeto.
    /// </summary>
    [Serializable]
    internal class UserPermissions
    {
        public string[] allowed;
        public bool     is_admin;
        public string   role;
        public bool     enforcement_enabled;
        public long     matrix_version;
    }
}
