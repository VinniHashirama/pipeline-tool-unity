using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace AntiGravity.PipelineTool.Editor
{
    /// <summary>Label, color and short glyph of an asset/Item type, as the web app shows it.</summary>
    internal readonly struct TypeInfo
    {
        public readonly string Label;
        public readonly string Glyph;
        public readonly Color  Color;

        public TypeInfo(string label, string glyph, Color color)
        {
            Label = label;
            Glyph = glyph;
            Color = color;
        }
    }

    /// <summary>
    /// Styles of the import window (v0.4): dark card list, tinted type badges,
    /// thumbnail placeholder, amber note. Everything is created lazily on first
    /// use inside OnGUI — EditorStyles is not ready earlier — and recreated when
    /// a domain reload or a skin change leaves it stale. Background textures are
    /// 1x1, HideAndDontSave, and checked with Unity's null (a destroyed texture
    /// compares equal to null) before reuse.
    /// </summary>
    internal static class HopperStyles
    {
        private static readonly Color Gray = Hex(0x94a3b8);

        private static readonly Dictionary<string, TypeInfo> Types = new()
        {
            ["character"]      = new TypeInfo("Character",   "CH", Hex(0xf472b6)),
            ["prop"]           = new TypeInfo("Prop",        "PR", Hex(0x60a5fa)),
            ["environment"]    = new TypeInfo("Environment", "EN", Hex(0x34d399)),
            ["vfx"]            = new TypeInfo("VFX",         "FX", Hex(0xfbbf24)),
            ["texture"]        = new TypeInfo("Texture",     "TX", Hex(0xa78bfa)),
            ["shared_texture"] = new TypeInfo("Texture",     "TX", Hex(0xa78bfa)),
            ["ui"]             = new TypeInfo("UI",          "UI", Gray),
            ["audio"]          = new TypeInfo("Audio",       "AU", Gray),
            ["other"]          = new TypeInfo("Other",       "?",  Gray),
        };

        public static readonly Color Amber = Hex(0xfbbf24);

        private static bool _proSkin;
        private static GUIStyle _card, _title, _subtitle, _glyph, _note, _header;
        private static readonly Dictionary<string, GUIStyle>  Badges   = new();
        private static readonly List<Texture2D>               Textures = new();

        public static TypeInfo Type(string type)
        {
            var key = (type ?? "").Trim().ToLowerInvariant();
            if (Types.TryGetValue(key, out var info)) return info;
            if (key.Length == 0) return Types["other"];
            // Unknown type (material, a new one on the server): its own name, in gray.
            var label = char.ToUpperInvariant(key[0]) + key.Substring(1).Replace('_', ' ');
            return new TypeInfo(label, label.Substring(0, 1), Gray);
        }

        // ------------------------------------------------------------------ //
        // Colors

        public static Color CardBackground => EditorGUIUtility.isProSkin ? Hex(0x1f2126) : Hex(0xe4e4e7);
        public static Color Separator      => EditorGUIUtility.isProSkin ? Hex(0x2e3138) : Hex(0xc8c8cc);
        public static Color Placeholder    => EditorGUIUtility.isProSkin ? Hex(0x2a2d34) : Hex(0xcfcfd4);
        public static Color Muted          => EditorGUIUtility.isProSkin ? Hex(0x9ca3af) : Hex(0x52525b);

        // ------------------------------------------------------------------ //
        // Styles

        public static GUIStyle Card     { get { EnsureFresh(); return _card     ??= BuildCard(); } }
        public static GUIStyle Title    { get { EnsureFresh(); return _title    ??= BuildTitle(); } }
        public static GUIStyle Subtitle { get { EnsureFresh(); return _subtitle ??= BuildSubtitle(); } }
        public static GUIStyle Glyph    { get { EnsureFresh(); return _glyph    ??= BuildGlyph(); } }
        public static GUIStyle Note     { get { EnsureFresh(); return _note     ??= BuildNote(); } }
        public static GUIStyle Header   { get { EnsureFresh(); return _header   ??= BuildHeader(); } }

        /// <summary>Small label with the type's color as text over a faint tint of it.</summary>
        public static GUIStyle Badge(TypeInfo type)
        {
            EnsureFresh();
            if (Badges.TryGetValue(type.Label, out var style) && style.normal.background != null)
                return style;

            var tint = type.Color;
            tint.a = EditorGUIUtility.isProSkin ? 0.18f : 0.28f;
            style = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment   = TextAnchor.MiddleCenter,
                fontStyle   = FontStyle.Bold,
                padding     = new RectOffset(6, 6, 1, 2),
                margin      = new RectOffset(0, 6, 2, 0),
                fixedHeight  = 16,
                stretchWidth = false,
            };
            style.normal.background = Solid(tint);
            style.normal.textColor  = EditorGUIUtility.isProSkin ? type.Color : Color.Lerp(type.Color, Color.black, 0.45f);
            Badges[type.Label] = style;
            return style;
        }

        /// <summary>Drops everything built; the next access rebuilds it.</summary>
        public static void Release()
        {
            foreach (var tex in Textures)
                if (tex != null) Object.DestroyImmediate(tex);
            Textures.Clear();
            Badges.Clear();
            _card = _title = _subtitle = _glyph = _note = _header = null;
        }

        // A skin switch changes every color; a destroyed background (the styles
        // survived, their textures did not) makes the card transparent.
        private static void EnsureFresh()
        {
            var stale = _proSkin != EditorGUIUtility.isProSkin ||
                        (_card != null && _card.normal.background == null) ||
                        (_note != null && _note.normal.background == null);
            if (!stale) return;
            Release();
            _proSkin = EditorGUIUtility.isProSkin;
        }

        private static GUIStyle BuildCard()
        {
            var style = new GUIStyle
            {
                padding = new RectOffset(0, 0, 0, 0),
                margin  = new RectOffset(4, 4, 2, 6),
            };
            style.normal.background = Solid(CardBackground);
            return style;
        }

        private static GUIStyle BuildTitle()
        {
            var style = new GUIStyle(EditorStyles.boldLabel) { fontSize = 12, clipping = TextClipping.Clip };
            style.normal.textColor = EditorGUIUtility.isProSkin ? Hex(0xf4f4f5) : Hex(0x18181b);
            return style;
        }

        private static GUIStyle BuildSubtitle()
        {
            var style = new GUIStyle(EditorStyles.miniLabel) { clipping = TextClipping.Clip };
            style.normal.textColor = Muted;
            return style;
        }

        private static GUIStyle BuildGlyph() =>
            new GUIStyle(EditorStyles.boldLabel) { alignment = TextAnchor.MiddleCenter, fontSize = 11 };

        private static GUIStyle BuildNote()
        {
            var tint = Amber;
            tint.a = EditorGUIUtility.isProSkin ? 0.12f : 0.25f;
            var style = new GUIStyle(EditorStyles.wordWrappedMiniLabel)
            {
                padding = new RectOffset(8, 8, 5, 5),
                margin  = new RectOffset(4, 4, 0, 6),
            };
            style.normal.background = Solid(tint);
            style.normal.textColor  = EditorGUIUtility.isProSkin ? Amber : Hex(0x78350f);
            return style;
        }

        private static GUIStyle BuildHeader() =>
            new GUIStyle(EditorStyles.boldLabel) { fontSize = 12, alignment = TextAnchor.MiddleLeft };

        private static Texture2D Solid(Color color)
        {
            var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
            tex.SetPixel(0, 0, color);
            tex.Apply();
            Textures.Add(tex);
            return tex;
        }

        private static Color Hex(int rgb) =>
            new Color(((rgb >> 16) & 0xff) / 255f, ((rgb >> 8) & 0xff) / 255f, (rgb & 0xff) / 255f, 1f);
    }
}
