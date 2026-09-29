#if UNITY_2021_2_OR_NEWER
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Overlays;
using UnityEngine;
using UnityEngine.UIElements;

namespace AntiGravity.PipelineTool.Editor
{
    /// <summary>
    /// One line in a corner of the Scene View: the frog, a dot with the worst sync state
    /// and a short summary ("All synced", "2 to update · 1 missing · 3 new"), and a button
    /// that re-checks sync status only — it never imports. Clicking the text opens the
    /// Import Window. Reads PipelineSyncStatus; never does I/O itself.
    /// </summary>
#if UNITY_2022_3_OR_NEWER
    [Overlay(typeof(SceneView), Id, "Hopper Sync", true,
        defaultDockZone = DockZone.LeftColumn, defaultDockPosition = DockPosition.Bottom)]
#else
    // No default dock position before 2022: it opens floating, and Unity remembers
    // wherever it is docked.
    [Overlay(typeof(SceneView), Id, "Hopper Sync", true)]
#endif
    public class HopperSyncOverlay : Overlay
    {
        private const string Id = "hopper-sync";

        private static readonly Color Grey = new(0.55f, 0.55f, 0.58f);

        private VisualElement _dot;
        private Label         _label;
        private Button        _refresh;
        private VisualElement _root;

        public override void OnCreated()
        {
            collapsedIcon = AssetDatabase.LoadAssetAtPath<Texture2D>(PipelineImportWindow.IconPath);
            PipelineSyncStatus.Changed += UpdateView;
        }

        public override void OnWillBeDestroyed()
        {
            PipelineSyncStatus.Changed -= UpdateView;
        }

        public override VisualElement CreatePanelContent()
        {
            _root = new VisualElement();
            _root.style.flexDirection = FlexDirection.Row;
            _root.style.alignItems    = Align.Center;
            _root.style.paddingLeft   = 2;
            _root.style.paddingRight  = 2;

            var frog = new Image { image = AssetDatabase.LoadAssetAtPath<Texture2D>(PipelineImportWindow.IconPath) };
            frog.style.width       = 18;
            frog.style.height      = 18;
            frog.style.marginRight = 5;
            _root.Add(frog);

            _dot = new VisualElement();
            _dot.style.width  = 8;
            _dot.style.height = 8;
            _dot.style.borderTopLeftRadius     = 4;
            _dot.style.borderTopRightRadius    = 4;
            _dot.style.borderBottomLeftRadius  = 4;
            _dot.style.borderBottomRightRadius = 4;
            _dot.style.marginRight = 5;
            _root.Add(_dot);

            _label = new Label();
            _label.style.unityTextAlign = TextAnchor.MiddleLeft;
            _label.style.marginRight    = 4;
            _label.AddManipulator(new Clickable(PipelineImportWindow.Open));
            _root.Add(_label);

            // The Editor's own refresh icon: the UI Toolkit font has no "⟳" glyph.
            _refresh = new Button(OnRefresh) { tooltip = "Check sync status again (does not import)" };
            _refresh.style.width         = 22;
            _refresh.style.height        = 20;
            _refresh.style.paddingLeft   = 2;
            _refresh.style.paddingRight  = 2;
            var refreshIcon = new Image { image = EditorGUIUtility.IconContent("Refresh").image, scaleMode = ScaleMode.ScaleToFit };
            refreshIcon.style.width  = 16;
            refreshIcon.style.height = 16;
            refreshIcon.pickingMode  = PickingMode.Ignore;
            _refresh.Add(refreshIcon);
            _root.Add(_refresh);

            // Sign-in and project live in EditorPrefs, which raise no event: a cheap poll
            // (no I/O besides EditorPrefs) keeps the line honest after a sign-out.
            _root.schedule.Execute(UpdateView).Every(2000);
            UpdateView();
            return _root;
        }

        private static void OnRefresh()
        {
            _ = PipelineSyncStatus.RefreshAsync(PipelineSettings.ProjectId);
        }

        private void UpdateView()
        {
            if (_label == null) return;

            if (!PipelineSettings.IsLoggedIn || string.IsNullOrEmpty(PipelineSettings.ProjectId))
            {
                Show(Grey, PipelineSettings.IsLoggedIn ? "Select a project in Hopper" : "Sign in to Hopper",
                    "Open Hopper > Import Window", canRefresh: false);
                return;
            }

            var s = PipelineSyncStatus.Summary;
            var parts = new List<string>();
            if (s.Outdated > 0) parts.Add($"{s.Outdated} to update");
            if (s.Missing  > 0) parts.Add($"{s.Missing} missing");
            if (s.Modified > 0) parts.Add($"{s.Modified} modified");
            if (s.New      > 0) parts.Add($"{s.New} new");
            if (s.Unknown  > 0) parts.Add($"{s.Unknown} unknown");

            Color color;
            if (s.Missing > 0)                        color = PipelineSyncStatus.ColorOf(SyncState.Missing);
            else if (s.Modified > 0)                  color = PipelineSyncStatus.ColorOf(SyncState.ModifiedLocally);
            else if (s.Outdated > 0 || s.New > 0)     color = PipelineSyncStatus.ColorOf(SyncState.Outdated);
            else if (s.Checked && s.Error == null && s.Unknown == 0) color = PipelineSyncStatus.ColorOf(SyncState.Synced);
            else                                      color = Grey;

            string text;
            if (PipelineSyncStatus.IsBusy)  text = "Checking…";
            else if (parts.Count > 0)       text = string.Join(" · ", parts);
            else if (s.Error != null)       text = s.Error;
            else if (s.Checked)             text = "All synced";
            else                            text = "Not checked yet";

            Show(color, text, Tooltip(s), canRefresh: !PipelineSyncStatus.IsBusy);
        }

        private static string Tooltip(SyncSummary s)
        {
            var lines = new List<string>();
            if (s.Error != null) lines.Add(s.Error);
            if (s.Missing > 0)
            {
                lines.Add("Deleted locally:");
                for (var i = 0; i < s.MissingNames.Count && i < 8; i++) lines.Add($"  {s.MissingNames[i]}");
                if (s.MissingNames.Count > 8) lines.Add($"  … and {s.MissingNames.Count - 8} more");
            }
            if (s.New > 0)     lines.Add($"{s.New} published file(s) not imported here");
            if (s.Unknown > 0) lines.Add($"{s.Unknown} tracked file(s) this server does not publish (unpublished, or imported from another server)");
            lines.Add(s.Checked ? $"Last checked {s.CheckedAtUtc.ToLocalTime():HH:mm}" : "Not checked against the server yet");
            lines.Add("Click to open the Hopper window");
            return string.Join("\n", lines);
        }

        private void Show(Color dot, string text, string tooltip, bool canRefresh)
        {
            _dot.style.backgroundColor = dot;
            _label.text    = text;
            _label.tooltip = tooltip;
            _refresh.SetEnabled(canRefresh);
        }
    }
}
#endif
