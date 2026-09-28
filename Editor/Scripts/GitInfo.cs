using System;
using System.Diagnostics;
using System.IO;

namespace AntiGravity.PipelineTool.Editor
{
    /// <summary>
    /// The commit the import was applied on (HEAD of the game repo), sent to
    /// mark-imported. The import itself is committed afterwards by whoever ran
    /// it — this pins the repo state the files landed on. Null when git is not
    /// available or the project is not a repo: the import still goes through.
    /// </summary>
    internal static class GitInfo
    {
        public static string HeadCommit()
        {
            try
            {
                var psi = new ProcessStartInfo("git", "rev-parse HEAD")
                {
                    WorkingDirectory       = Directory.GetCurrentDirectory(), // Unity project root
                    RedirectStandardOutput = true,
                    RedirectStandardError  = true,
                    UseShellExecute        = false,
                    CreateNoWindow         = true,
                };
                using var process = Process.Start(psi);
                if (process == null) return null;
                var output = process.StandardOutput.ReadToEnd().Trim();
                if (!process.WaitForExit(5000) || process.ExitCode != 0) return null;
                return IsHash(output) ? output : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static bool IsHash(string s)
        {
            if (s.Length < 7 || s.Length > 64) return false;
            foreach (var c in s)
                if (!Uri.IsHexDigit(c)) return false;
            return true;
        }
    }
}
