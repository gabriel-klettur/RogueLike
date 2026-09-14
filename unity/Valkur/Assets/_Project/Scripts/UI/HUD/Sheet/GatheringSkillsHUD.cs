using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UI;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay;
using Valkur.Gameplay.World;

namespace Valkur.UI.HUD
{
    /// <summary>
    /// The character sheet's OFICIOS tab: each gathering skill as a number the player climbs, the
    /// goods it has uncovered and the ones still ahead, and — the part that sends them somewhere —
    /// which kinds of node suit their skill right now.
    ///
    /// <para><b>WHY THE NODE LIST IS THE POINT.</b> A gain depends on how hard the node is against
    /// the worker, so a skill whose screen showed only the number would leave the player chopping
    /// the same sapling long after it stopped teaching anything, with nothing on screen saying why
    /// progress had slowed. Every family is listed with its ease in the player's own terms —
    /// "adecuado", "trivial, apenas enseña" — computed by the same pure function the gain uses.</para>
    ///
    /// <para><b>A LOCKED WOOD IS SHOWN, NOT HIDDEN.</b> The tier list is the promise of the skill.
    /// A list filtered to what the player already finds is empty of exactly the thing that makes
    /// the next hour worth it; a locked row with its percent and where to look is a goal.</para>
    ///
    /// <para>Read-only and polled at a slow tick while open: a skill moves by a tenth every few
    /// blows at most, and the panel owns no state a missed frame could corrupt.</para>
    /// </summary>
    public sealed class GatheringSkillsHUD : SingletonMonoBehaviour<GatheringSkillsHUD>
    {
        private const int LeftWidth = 150;
        private const int Gap = 6;
        private const int IconTexels = 11;
        private const int TierRowTexels = 13;
        private const int NodeRowTexels = 9;
        private const float RefreshSeconds = 0.5f;

        private SheetPanelChrome _chrome;
        private bool _built;
        private float _nextRefresh;

        private GatheringSkillDefinition _skill;

        private HudPixelText _name, _percent, _percentSign, _next, _nextWhere, _nodesHeader, _tiersHeader;
        private Image _barFill;
        private int _barWidth;
        private readonly List<Image> _ticks = new List<Image>();
        private readonly List<HudPixelText> _nodeNames = new List<HudPixelText>();
        private readonly List<HudPixelText> _nodeEase = new List<HudPixelText>();
        private readonly List<RawImage> _tierIcons = new List<RawImage>();
        private readonly List<HudPixelText> _tierNames = new List<HudPixelText>();
        private readonly List<HudPixelText> _tierStatus = new List<HudPixelText>();
        private readonly List<HudPixelText> _tierWhere = new List<HudPixelText>();

        public bool IsOpen { get; private set; }
        public SheetPanelChrome Chrome => _chrome;

        protected override bool Persist => false;

        protected override void OnSingletonAwake() => EnsureBuilt();

        protected override void OnDestroy()
        {
            if (_chrome != null) _chrome.Dispose();
            base.OnDestroy();
        }

        public void Open()
        {
            EnsureBuilt();
            IsOpen = true;
            _chrome.Show();
            Refresh();
        }

        public void Close()
        {
            IsOpen = false;
            if (_chrome != null) _chrome.Hide();
        }

        private void Update()
        {
            if (!_built) return;
            _chrome.Tick(Time.unscaledDeltaTime);
            if (!IsOpen || Time.unscaledTime < _nextRefresh) return;
            _nextRefresh = Time.unscaledTime + RefreshSeconds;
            Refresh();
        }

        // ── Build ─────────────────────────────────────────────────────────────

        /// <summary>Builds the window once. Public because Edit Mode never calls Awake.</summary>
        public void EnsureBuilt()
        {
            if (_built) return;
            _built = true;

            _chrome = new SheetPanelChrome(transform, "GatheringSkillsHUD");
            var catalog = GatheringSkillCatalog.Shared;
            _skill = catalog != null && catalog.skills.Count > 0 ? catalog.skills[0] : null;

            var body = _chrome.MakeBody("Body");
            BuildSkillColumn(body);
            BuildTierColumn(body);
        }

        private void BuildSkillColumn(Transform body)
        {
            var theme = _chrome.Theme;
            int h = _chrome.BodyHeight;

            SheetPanelChrome.Tinted("Recess", body, _chrome.Art.BarFrame, 0, 0, LeftWidth, h, theme.recess, Image.Type.Sliced);

            int y = h - 12;
            _name = Text(body, "SkillName", 4, y, LeftWidth - 8, HudFontFace.Small, HudTextAlign.Centre, theme.gold);

            y -= 16;
            _percent = Text(body, "Percent", 4, y, LeftWidth - 20, HudFontFace.Large, HudTextAlign.Centre, theme.text);
            _percentSign = Text(body, "PercentSign", LeftWidth - 30, y, 20, HudFontFace.Small, HudTextAlign.Left, theme.textDim);

            y -= 12;
            _barWidth = LeftWidth - 20;
            // The track is STONE on the recess, not recess on recess: measured on the first
            // capture the two were the same colour and the bar read as a floating 1-texel fill.
            SheetPanelChrome.Tinted("Track", body, _chrome.Art.BarFrameThin, 10, y, _barWidth, 7, theme.stoneLight, Image.Type.Sliced);
            _barFill = SheetPanelChrome.Tinted("Fill", body, _chrome.Art.White, 11, y + 1, 1, 5, theme.gold);

            if (_skill != null && _skill.yieldTable != null)
            {
                foreach (var tier in _skill.yieldTable.tiers)
                {
                    if (tier == null || tier.minSkill <= 0f) continue;
                    int x = 11 + Mathf.RoundToInt(tier.minSkill / 100f * (_barWidth - 2));
                    _ticks.Add(SheetPanelChrome.Tinted("Tick_" + tier.key, body, _chrome.Art.White, x, y - 2, 1, 2, TierColour(tier)));
                }
            }

            y -= 13;
            _next = Text(body, "Next", 4, y, LeftWidth - 8, HudFontFace.Small, HudTextAlign.Centre, theme.textDim);
            y -= 9;
            _nextWhere = Text(body, "NextWhere", 4, y, LeftWidth - 8, HudFontFace.Small, HudTextAlign.Centre, theme.textDisabled);

            y -= 16;
            _nodesHeader = Text(body, "NodesHeader", 6, y, LeftWidth - 12, HudFontFace.Small, HudTextAlign.Left, theme.gold);

            int count = _skill != null ? _skill.trainingNodes.Count : 0;
            for (int i = 0; i < count; i++)
            {
                y -= NodeRowTexels;
                _nodeNames.Add(Text(body, "Node" + i, 8, y, 70, HudFontFace.Small, HudTextAlign.Left, theme.textDim));
                _nodeEase.Add(Text(body, "Ease" + i, 78, y, LeftWidth - 84, HudFontFace.Small, HudTextAlign.Right, theme.textDim));
            }
        }

        private void BuildTierColumn(Transform body)
        {
            var theme = _chrome.Theme;
            int x0 = LeftWidth + Gap;
            int w = _chrome.BodyWidth - x0;
            int y = _chrome.BodyHeight - 12;

            _tiersHeader = Text(body, "TiersHeader", x0 + 2, y, w - 4, HudFontFace.Small, HudTextAlign.Left, theme.gold);

            if (_skill == null || _skill.yieldTable == null) return;

            int iconPx = IconTexels * Mathf.Max(1, _chrome.PixelScale);
            foreach (var tier in _skill.yieldTable.tiers)
            {
                y -= TierRowTexels;

                var iconRt = HudRect.Make("Icon_" + tier.key, body, x0 + 2, y - 2, IconTexels, IconTexels);
                var icon = iconRt.gameObject.AddComponent<RawImage>();
                icon.raycastTarget = false;
                var sprite = tier.items != null && tier.items.Length > 0 && tier.items[0] != null ? tier.items[0].icon : null;
                var baked = HudTextureBaker.Icon(sprite, iconPx);
                icon.texture = baked != null ? (Texture)baked : (sprite != null ? sprite.texture : null);
                _tierIcons.Add(icon);

                _tierNames.Add(Text(body, "Tier_" + tier.key, x0 + 16, y, 96, HudFontFace.Small, HudTextAlign.Left, theme.text));
                _tierStatus.Add(Text(body, "Status_" + tier.key, x0 + 112, y, 34, HudFontFace.Small, HudTextAlign.Right, theme.textDim));
                _tierWhere.Add(Text(body, "Where_" + tier.key, x0 + 152, y, w - 154, HudFontFace.Small, HudTextAlign.Left, theme.textDisabled));
            }
        }

        private HudPixelText Text(Transform parent, string name, int x, int y, int w, HudFontFace face,
            HudTextAlign align, Color colour)
        {
            var t = HudPixelText.Create(parent, name, _chrome.Art, face, align, x, y, w, HudPixelFont.HeightOf(face) + 2);
            t.color = colour;
            return t;
        }

        // ── Refresh ───────────────────────────────────────────────────────────

        private void Refresh()
        {
            if (!_built) return;
            _chrome.SetTitle("Oficios");

            if (_skill == null)
            {
                _chrome.SetFooter("Sin habilidades de recolección");
                return;
            }

            var player = EntityRegistry.PlayerTransform;
            var skills = player != null ? PlayerGatheringSkills.Peek(player.gameObject) : null;
            int tenths = skills != null ? skills.GetTenths(_skill.skillKey) : 0;
            float percent = GatheringSkillDefinition.ToPercent(tenths);
            var theme = _chrome.Theme;

            _name.SetText(_skill.displayName);
            _percent.SetText(percent.ToString("0.0", CultureInfo.InvariantCulture));
            _percentSign.SetText("%");

            // The sign sits against the number's own ink, which moves as "9.9" becomes "10.0".
            var signRt = _percentSign.rectTransform;
            int numberRight = 4 + (LeftWidth - 20 + _percent.InkWidth) / 2;
            signRt.anchoredPosition = new Vector2(numberRight + 2, signRt.anchoredPosition.y);

            var fillRt = _barFill.rectTransform;
            fillRt.sizeDelta = new Vector2(Mathf.Max(1, Mathf.RoundToInt(percent / 100f * (_barWidth - 2))), fillRt.sizeDelta.y);

            RefreshNext(percent);
            RefreshNodes(tenths);
            int reachable = RefreshTiers(percent);

            _tiersHeader.SetText("Maderas");
            _chrome.SetFooter($"{_skill.displayName} {GatheringSkillDefinition.FormatPercent(tenths)}  -  " +
                              $"{reachable} de {_skill.yieldTable.tiers.Count} maderas descubiertas");
        }

        private void RefreshNext(float percent)
        {
            GatheringYieldTable.Tier next = null;
            foreach (var tier in _skill.yieldTable.tiers)
                if (tier != null && tier.minSkill > percent && (next == null || tier.minSkill < next.minSkill))
                    next = tier;

            if (next == null)
            {
                _next.SetText(percent >= 100f ? "Maestría completa" : "Todas descubiertas");
                _nextWhere.SetText(string.Empty);
                return;
            }

            _next.SetText("Siguiente: " + next.displayName);
            _nextWhere.SetText("a " + next.minSkill.ToString("0", CultureInfo.InvariantCulture) + "%" +
                               (string.IsNullOrEmpty(next.whereHint) ? string.Empty : " en " + next.whereHint));
        }

        private void RefreshNodes(int tenths)
        {
            var theme = _chrome.Theme;
            _nodesHeader.SetText("Dónde aprender");

            for (int i = 0; i < _nodeNames.Count && i < _skill.trainingNodes.Count; i++)
            {
                var node = _skill.trainingNodes[i];
                if (node == null) { _nodeNames[i].SetText(string.Empty); _nodeEase[i].SetText(string.Empty); continue; }

                var ease = _skill.Ease(tenths, node.skillDifficulty);
                _nodeNames[i].SetText(string.IsNullOrEmpty(node.nodeDisplayName) ? node.name : node.nodeDisplayName);
                _nodeEase[i].SetText(ShortEase(ease));
                _nodeEase[i].color = EaseColour(ease);
                _nodeNames[i].color = ease == GatheringEase.Suitable ? theme.text : theme.textDim;
            }
        }

        private int RefreshTiers(float percent)
        {
            var theme = _chrome.Theme;
            int reachable = 0;

            for (int i = 0; i < _tierNames.Count; i++)
            {
                var tier = _skill.yieldTable.tiers[i];
                bool unlocked = percent >= tier.minSkill;
                if (unlocked) reachable++;

                _tierNames[i].SetText(tier.displayName);
                _tierNames[i].color = unlocked ? TierColour(tier) : theme.textDisabled;
                _tierIcons[i].color = unlocked ? Color.white : new Color(0.25f, 0.25f, 0.3f, 0.8f);

                int price = tier.items != null && tier.items.Length > 0 && tier.items[0] != null ? tier.items[0].buyPrice : 0;
                _tierStatus[i].SetText(unlocked
                    ? price + " MON"
                    : tier.minSkill.ToString("0", CultureInfo.InvariantCulture) + "%");
                _tierStatus[i].color = unlocked ? theme.gold : theme.textDisabled;

                _tierWhere[i].SetText(tier.whereHint ?? string.Empty);
            }
            return reachable;
        }

        private static string ShortEase(GatheringEase ease)
        {
            switch (ease)
            {
                case GatheringEase.Trivial:  return "trivial";
                case GatheringEase.Easy:     return "fácil";
                case GatheringEase.Suitable: return "adecuado";
                case GatheringEase.Hard:     return "difícil";
                default:                     return "muy difícil";
            }
        }

        private Color EaseColour(GatheringEase ease)
        {
            var theme = _chrome.Theme;
            switch (ease)
            {
                case GatheringEase.Suitable: return theme.success;
                case GatheringEase.Easy:     return theme.info;
                case GatheringEase.Hard:     return theme.warning;
                case GatheringEase.VeryHard: return theme.danger;
                default:                     return theme.textDisabled;
            }
        }

        private static Color TierColour(GatheringYieldTable.Tier tier)
        {
            var item = tier.items != null && tier.items.Length > 0 ? tier.items[0] : null;
            return item != null ? RarityPalette.Color(item.rarity) : Color.white;
        }
    }
}
