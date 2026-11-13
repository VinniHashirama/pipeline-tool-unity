using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using AntiGravity.PipelineTool.Editor.Models;
using UnityEditor;
using UnityEngine;

namespace AntiGravity.PipelineTool.Editor
{
    /// <summary>
    /// Where the blendshape map (v0.9) lives between imports: the ModelImporter's userData,
    /// saved in the .meta. JSON with a key of its own — {"hopper_shape_map":[…]} — so a
    /// userData someone else wrote (a JSON object) keeps its keys, and readers that parse it
    /// with JsonUtility just ignore ours. A userData that is not a JSON object is left alone.
    /// </summary>
    internal static class ShapeMapStore
    {
        private const string Key = "hopper_shape_map";

        [Serializable]
        private class Envelope
        {
            public ShapeMapRow[] hopper_shape_map;
        }

        /// <summary>The map saved in the importer, or null when there is none.</summary>
        public static ShapeMapRow[] Read(string userData)
        {
            if (string.IsNullOrEmpty(userData) || userData.IndexOf($"\"{Key}\"", StringComparison.Ordinal) < 0)
                return null;
            try
            {
                var rows = JsonUtility.FromJson<Envelope>(userData)?.hopper_shape_map;
                return rows != null && rows.Length > 0 ? rows : null;
            }
            catch (ArgumentException)
            {
                return null; // not JSON
            }
        }

        /// <summary>
        /// Writes the map into importer.userData (the caller saves the importer). Empty map:
        /// removes ours. Returns a note for the status line when it could not be written.
        /// </summary>
        public static string Write(ModelImporter importer, ShapeMapRow[] rows)
        {
            var rest = Strip(importer.userData ?? "", out var ok);
            if (!ok)
            {
                if (rows == null || rows.Length == 0) return null; // nothing of ours to write or remove
                var msg = $"{Path.GetFileName(importer.assetPath)}: the importer's userData is used by something else — blendshape map not applied.";
                Debug.LogWarning($"[Hopper] {msg}");
                return msg;
            }

            string value;
            if (rows == null || rows.Length == 0)
            {
                value = rest;
            }
            else
            {
                var ours = JsonUtility.ToJson(new Envelope { hopper_shape_map = rows });
                var inner = rest.Trim();
                // rest is "" or a JSON object: our key goes first, the other keys after it.
                value = inner.Length <= 2
                    ? ours
                    : ours.Substring(0, ours.Length - 1) + "," + inner.Substring(1);
            }
            if (value.Trim() == "{}") value = "";
            if (value != (importer.userData ?? "")) importer.userData = value;
            return null;
        }

        /// <summary>
        /// userData without our key. ok = false when userData is not empty and not a JSON
        /// object (someone else's format: we do not touch it).
        /// </summary>
        private static string Strip(string userData, out bool ok)
        {
            ok = true;
            var text = userData.Trim();
            if (text.Length == 0) return "";
            if (text[0] != '{' || text[text.Length - 1] != '}') { ok = false; return userData; }

            // Find "hopper_shape_map" as a key at depth 1, skipping string contents.
            var depth = 0;
            for (var i = 0; i < text.Length; i++)
            {
                var ch = text[i];
                if (ch == '"')
                {
                    var end = StringEnd(text, i);
                    if (end < 0) { ok = false; return userData; }
                    if (depth == 1 && text.Substring(i + 1, end - i - 1) == Key)
                    {
                        var colon = SkipSpaces(text, end + 1);
                        if (colon < text.Length && text[colon] == ':')
                        {
                            var valueEnd = ValueEnd(text, SkipSpaces(text, colon + 1));
                            if (valueEnd < 0) { ok = false; return userData; }
                            return Remove(text, i, valueEnd);
                        }
                    }
                    i = end;
                    continue;
                }
                if (ch == '{' || ch == '[') depth++;
                else if (ch == '}' || ch == ']') depth--;
            }
            return text;
        }

        // Removes [start, end) plus one comma next to it, so the object stays valid.
        private static string Remove(string text, int start, int end)
        {
            var after = SkipSpaces(text, end);
            if (after < text.Length && text[after] == ',')
                return text.Substring(0, start) + text.Substring(SkipSpaces(text, after + 1));
            var before = start - 1;
            while (before >= 0 && char.IsWhiteSpace(text[before])) before--;
            if (before >= 0 && text[before] == ',')
                return text.Substring(0, before) + text.Substring(end);
            return text.Substring(0, start) + text.Substring(end);
        }

        // Index of the closing quote of the string that opens at start, or -1.
        private static int StringEnd(string text, int start)
        {
            for (var i = start + 1; i < text.Length; i++)
            {
                if (text[i] == '\\') { i++; continue; }
                if (text[i] == '"') return i;
            }
            return -1;
        }

        // Index right after the JSON value that starts at start, or -1.
        private static int ValueEnd(string text, int start)
        {
            if (start >= text.Length) return -1;
            if (text[start] == '"') { var e = StringEnd(text, start); return e < 0 ? -1 : e + 1; }
            if (text[start] == '[' || text[start] == '{')
            {
                var depth = 0;
                for (var i = start; i < text.Length; i++)
                {
                    var ch = text[i];
                    if (ch == '"') { i = StringEnd(text, i); if (i < 0) return -1; continue; }
                    if (ch == '[' || ch == '{') depth++;
                    else if ((ch == ']' || ch == '}') && --depth == 0) return i + 1;
                }
                return -1;
            }
            var j = start;
            while (j < text.Length && text[j] != ',' && text[j] != '}') j++;
            return j;
        }

        private static int SkipSpaces(string text, int i)
        {
            while (i < text.Length && char.IsWhiteSpace(text[i])) i++;
            return i;
        }
    }

    /// <summary>
    /// Applies the blendshape map (v0.9) to every clip of a model whose importer carries one:
    /// a curve on blendShape.&lt;from&gt; moves to blendShape.&lt;to&gt;, or is dropped when to is "".
    /// Runs inside the model import, so the result is the imported clip — on Hopper's import
    /// and on any manual reimport after it.
    /// </summary>
    internal class ShapeMapPostprocessor : AssetPostprocessor
    {
        private const string Prefix = "blendShape.";

        private void OnPostprocessAnimation(GameObject root, AnimationClip clip)
        {
            if (!(assetImporter is ModelImporter importer)) return;
            var rows = ShapeMapStore.Read(importer.userData);
            if (rows == null) return;
            Apply(clip, rows, $"{Path.GetFileName(assetPath)} · {clip.name}");
        }

        private struct Move
        {
            public EditorCurveBinding Source;
            public EditorCurveBinding Target; // propertyName null = drop
            public string Label;
        }

        internal static void Apply(AnimationClip clip, ShapeMapRow[] rows, string where)
        {
            var bindings = AnimationUtility.GetCurveBindings(clip);
            var shapes = bindings
                .Where(b => b.type == typeof(SkinnedMeshRenderer) && b.propertyName != null
                            && b.propertyName.StartsWith(Prefix, StringComparison.Ordinal))
                .ToList();
            if (shapes.Count == 0) return;

            var moves = new List<Move>();
            var taken = new HashSet<string>();
            foreach (var row in rows)
            {
                if (row == null || string.IsNullOrEmpty(row.from) || row.from == row.to) continue;

                var matches = shapes.Where(b => Matches(ShapeName(b), row.from)).ToList();
                if (matches.Count == 0) continue;
                var onMesh = matches.Where(b => SameMesh(b.path, row.mesh)).ToList();
                List<EditorCurveBinding> chosen;
                if (onMesh.Count > 0) chosen = onMesh;
                else if (matches.Count == 1) chosen = matches; // the mesh did not match: by shape name, if unique
                else
                {
                    Debug.LogWarning($"[Hopper] {where}: blendshape '{row.from}' is on {matches.Count} meshes and none is '{row.mesh}' — row skipped.");
                    continue;
                }

                foreach (var b in chosen)
                {
                    if (!taken.Add(Key(b))) continue; // already moved by an earlier row
                    var name = ShapeName(b);
                    var move = new Move { Source = b };
                    if (string.IsNullOrEmpty(row.to))
                    {
                        move.Label = $"{name} removed";
                    }
                    else
                    {
                        // Same form as the binding: full (Expressions.blink) or short (blink).
                        var target = name == row.from ? row.to : ShortName(row.to);
                        move.Target = EditorCurveBinding.FloatCurve(b.path, typeof(SkinnedMeshRenderer), Prefix + target);
                        move.Label = $"{name} -> {target}";
                    }
                    moves.Add(move);
                }
            }
            if (moves.Count == 0) return;

            // A target the clip already animates (and that is not moving away) is never overwritten.
            var leaving = new HashSet<string>(moves.Select(m => Key(m.Source)));
            var occupied = new HashSet<string>(bindings.Select(Key).Where(k => !leaving.Contains(k)));
            var applied = new List<Move>();
            foreach (var m in moves)
            {
                if (m.Target.propertyName == null) { applied.Add(m); continue; }
                if (!occupied.Add(Key(m.Target)))
                {
                    Debug.LogWarning($"[Hopper] {where}: the clip already animates '{ShapeName(m.Target)}' on '{m.Target.path}' — '{ShapeName(m.Source)}' left as is.");
                    continue;
                }
                applied.Add(m);
            }
            if (applied.Count == 0) return;

            // Read every curve before removing any: rows may swap two shapes.
            var curves = applied.Select(m => AnimationUtility.GetEditorCurve(clip, m.Source)).ToList();
            foreach (var m in applied) AnimationUtility.SetEditorCurve(clip, m.Source, null);
            for (var i = 0; i < applied.Count; i++)
                if (applied[i].Target.propertyName != null && curves[i] != null)
                    AnimationUtility.SetEditorCurve(clip, applied[i].Target, curves[i]);

            var moved = applied.Count(m => m.Target.propertyName != null);
            var summary = new StringBuilder($"[Hopper] {where}: blendshape map — {moved} curve(s) redirected, {applied.Count - moved} removed (");
            summary.Append(string.Join(", ", applied.Select(m => m.Label))).Append(").");
            Debug.Log(summary.ToString());
        }

        private static string ShapeName(EditorCurveBinding b) => b.propertyName.Substring(Prefix.Length);

        // The binding may carry the full name (Expressions.blink) or only the target (blink).
        private static bool Matches(string shape, string from) => shape == from || shape == ShortName(from);

        private static string ShortName(string name)
        {
            var dot = name.LastIndexOf('.');
            return dot < 0 ? name : name.Substring(dot + 1);
        }

        // mesh is the node name in the FBX; the binding path ends in it (Billie/MSH_Billie_Head).
        // A Maya namespace (Billie:MSH_…) may be on one side only.
        private static bool SameMesh(string path, string mesh)
        {
            if (string.IsNullOrEmpty(mesh)) return false;
            var last = path ?? "";
            var slash = last.LastIndexOf('/');
            if (slash >= 0) last = last.Substring(slash + 1);
            return last == mesh || StripNamespace(last) == StripNamespace(mesh);
        }

        private static string StripNamespace(string name)
        {
            var colon = name.LastIndexOf(':');
            return colon < 0 ? name : name.Substring(colon + 1);
        }

        private static string Key(EditorCurveBinding b) => $"{b.path}|{b.type?.FullName}|{b.propertyName}";
    }
}
