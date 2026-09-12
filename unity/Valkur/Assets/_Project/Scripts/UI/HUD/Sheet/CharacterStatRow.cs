using System;
using UnityEngine;
using UnityEngine.UI;
using Valkur.Data;
using Valkur.Gameplay;
using Valkur.Gameplay.HUD;

namespace Valkur.UI.HUD
{
    /// <summary>
    /// One line of the CHARACTER tab: a name, a value, and a stacked bar saying which layers the
    /// value is made of.
    ///
    /// <para><b>The bar is the point.</b> <c>PlayerStats</c> composes seven layers and
    /// <c>GetLayerContribution</c> has always been able to name each one's share; the old panel
    /// printed them as "14 = 2 base + 4 level + 6 gear" in the same grey as everything else, and
    /// tried to align the columns with <c>PadRight</c> in a proportional font. A bar answers the
    /// same question without reading, and it answers a second one the text could not: a layer
    /// that has silently stopped contributing is a segment that is MISSING, which is visible,
    /// where a "+0 gear" was simply omitted and looked like nothing at all.</para>
    ///
    /// <para>Every segment lands on a whole texel, and the LAST one absorbs the rounding, so the
    /// bar's drawn width is exactly the track's — a one-texel gap at the end of a full bar reads
    /// as a stat that is not quite at its value.</para>
    /// </summary>
    public sealed class CharacterStatRow
    {
        /// <summary>
        /// The layer vocabulary, in declaration order — a CONSTANT derived from the enum itself,
        /// never written after construction, so a domain reload rebuilding it produces exactly
        /// the same array.
        ///
        /// <para>The ratchet only recognises a reset written as <c>stsfld</c> or
        /// <c>field.Clear()</c>, and a <c>static readonly</c> array can be neither — which is why
        /// this is declared safe rather than given a hook it could not satisfy.</para>
        /// </summary>
        [Valkur.Core.SelfHealingStatic("Constant table read from the StatLayer enum; never mutated.")]
        private static readonly StatLayer[] Layers = (StatLayer[])Enum.GetValues(typeof(StatLayer));

        private readonly RectTransform _root;
        private readonly HudPixelText _name;
        private readonly HudPixelText _value;
        private readonly Image[] _segments;
        private readonly SheetPanelChrome _chrome;
        private readonly int _trackX, _trackWidth;

        private float _knockUntil;
        private float _baseX;

        public StatKind Stat { get; }

        /// <summary>Centre of the value column, in the table's texel space. Where motes start.</summary>
        public Vector2 ValueCentre { get; }

        public CharacterStatRow(StatKind stat, Transform parent, SheetPanelChrome chrome,
                                int x, int y, int width)
        {
            Stat = stat;
            _chrome = chrome;
            var style = chrome.Style;
            var theme = chrome.Theme;

            _root = HudRect.Make("Stat_" + stat, parent, x, y, width, style.statRowTexels);
            _baseX = x;

            int valueW = style.valueColumnTexels;
            _name = HudPixelText.Create(_root, "Name", chrome.Art, HudFontFace.Small,
                                        HudTextAlign.Left, 2, style.statRowTexels - 8,
                                        width - valueW - 6, 7);
            _name.color = theme.textDim;
            _name.SetText(StatCatalog.DisplayName(stat).ToUpperInvariant());

            _value = HudPixelText.Create(_root, "Value", chrome.Art, HudFontFace.Small,
                                         HudTextAlign.Right, width - valueW - 2,
                                         style.statRowTexels - 8, valueW, 7);
            _value.color = theme.text;
            ValueCentre = new Vector2(x + width - valueW / 2, y + style.statRowTexels - 5);

            // The track sits under the name, across the width the name may use, so a long stat
            // name and its bar are the same object to the eye.
            _trackX = 2;
            _trackWidth = width - valueW - 6;
            SheetPanelChrome.Tinted("Track", _root, chrome.Art.White, _trackX, 1,
                                    _trackWidth, style.breakdownBarTexels, theme.recess);

            _segments = new Image[Layers.Length];
            for (int i = 0; i < Layers.Length; i++)
            {
                _segments[i] = SheetPanelChrome.Tinted("Seg_" + Layers[i], _root, chrome.Art.White,
                                                       _trackX, 1, 1, style.breakdownBarTexels,
                                                       ColourOf(Layers[i], theme));
                _segments[i].enabled = false;
            }
        }

        /// <summary>
        /// The colour of a layer. Deliberately NOT R6's semantic table: these are provenances,
        /// not resources, and reusing the health green for "base" would make a stat bar look like
        /// a health bar. They are shades of the window's own stone and gold, with the two the
        /// player CHANGES — talents and gear — given the only saturated ones.
        /// </summary>
        private static Color ColourOf(StatLayer layer, HudTheme theme)
        {
            switch (layer)
            {
                // Base is the one layer EVERY stat has, so it is the segment most often on
                // screen — and it was stoneLight on dark stone, which disappeared. Most bars
                // then looked as though they began halfway along their own track.
                case StatLayer.Base:      return theme.textDisabled;
                case StatLayer.Level:     return theme.textDim;
                case StatLayer.Skill:     return theme.gold;
                case StatLayer.Grimoire:  return theme.info;
                case StatLayer.Equipment: return theme.rarityUncommon;
                case StatLayer.Buff:      return theme.rarityEpic;
                case StatLayer.Aura:      return theme.rarityRare;
                default:                  return theme.textDisabled;
            }
        }

        public void SetEmpty()
        {
            _value.SetText("—");
            for (int i = 0; i < _segments.Length; i++) _segments[i].enabled = false;
        }

        /// <summary>Repaints the row: the number, and the bar it is made of.</summary>
        public void SetValue(PlayerStats stats, float value)
        {
            _value.SetText(Format(Stat, value));

            // Magnitudes, because a negative contribution (Anvil Stance costs movement speed)
            // still occupies the bar — it is part of where the number came from. Its segment
            // takes the danger colour so "this took something away" is not drawn as a gift.
            float total = 0f;
            for (int i = 0; i < Layers.Length; i++)
                total += Mathf.Abs(Contribution(stats, Layers[i]));

            if (total <= 0.0001f)
            {
                for (int i = 0; i < _segments.Length; i++) _segments[i].enabled = false;
                return;
            }

            int pen = _trackX;
            int lastVisible = -1;
            for (int i = 0; i < Layers.Length; i++)
            {
                float share = Mathf.Abs(Contribution(stats, Layers[i]));
                int w = Mathf.FloorToInt(share / total * _trackWidth);
                bool show = w > 0;
                _segments[i].enabled = show;
                if (!show) continue;

                var rt = _segments[i].rectTransform;
                rt.anchoredPosition = new Vector2(pen, rt.anchoredPosition.y);
                rt.sizeDelta = new Vector2(w, rt.sizeDelta.y);
                _segments[i].color = Contribution(stats, Layers[i]) < 0f
                    ? _chrome.Theme.danger
                    : ColourOf(Layers[i], _chrome.Theme);
                pen += w;
                lastVisible = i;
            }

            // The last segment absorbs the floor() rounding so the bar ends exactly on the track.
            if (lastVisible >= 0 && pen < _trackX + _trackWidth)
            {
                var rt = _segments[lastVisible].rectTransform;
                rt.sizeDelta = new Vector2(rt.sizeDelta.x + (_trackX + _trackWidth - pen), rt.sizeDelta.y);
            }
        }

        private float Contribution(PlayerStats stats, StatLayer layer)
            => layer == StatLayer.Base ? stats.GetBase(Stat) : stats.GetLayerContribution(Stat, layer);

        /// <summary>Knocks the row one texel aside for a moment. The only thing here that moves.</summary>
        public void Knock(float now) => _knockUntil = now + _chrome.Style.statKnockSeconds;

        public void Tick(float now)
        {
            bool live = now < _knockUntil;
            float wanted = _baseX + (live ? 1f : 0f);
            var p = _root.anchoredPosition;
            if (!Mathf.Approximately(p.x, wanted)) _root.anchoredPosition = new Vector2(wanted, p.y);
        }

        private static string Format(StatKind stat, float value)
        {
            if (StatCatalog.IsPercentage(stat)) return (value * 100f).ToString("0.#") + "%";
            if (StatCatalog.IsInteger(stat)) return Mathf.RoundToInt(value).ToString();
            return value.ToString("0.##");
        }
    }
}
