using System;

namespace AntiGravity.PipelineTool.Editor.Models
{
    [Serializable]
    public class ImportResult
    {
        public bool success;
        public string task_id;
        public string new_status;
        public string imported_by;
        public string commit_hash;
        public int version;
    }
}
