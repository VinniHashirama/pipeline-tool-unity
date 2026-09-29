using System;
using UnityEditor;

namespace AntiGravity.PipelineTool.Editor
{
    /// <summary>
    /// Recomputes the sync state (disk + manifest, no network) when something under the
    /// Target Folder is imported, deleted or moved — deleting a file in the Project window
    /// shows as Missing right away. A deletion made outside Unity shows once Unity notices
    /// it, on its next asset refresh. Never removes a manifest entry: a deletion by mistake
    /// is exactly what this should point out.
    /// </summary>
    internal class PipelineAssetWatcher : AssetPostprocessor
    {
        private static bool _scheduled;

        private static void OnPostprocessAllAssets(
            string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            if (_scheduled) return;

            string root;
            try { root = PathSafety.NormalizeTarget(PipelineSettings.ImportTargetPath); }
            catch (InvalidOperationException) { return; }

            if (!Touches(root, deleted) && !Touches(root, moved) && !Touches(root, movedFrom) && !Touches(root, imported))
                return;

            // Once per batch, after Unity finishes the import it is in the middle of.
            _scheduled = true;
            EditorApplication.delayCall += () =>
            {
                _scheduled = false;
                PipelineSyncStatus.Recompute();
            };
        }

        private static bool Touches(string root, string[] paths)
        {
            foreach (var path in paths)
                if (path == root || path.StartsWith(root + "/", StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }
    }
}
