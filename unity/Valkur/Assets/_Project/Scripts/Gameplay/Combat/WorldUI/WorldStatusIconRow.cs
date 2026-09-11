using System.Collections.Generic;
using UnityEngine;
using Valkur.Core;
using Valkur.Core.UI;
using Valkur.Data;

namespace Valkur.Gameplay.Combat
{
    /// <summary>
    /// The row of status glyphs above an entity's bars.
    ///
    /// <para>Burn, Poison, Stun, Freeze, Slow, Root, Vulnerable and Marked are all applied by
    /// shipped content, and before this row existed none of them was visible: the body tint was
    /// the only trace, and it shares <c>SpriteTintStack</c> with the hit flash and the death fade.
    /// So "it is burning" and "I just hit it" were the same signal.</para>
    ///
    /// <para><b>Polled, not subscribed.</b> The manager raises apply/remove events and they would
    /// cover membership perfectly — but not the last second before a status ends, which is the
    /// half of the readout a player acts on. One list copy at
    /// <see cref="POLL_INTERVAL"/> answers both questions with one mechanism, and leaves nothing
    /// to unsubscribe on an entity that is about to be destroyed mid-fight.</para>
    ///
    /// <para>Capped at <c>maxIcons</c> and then collapsed into an overflow pip. A row wider than
    /// the creature it describes stops being a readout and becomes a second silhouette.</para>
    ///
    /// <para><b>Every glyph sits on the same dark TILE.</b> The painted glyphs carry only an
    /// outline dilated from their own silhouette, so a flame came out seven texels wide, a drop
    /// five and the slow glyph seven — three different shapes in a row that should read as one
    /// strip, and a timer under each that was sometimes flush and sometimes a pedestal. A uniform
    /// seven-by-eight tile with chamfered corners gives the row one rhythm, and its bottom row IS
    /// the timer: the status's own hue drains out of it and leaves the dark tile behind.</para>
    /// </summary>
    internal sealed class WorldStatusIconRow
    {
        private const float POLL_INTERVAL = 0.15f;

        /// <summary>Seconds of blinking before a status runs out.</summary>
        private const float EXPIRY_WARNING = 1.2f;

        /// <summary>How many consecutive sorting orders the row claims: tiles, glyphs, timers.</summary>
        /// <remarks>The timer needs an order of its OWN: the glyph cell covers the tile's bottom row
        /// too, and at an equal order Unity drew the glyph's dilated outline over the timer on two
        /// icons out of three — measured, the burn timer vanished and the poison one kept two
        /// texels.</remarks>
        internal const int SLOT_COUNT = 3;

        // The tile is as tall as the glyph cell (the slow glyph's outline reaches the cell's top
        // row) and two texels narrower (no glyph's dilated outline reaches its side columns),
        // chamfered by drawing it as two overlapping bands rather than one rectangle. Opaque, and
        // in the outline's own colour, so each glyph's baked outline melts into it instead of
        // showing as a second, slightly darker shape inside the tile.
        private const float TILE_ALPHA = 1f;

        private static readonly Color LineColour = new Color(0.96f, 0.93f, 0.84f, 0.9f);

        // How far a timer's hue is lifted toward white. The raw status tints are mid-value (burn
        // orange, poison lime) and a one-texel line of them on the dark tile read as a gap
        // rather than as a line.
        private const float TIMER_LIFT = 0.3f;

        private readonly Transform _root;
        private readonly SpriteRenderer[] _icons;
        private readonly SpriteRenderer[] _lines;
        private readonly SpriteRenderer[] _tileV;
        private readonly SpriteRenderer[] _tileH;
        private readonly float[] _iconX;
        private readonly SpriteRenderer _overflow;
        private readonly float _side;
        private readonly float _gap;
        private readonly int _tileWTexels;
        private readonly int _tileHTexels;

        private readonly List<StatusEffect> _buffer = new List<StatusEffect>(8);
        private readonly List<int> _shownKinds = new List<int>(8);

        private StatusEffectManager _source;
        private WorldBarStyle _style;
        private float _pollLeft;
        private float _centreY;
        private float _alpha = 1f;
        private int _visible;

        public WorldStatusIconRow(Transform parent, WorldBarStyle style, int sortingBase)
        {
            _style = style;
            _side = WorldBarGeometry.Texels(style.iconTexels);
            _tileHTexels = Mathf.Max(3, style.iconTexels);
            _tileWTexels = Mathf.Max(3, style.iconTexels - 2);
            _gap = WorldBarGeometry.Texels(style.iconGapTexels);

            var go = new GameObject("Status");
            _root = go.transform;
            _root.SetParent(parent, false);
            _root.localRotation = Quaternion.identity;
            _root.localScale = Vector3.one;
            _root.gameObject.SetActive(false);

            _icons = new SpriteRenderer[Mathf.Max(1, style.maxIcons)];
            _lines = new SpriteRenderer[_icons.Length];
            _tileV = new SpriteRenderer[_icons.Length];
            _tileH = new SpriteRenderer[_icons.Length];
            _iconX = new float[_icons.Length];
            for (int i = 0; i < _icons.Length; i++)
            {
                _tileV[i] = MakePart("TileV" + i, sortingBase);
                _tileV[i].sprite = WorldBarArt.Solid;
                _tileH[i] = MakePart("TileH" + i, sortingBase);
                _tileH[i].sprite = WorldBarArt.Solid;
                _icons[i] = MakePart("Icon" + i, sortingBase + 1);
                _lines[i] = MakePart("Duration" + i, sortingBase + 2);
                _lines[i].sprite = WorldBarArt.Solid;
            }
            _overflow = MakePart("Overflow", sortingBase + 1);
            _overflow.sprite = WorldBarArt.Solid;
            _overflow.gameObject.SetActive(false);
        }

        private SpriteRenderer MakePart(string name, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_root, false);
            go.transform.localScale = Vector3.one;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.drawMode = SpriteDrawMode.Sliced;
            sr.sharedMaterial = WorldBarArt.Material;
            sr.sortingLayerName = SortingConfig.LAYER_UI_WORLD;
            sr.sortingOrder = order;
            go.SetActive(false);
            return sr;
        }

        /// <summary>Which manager to read. Null switches the row off entirely.</summary>
        public void Bind(StatusEffectManager source)
        {
            _source = source;
            _pollLeft = 0f;
        }

        /// <summary>How tall the row is when anything is in it — what the layout reserves.</summary>
        public float Height => _side;

        /// <summary>True while at least one glyph is drawn.</summary>
        public bool HasAny => _visible > 0;

        public void SetSortingBase(int order)
        {
            for (int i = 0; i < _icons.Length; i++)
            {
                _tileV[i].sortingOrder = order;
                _tileH[i].sortingOrder = order;
                _icons[i].sortingOrder = order + 1;
                _lines[i].sortingOrder = order + 2;
            }
            _overflow.sortingOrder = order + 1;
        }

        public void Layout(float centreY)
        {
            _centreY = centreY;
            _root.localPosition = new Vector3(0f, centreY, 0f);
        }

        /// <summary>
        /// The rig's fade. Re-applied to every drawn glyph on the next tick: it used to be only
        /// STORED, so the icons ignored the fade and popped in and out while the bars under them
        /// dissolved.
        /// </summary>
        public void SetAlpha(float alpha)
        {
            if (Mathf.Abs(alpha - _alpha) < 0.002f) return;
            _alpha = alpha;
            _colourDirty = true;
        }

        private bool _colourDirty = true;

        /// <summary>
        /// Advance the row. Returns true when a status is present, which the rig treats as a
        /// reason to keep the bars on screen: a poisoned character with full health is news.
        /// </summary>
        public bool Tick(float dt, WorldBarStyle style)
        {
            _style = style;

            _pollLeft -= dt;
            if (_pollLeft <= 0f)
            {
                _pollLeft = POLL_INTERVAL;
                Refresh();
            }

            if (_visible == 0) return false;

            // One pass per frame over at most maxIcons glyphs: the blink of whatever is about to
            // run out, the duration line under each, and the rig's fade. Everything is written
            // only when it changed, so a steady row costs a comparison per icon.
            float t = WorldBarGeometry.TEXEL;
            float now = Time.time;
            for (int i = 0; i < _visible && i < _buffer.Count; i++)
            {
                var sr = _icons[i];
                if (!sr.gameObject.activeSelf) continue;
                var effect = _buffer[i];
                float remaining = effect.EndTime - now;
                float blink = 1f;
                if (remaining <= EXPIRY_WARNING)
                {
                    float phase = Mathf.Sin(now * 12f) * 0.5f + 0.5f;
                    blink = Mathf.Lerp(0.35f, 1f, phase);
                }

                var c = TintFor(KindIndex(effect));
                c.a *= _alpha * blink;
                if (sr.color != c) sr.color = c;

                var tc = _style.outline;
                tc.a *= TILE_ALPHA * _alpha;
                if (_tileV[i].color != tc) { _tileV[i].color = tc; _tileH[i].color = tc; }

                // The timer is the tile's bottom row, inset one texel each side by the chamfer,
                // in the status's own hue lifted toward white. Whole texels: a shrinking line
                // that moves by sub-texel steps smears.
                var line = _lines[i];
                bool wantLine = style.iconDurationLines && effect.Duration > 0f && _alpha > 0.01f;
                float frac = wantLine ? Mathf.Clamp01(remaining / effect.Duration) : 0f;
                float span = WorldBarGeometry.Texels(_tileWTexels - 2);
                float w = wantLine ? Mathf.Ceil(frac * span / t - 1e-4f) * t : 0f;
                bool on = w > 0f;
                if (line.gameObject.activeSelf != on) line.gameObject.SetActive(on);
                if (!on) continue;
                var size = new Vector2(w, t);
                if (line.size != size) line.size = size;
                var pos = new Vector3(_iconX[i] - span * 0.5f + w * 0.5f, TileBottom + t * 0.5f, 0f);
                if (line.transform.localPosition != pos) line.transform.localPosition = pos;
                var lc = Color.Lerp(_style.StatusTint(KindIndex(effect)), Color.white, TIMER_LIFT);
                lc.a = _alpha * blink;
                if (line.color != lc) line.color = lc;
            }

            if (_colourDirty && _overflow.gameObject.activeSelf)
            {
                var oc = LineColour;
                oc.a *= _alpha;
                _overflow.color = oc;
            }
            _colourDirty = false;
            return true;
        }

        private void Refresh()
        {
            _buffer.Clear();
            if (_source != null) _source.CopyActiveTo(_buffer);

            // Stable ORDER matters more than it looks: re-sorting the row every poll would make
            // the icons swap places while the player is reading them. The manager enumerates a
            // dictionary, so the order is arbitrary but does not churn between polls; sorting by
            // the kind's own value makes it deterministic instead.
            _buffer.Sort(CompareByKind);

            int wanted = Mathf.Min(_buffer.Count, _icons.Length);
            bool changed = wanted != _visible;
            if (!changed)
            {
                for (int i = 0; i < wanted; i++)
                    if (i >= _shownKinds.Count || _shownKinds[i] != KindIndex(_buffer[i]))
                    { changed = true; break; }
            }

            _visible = wanted;
            bool anyRow = _buffer.Count > 0;
            if (_root.gameObject.activeSelf != anyRow) _root.gameObject.SetActive(anyRow);
            if (!anyRow)
            {
                // Put the glyphs away too, not just the row that holds them. Leaving them enabled
                // under a disabled parent is invisible today and is one re-parent away from a
                // stale icon reappearing over a creature that no longer carries that status.
                for (int i = 0; i < _icons.Length; i++)
                {
                    if (_icons[i].gameObject.activeSelf) _icons[i].gameObject.SetActive(false);
                    if (_lines[i].gameObject.activeSelf) _lines[i].gameObject.SetActive(false);
                    HideTile(i);
                }
                if (_overflow.gameObject.activeSelf) _overflow.gameObject.SetActive(false);
                _shownKinds.Clear();
                return;
            }
            if (!changed) return;

            _shownKinds.Clear();
            int overflow = _buffer.Count - wanted;
            float overflowW = overflow > 0 ? WorldBarGeometry.Texels(2) + _gap : 0f;
            float total = wanted * _side + Mathf.Max(0, wanted - 1) * _gap + overflowW;
            float x = -total * 0.5f + _side * 0.5f;

            for (int i = 0; i < _icons.Length; i++)
            {
                var sr = _icons[i];
                bool on = i < wanted;
                if (sr.gameObject.activeSelf != on) sr.gameObject.SetActive(on);
                if (!on)
                {
                    if (_lines[i].gameObject.activeSelf) _lines[i].gameObject.SetActive(false);
                    HideTile(i);
                    continue;
                }

                int kind = KindIndex(_buffer[i]);
                _shownKinds.Add(kind);
                var sprite = WorldBarArt.Icon((StatusEffectKind)kind);
                if (sprite == null)
                {
                    // No drawing for this kind: leave the slot empty rather than showing another
                    // status's glyph, which would be a lie the player cannot check.
                    sr.gameObject.SetActive(false);
                    if (_lines[i].gameObject.activeSelf) _lines[i].gameObject.SetActive(false);
                    HideTile(i);
                    continue;
                }
                sr.sprite = sprite;
                sr.size = new Vector2(_side, _side);
                _iconX[i] = WorldBarGeometry.SnapToTexel(x);
                sr.transform.localPosition = new Vector3(_iconX[i], 0f, 0f);
                PlaceTile(i);
                var c = TintFor(kind);
                c.a *= _alpha;
                sr.color = c;
                x += _side + _gap;
            }

            bool showOverflow = overflow > 0;
            if (_overflow.gameObject.activeSelf != showOverflow)
                _overflow.gameObject.SetActive(showOverflow);
            if (showOverflow)
            {
                _overflow.size = new Vector2(WorldBarGeometry.Texels(2), WorldBarGeometry.Texels(2));
                _overflow.transform.localPosition =
                    new Vector3(WorldBarGeometry.SnapToTexel(x - _side * 0.5f + WorldBarGeometry.TEXEL), 0f, 0f);
                var c = LineColour;
                c.a *= _alpha;
                _overflow.color = c;
            }
        }

        /// <summary>Y of the tile's bottom edge in the row's space: the glyph cell's own floor.</summary>
        private float TileBottom => -_side * 0.5f;

        private void PlaceTile(int i)
        {
            float cy = TileBottom + WorldBarGeometry.Texels(_tileHTexels) * 0.5f;
            // Two bands make a chamfered tile: the vertical one full height and a texel in from
            // each side, the horizontal one full width and a texel in from top and bottom.
            _tileV[i].size = new Vector2(WorldBarGeometry.Texels(_tileWTexels - 2), WorldBarGeometry.Texels(_tileHTexels));
            _tileH[i].size = new Vector2(WorldBarGeometry.Texels(_tileWTexels), WorldBarGeometry.Texels(_tileHTexels - 2));
            _tileV[i].transform.localPosition = new Vector3(_iconX[i], cy, 0f);
            _tileH[i].transform.localPosition = new Vector3(_iconX[i], cy, 0f);
            if (!_tileV[i].gameObject.activeSelf) _tileV[i].gameObject.SetActive(true);
            if (!_tileH[i].gameObject.activeSelf) _tileH[i].gameObject.SetActive(true);
            // Clear, so the next tick writes the faded colour: a tile that appeared at full
            // opacity for one frame while the rig was still fading in would pop.
            _tileV[i].color = Color.clear;
            _tileH[i].color = Color.clear;
        }

        private void HideTile(int i)
        {
            if (_tileV[i].gameObject.activeSelf) _tileV[i].gameObject.SetActive(false);
            if (_tileH[i].gameObject.activeSelf) _tileH[i].gameObject.SetActive(false);
        }

        /// <summary>
        /// What colour to draw a glyph in.
        ///
        /// <para>The GENERATED glyphs are white silhouettes in the alpha and get their meaning
        /// from <c>statusTints</c>. The hand-drawn ones carry their own hue — an orange flame, a
        /// lime drop, a pink shield — and multiplying a tint into those gives the wrong colour
        /// twice over: the flame tinted orange again goes muddy, and the pale snowflake tinted
        /// lavender stops being ice. A painted glyph is drawn WHITE, which is the identity for a
        /// multiply.</para>
        /// </summary>
        private Color TintFor(int kind)
        {
            if (WorldBarArt.IsPainted(WorldBarSheetLayout.IconId(kind))) return Color.white;
            return _style.StatusTint(kind);
        }

        private static int CompareByKind(StatusEffect a, StatusEffect b)
            => KindIndex(a).CompareTo(KindIndex(b));

        private static int KindIndex(StatusEffect e) => e == null ? -1 : (int)e.Kind;
    }
}
