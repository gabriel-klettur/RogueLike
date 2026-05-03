using System.Text;
using UnityEngine;
using UnityEngine.UI;
using Valkur.Core;
using Valkur.Infrastructure.Persistence.Profile;

namespace Valkur.Gameplay.HUD
{
    /// <summary>
    /// Modal "Statistics" panel that reads the bound <see cref="IProfileDb"/>
    /// and renders aggregate run/kill/profile data as text. Designed as a
    /// main-menu tab today; the same component drops into the pause menu
    /// later without changes (the Open/Close API is identical to
    /// <see cref="SkillTreeHUD"/>).
    ///
    /// Sections:
    ///   1. Lifetime profile counters (total runs, total playtime, average
    ///      run duration).
    ///   2. Top 5 monster kills (entity_key + count).
    ///   3. Recent run history (last 10 runs: started_at, duration, kills,
    ///      depth, killed_by). Most-recent first.
    ///
    /// Built procedurally on first Open. Replace EnsureBuilt with a
    /// prefab Canvas later for richer visuals.
    /// </summary>
    public sealed class StatisticsHUD : SingletonMonoBehaviour<StatisticsHUD>
    {
        [Tooltip("Profile DB the panel renders. Auto-resolved from " +
                 "ServiceLocator on first Open if null.")]
        [SerializeField] private bool autoResolveDb = true;

        private IProfileDb _db;

        private Canvas _canvas;
        private GameObject _root;
        private Text _textLabel;

        public bool IsOpen { get; private set; }
        public IProfileDb Db => _db;

        protected override bool Persist => false;

        public void BindDb(IProfileDb db)
        {
            _db = db;
            if (IsOpen) Refresh();
        }

        public void Toggle() { if (IsOpen) Close(); else Open(); }

        public void Open()
        {
            EnsureBuilt();
            if (_db == null && autoResolveDb)
                ServiceLocator.TryGet(out _db);

            IsOpen = true;
            if (_root != null) _root.SetActive(true);
            Refresh();
        }

        public void Close()
        {
            IsOpen = false;
            if (_root != null) _root.SetActive(false);
        }

        protected override void OnSingletonAwake()
        {
            EnsureBuilt();
        }

        // ── Internal ───────────────────────────────────────────────────────────

        // Test seam — produces the textual representation without a Canvas.
        public string ComputeStatsText()
        {
            if (_db == null) return string.Empty;

            var sb = new StringBuilder();

            sb.AppendLine("──── Lifetime ────");
            int totalRuns = _db.Profile.GetInt("total_runs");
            float playtimeSec = _db.Profile.GetFloat("total_playtime_sec");
            float avgSec = _db.Runs.AverageDurationSeconds();
            sb.AppendLine($"  Total runs: {totalRuns}");
            sb.AppendLine($"  Total playtime: {FormatDuration(playtimeSec)}");
            sb.AppendLine($"  Average run: {FormatDuration(avgSec)}");
            sb.AppendLine($"  Achievements: {_db.Achievements.UnlockedCount()}");
            sb.AppendLine();

            sb.AppendLine("──── Top monster kills ────");
            var top = _db.KillStats.GetTop(5);
            if (top.Count == 0)
            {
                sb.AppendLine("  (no kills yet)");
            }
            else
            {
                foreach (var k in top)
                    sb.AppendLine($"  {k.entityKey}: {k.totalKills}");
            }
            sb.AppendLine();

            sb.AppendLine("──── Recent runs ────");
            var runs = _db.Runs.GetAll();
            int shown = 0;
            for (int i = 0; i < runs.Count && shown < 10; i++, shown++)
            {
                var r = runs[i];
                string when = string.IsNullOrEmpty(r.startedAtIso) ? "?" : r.startedAtIso.Substring(0, 10);
                string killer = string.IsNullOrEmpty(r.killedBy) ? "alive" : r.killedBy;
                sb.AppendLine($"  {when}  {FormatDuration(r.durationSeconds)}  " +
                              $"kills={r.totalKills}  depth={r.depthReached}  ({killer})");
            }
            if (runs.Count == 0) sb.AppendLine("  (no runs yet)");

            return sb.ToString();
        }

        private static string FormatDuration(float seconds)
        {
            if (seconds <= 0f) return "—";
            int total = Mathf.RoundToInt(seconds);
            int h = total / 3600;
            int m = (total % 3600) / 60;
            int s = total % 60;
            if (h > 0) return $"{h}h {m:00}m {s:00}s";
            if (m > 0) return $"{m}m {s:00}s";
            return $"{s}s";
        }

        private void Refresh()
        {
            if (_textLabel == null) return;
            _textLabel.text = ComputeStatsText();
        }

        public void EnsureBuilt()
        {
            if (_canvas != null) return;

            _root = new GameObject("StatisticsHUD_Root");
            _root.transform.SetParent(transform, false);

            _canvas = _root.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 70;
            _root.AddComponent<CanvasScaler>().uiScaleMode =
                CanvasScaler.ScaleMode.ScaleWithScreenSize;
            _root.AddComponent<GraphicRaycaster>();

            // Centred modal panel.
            var panel = new GameObject("Panel");
            panel.transform.SetParent(_root.transform, false);
            var panelRt = panel.AddComponent<RectTransform>();
            panelRt.anchorMin = new Vector2(0.18f, 0.12f);
            panelRt.anchorMax = new Vector2(0.82f, 0.88f);
            panelRt.offsetMin = panelRt.offsetMax = Vector2.zero;
            var panelImg = panel.AddComponent<Image>();
            panelImg.color = new Color(0f, 0f, 0f, 0.88f);

            var header = new GameObject("Header");
            header.transform.SetParent(panel.transform, false);
            var hdrRt = header.AddComponent<RectTransform>();
            hdrRt.anchorMin = new Vector2(0, 0.93f);
            hdrRt.anchorMax = new Vector2(1, 1);
            hdrRt.offsetMin = hdrRt.offsetMax = Vector2.zero;
            var hdrLabel = header.AddComponent<Text>();
            hdrLabel.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            hdrLabel.alignment = TextAnchor.MiddleCenter;
            hdrLabel.color = Color.white;
            hdrLabel.fontSize = 18;
            hdrLabel.fontStyle = FontStyle.Bold;
            hdrLabel.text = "Statistics";

            var bodyGo = new GameObject("Body");
            bodyGo.transform.SetParent(panel.transform, false);
            var bodyRt = bodyGo.AddComponent<RectTransform>();
            bodyRt.anchorMin = new Vector2(0.04f, 0.04f);
            bodyRt.anchorMax = new Vector2(0.96f, 0.92f);
            bodyRt.offsetMin = bodyRt.offsetMax = Vector2.zero;
            _textLabel = bodyGo.AddComponent<Text>();
            _textLabel.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _textLabel.alignment = TextAnchor.UpperLeft;
            _textLabel.color = new Color(0.95f, 0.95f, 0.85f);
            _textLabel.fontSize = 14;
            _textLabel.text = "";

            _root.SetActive(false);
        }
    }
}
