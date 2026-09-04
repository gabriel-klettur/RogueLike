using System.Text;
using UnityEngine;
using UnityEngine.UI;
using Valkur.Core;
using Valkur.Data;

namespace Valkur.Gameplay.HUD
{
    /// <summary>
    /// The character sheet's CHARACTER tab: every stat, its current value, and where that
    /// value came from.
    ///
    /// This panel is the reason the other eleven systems could not have stayed inert.
    /// Before it, the sheet's "STATS" tab showed lifetime PROFILE statistics — runs
    /// played, achievements, top monsters killed — and nothing anywhere in the game said
    /// how much damage the player dealt or how much defense they had. A number with no
    /// screen is a number nobody can notice is broken, which is how a class's armour value
    /// managed to reach no gameplay code for the life of the project.
    ///
    /// The breakdown is the load-bearing half. "Melee Damage 14" tells a player what they
    /// have; "14 = 2 base + 4 level + 6 equipment + 2 talents" tells them what to do next,
    /// and makes a layer that has silently stopped contributing visible on sight.
    /// </summary>
    public sealed class CharacterStatsHUD : SingletonMonoBehaviour<CharacterStatsHUD>
    {
        [SerializeField] private PlayerStats stats;

        private Canvas _canvas;
        private GameObject _root;
        private Text _textLabel;

        public bool IsOpen { get; private set; }

        protected override bool Persist => false;

        public void Bind(PlayerStats value)
        {
            Unbind();
            stats = value;
            if (stats != null) stats.OnStatsChanged += Refresh;
            if (IsOpen) Refresh();
        }

        public void Open()
        {
            EnsureBuilt();
            if (stats == null) AutoResolve();
            IsOpen = true;
            if (_root != null) _root.SetActive(true);
            Refresh();
        }

        public void Close()
        {
            IsOpen = false;
            if (_root != null) _root.SetActive(false);
        }

        public void Toggle() { if (IsOpen) Close(); else Open(); }

        protected override void OnSingletonAwake() => EnsureBuilt();

        protected override void OnDestroy()
        {
            Unbind();
            base.OnDestroy();
        }

        private void Unbind()
        {
            if (stats != null) stats.OnStatsChanged -= Refresh;
        }

        private void AutoResolve()
        {
            var player = EntityRegistry.PlayerTransform;
            if (player == null) return;
            Bind(player.GetComponent<PlayerStats>());
        }

        /// <summary>
        /// The panel's whole content as text. A test seam as much as a renderer: asserting
        /// on this string is how <c>CharacterSheetStatsTests</c> can check that a talent
        /// purchase actually reaches the screen, rather than only reaching the store.
        /// </summary>
        public string ComputeSheetText()
        {
            if (stats == null) return "(no character)";

            var sb = new StringBuilder();
            var player = EntityRegistry.PlayerTransform;
            var xp = player != null ? player.GetComponent<Experience>() : null;
            var skills = player != null ? player.GetComponent<LearnedSkills>() : null;
            var grimoire = player != null ? player.GetComponent<KnownSpells>() : null;

            sb.AppendLine("──── Character ────");
            if (xp != null)
                sb.AppendLine($"  Level {xp.Level}   ({xp.XpInCurrentLevel}/{xp.XpForNextLevel - xp.XpRequiredForLevel(xp.Level)} XP)");
            if (skills != null)
                sb.AppendLine($"  Skill points: {skills.AvailablePoints}   (spent {skills.SpentPoints})");
            if (grimoire != null)
                sb.AppendLine($"  Arcane points: {grimoire.AvailablePoints}   (spent {grimoire.SpentPoints})");
            sb.AppendLine();

            sb.AppendLine("──── Stats ────");
            foreach (var stat in StatCatalog.All)
            {
                sb.Append("  ");
                sb.Append(StatCatalog.DisplayName(stat).PadRight(20));
                sb.Append(FormatValue(stat, stats.Get(stat)).PadLeft(8));

                string breakdown = FormatBreakdown(stat);
                if (!string.IsNullOrEmpty(breakdown))
                {
                    sb.Append("   = ");
                    sb.Append(breakdown);
                }
                sb.AppendLine();
            }

            return sb.ToString();
        }

        private string FormatBreakdown(StatKind stat)
        {
            var sb = new StringBuilder();
            AppendPart(sb, stat, "base", stats.GetBase(stat));

            foreach (StatLayer layer in System.Enum.GetValues(typeof(StatLayer)))
            {
                if (layer == StatLayer.Base) continue;
                float contribution = stats.GetLayerContribution(stat, layer);
                // A layer contributing nothing is omitted rather than shown as zero: a
                // sheet listing seven "+0" rows per stat is a sheet nobody reads.
                if (Mathf.Abs(contribution) < 0.005f) continue;
                AppendPart(sb, stat, layer.ToString().ToLowerInvariant(), contribution);
            }

            return sb.ToString();
        }

        private static void AppendPart(StringBuilder sb, StatKind stat, string label, float value)
        {
            if (sb.Length > 0) sb.Append(value >= 0f ? " + " : " - ");
            else if (value < 0f) sb.Append("-");

            sb.Append(FormatValue(stat, Mathf.Abs(value)));
            sb.Append(' ');
            sb.Append(label);
        }

        private static string FormatValue(StatKind stat, float value)
        {
            if (StatCatalog.IsPercentage(stat)) return (value * 100f).ToString("0.#") + "%";
            if (StatCatalog.IsInteger(stat)) return Mathf.RoundToInt(value).ToString();
            return value.ToString("0.##");
        }

        private void Refresh()
        {
            if (_textLabel == null) return;
            _textLabel.text = ComputeSheetText();
        }

        public void EnsureBuilt()
        {
            if (_canvas != null) return;

            _root = new GameObject("CharacterStatsHUD_Root");
            _root.transform.SetParent(transform, false);

            _canvas = _root.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 60;
            _root.AddComponent<CanvasScaler>().uiScaleMode =
                CanvasScaler.ScaleMode.ScaleWithScreenSize;
            _root.AddComponent<GraphicRaycaster>();

            // Same rect as SkillTreeHUD and StatisticsHUD: the character sheet's tab strip
            // sits above all three and a panel that moved between tabs would read as the
            // window jumping rather than as the content changing.
            var panel = new GameObject("Panel");
            panel.transform.SetParent(_root.transform, false);
            var panelRt = panel.AddComponent<RectTransform>();
            panelRt.anchorMin = new Vector2(0.2f, 0.15f);
            panelRt.anchorMax = new Vector2(0.8f, 0.85f);
            panelRt.offsetMin = panelRt.offsetMax = Vector2.zero;
            var panelImg = panel.AddComponent<Image>();
            panelImg.color = new Color(0f, 0f, 0f, 0.85f);

            var header = new GameObject("Header");
            header.transform.SetParent(panel.transform, false);
            var hdrRt = header.AddComponent<RectTransform>();
            hdrRt.anchorMin = new Vector2(0, 0.92f);
            hdrRt.anchorMax = new Vector2(1, 1);
            hdrRt.offsetMin = hdrRt.offsetMax = Vector2.zero;
            var hdrLabel = header.AddComponent<Text>();
            hdrLabel.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            hdrLabel.alignment = TextAnchor.MiddleCenter;
            hdrLabel.color = Color.white;
            hdrLabel.fontSize = 18;
            hdrLabel.fontStyle = FontStyle.Bold;
            hdrLabel.text = "Character";

            var body = new GameObject("Body");
            body.transform.SetParent(panel.transform, false);
            var bodyRt = body.AddComponent<RectTransform>();
            bodyRt.anchorMin = new Vector2(0.04f, 0.03f);
            bodyRt.anchorMax = new Vector2(0.96f, 0.90f);
            bodyRt.offsetMin = bodyRt.offsetMax = Vector2.zero;
            _textLabel = body.AddComponent<Text>();
            _textLabel.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _textLabel.alignment = TextAnchor.UpperLeft;
            _textLabel.color = new Color(0.90f, 0.92f, 0.96f);
            _textLabel.fontSize = 14;
            _textLabel.supportRichText = false;

            _root.SetActive(false);
        }
    }
}
