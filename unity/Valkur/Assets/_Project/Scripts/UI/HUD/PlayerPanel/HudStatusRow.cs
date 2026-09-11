using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Valkur.Data;
using Valkur.Gameplay.Combat;

namespace Valkur.UI.HUD
{
    /// <summary>
    /// The player's status effects, as the same glyphs the bars over the head draw.
    ///
    /// <para>Burn, Poison, Stun, Freeze, Slow, Root, Vulnerable and Marked were readable only
    /// above the character, where the eye is on the fight rather than on the numbers. The corner
    /// is where a player looks to DECIDE — whether to drink, whether to dash out of a puddle —
    /// so the row sits beside the slots. Glyphs come from <c>Combat.StatusGlyphs</c> and tints
    /// from <c>WorldBarStyle.StatusTint</c>: one table and one palette for both readouts.</para>
    ///
    /// <para><b>Polled</b>, not subscribed: the apply/remove events cover membership and say
    /// nothing about the last second of a status, which is the half a player acts on. Each tile
    /// carries a shrinking duration line and blinks through its last 1.5 seconds.</para>
    /// </summary>
    public sealed class HudStatusRow
    {
        private const int TileW = 9;
        private const int TileH = 10;
        private const float ExpiryWarning = 1.5f;

        public RectTransform Root { get; }

        private readonly Image[] _tiles;
        private readonly Image[] _glyphs;
        private readonly Image[] _lines;
        private readonly HudArt _art;
        private readonly List<StatusEffect> _buffer = new List<StatusEffect>(8);
        private readonly HudPixelText _overflow;
        private int _shown;

        /// <summary>How many tiles are showing.</summary>
        public int Shown => _shown;

        public HudStatusRow(Transform parent, HudArt art, int x, int y, int maxTiles)
        {
            _art = art;
            Root = HudRect.Make("Status", parent, x, y, maxTiles * (TileW + 1), TileH);
            _tiles = new Image[maxTiles];
            _glyphs = new Image[maxTiles];
            _lines = new Image[maxTiles];
            for (int i = 0; i < maxTiles; i++)
            {
                int tx = i * (TileW + 1);
                _tiles[i] = HudRect.MakeImage("Tile" + i, Root, art.StatusTile, tx, 0, TileW, TileH, Image.Type.Sliced);
                _glyphs[i] = HudRect.MakeImage("Glyph" + i, Root, null, tx + 1, 3, 6, 6);
                _lines[i] = HudRect.MakeImage("Line" + i, Root, art.White, tx + 1, 1, TileW - 2, 1);
                _tiles[i].enabled = _glyphs[i].enabled = _lines[i].enabled = false;
            }
            _overflow = HudPixelText.Create(Root, "More", art, HudFontFace.Small, HudTextAlign.Right,
                                            0, 0, Root.sizeDelta.x > 0 ? (int)Root.sizeDelta.x : 1, TileH);
        }

        public void Tick(StatusEffectManager manager)
        {
            _buffer.Clear();
            if (manager != null) manager.CopyActiveTo(_buffer);
            _buffer.Sort((a, b) => ((int)a.Kind).CompareTo((int)b.Kind));

            var palette = WorldBarStyle.Active;
            float now = Time.time;
            int max = _tiles.Length;
            bool overflow = _buffer.Count > max;
            int count = overflow ? max - 1 : Mathf.Min(_buffer.Count, max);
            _shown = count;

            for (int i = 0; i < max; i++)
            {
                bool on = i < count;
                if (_tiles[i].enabled != on) _tiles[i].enabled = on;
                if (_glyphs[i].enabled != on) _glyphs[i].enabled = on;
                if (!on)
                {
                    if (_lines[i].enabled) _lines[i].enabled = false;
                    continue;
                }

                var effect = _buffer[i];
                int kind = (int)effect.Kind;
                var sprite = kind >= 0 && kind < _art.StatusGlyph.Length ? _art.StatusGlyph[kind] : null;
                if (_glyphs[i].sprite != sprite) _glyphs[i].sprite = sprite;

                float remaining = effect.EndTime - now;
                float blink = 1f;
                if (remaining <= ExpiryWarning)
                    blink = Mathf.Lerp(0.35f, 1f, Mathf.Sin(now * 12f) * 0.5f + 0.5f);
                var tint = palette != null ? palette.StatusTint(kind) : Color.white;
                var c = tint;
                c.a *= blink;
                if (_glyphs[i].color != c) _glyphs[i].color = c;

                bool line = effect.Duration > 0f;
                if (_lines[i].enabled != line) _lines[i].enabled = line;
                if (line)
                {
                    int w = Mathf.Clamp(Mathf.CeilToInt((TileW - 2) * Mathf.Clamp01(remaining / effect.Duration)), 0, TileW - 2);
                    var rt = _lines[i].rectTransform;
                    if ((int)rt.sizeDelta.x != w) rt.sizeDelta = new Vector2(w, 1f);
                    var lc = Color.Lerp(tint, Color.white, 0.35f);
                    if (_lines[i].color != lc) _lines[i].color = lc;
                }
            }

            _overflow.SetText(overflow ? "+" + (_buffer.Count - count) : "");
        }
    }
}
