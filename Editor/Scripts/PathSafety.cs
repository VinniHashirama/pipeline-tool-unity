using System;
using System.IO;

namespace AntiGravity.PipelineTool.Editor
{
    /// <summary>
    /// Every path the plugin writes to comes from the server (engine_path,
    /// file_name) or from free text in Settings (Target Folder). Nothing is
    /// written until it passes here: relative, no "." or ".." segments, no
    /// characters Windows/macOS/Unity refuse, and inside the target folder,
    /// which itself must be inside Assets/.
    /// </summary>
    internal static class PathSafety
    {
        // Path.GetInvalidFileNameChars() depends on the OS (on macOS it is only
        // '/' and '\0'): the repo is shared across OSes, so the list is fixed.
        private static readonly char[] Forbidden = { '<', '>', ':', '"', '|', '?', '*', '\\', '/' };

        /// <summary>"Assets/ImportedAssets/" → "Assets/ImportedAssets". Throws when outside Assets/.</summary>
        public static string NormalizeTarget(string target)
        {
            var t = (target ?? "").Replace('\\', '/').Trim().TrimEnd('/');
            if (t != "Assets" && !t.StartsWith("Assets/", StringComparison.Ordinal))
                throw new InvalidOperationException($"Target Folder must be inside Assets/ (got \"{target}\"). Fix it in Settings.");
            foreach (var segment in t.Split('/'))
                CheckSegment(segment, target);
            return t;
        }

        /// <summary>
        /// Target folder + a relative path from the server, as a project-relative
        /// path ("Assets/…"). Throws instead of writing anywhere unexpected.
        /// </summary>
        public static string Combine(string target, string relative)
        {
            var root = NormalizeTarget(target);
            if (string.IsNullOrWhiteSpace(relative))
                throw new InvalidOperationException("The server sent an empty path.");

            var rel = relative.Replace('\\', '/');
            if (rel.StartsWith("/", StringComparison.Ordinal) || Path.IsPathRooted(rel))
                throw new InvalidOperationException($"Refusing an absolute path from the server: \"{relative}\".");
            foreach (var segment in rel.Split('/'))
                CheckSegment(segment, relative);

            var result = root + "/" + rel;

            // Belt and braces: the resolved path must still be under the root.
            var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var full = Path.GetFullPath(result);
            if (!full.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Refusing a path outside the Target Folder: \"{relative}\".");

            return result;
        }

        /// <summary>Same path, ignoring slash direction and case (Windows/macOS checkouts).</summary>
        public static bool SamePath(string a, string b) =>
            string.Equals((a ?? "").Replace('\\', '/').TrimEnd('/'), (b ?? "").Replace('\\', '/').TrimEnd('/'),
                StringComparison.OrdinalIgnoreCase);

        /// <summary>A single folder/file name from free text: forbidden characters become "_".</summary>
        public static string SanitizeSegment(string name)
        {
            var s = (name ?? "").Trim();
            foreach (var c in Forbidden)
                s = s.Replace(c, '_');
            var chars = s.ToCharArray();
            for (var i = 0; i < chars.Length; i++)
                if (chars[i] < 32) chars[i] = '_';
            s = new string(chars).Trim().TrimEnd('.');
            return s.Length == 0 || s == "." || s == ".." ? "_" : s;
        }

        private static void CheckSegment(string segment, string whole)
        {
            if (segment.Length == 0 || segment == "." || segment == ".." || segment.Trim() != segment || segment.EndsWith(".", StringComparison.Ordinal))
                throw new InvalidOperationException($"Refusing path \"{whole}\": invalid segment \"{segment}\".");
            if (segment.IndexOfAny(Forbidden) >= 0)
                throw new InvalidOperationException($"Refusing path \"{whole}\": forbidden character in \"{segment}\".");
            foreach (var c in segment)
                if (c < 32)
                    throw new InvalidOperationException($"Refusing path \"{whole}\": control character.");
        }
    }
}
