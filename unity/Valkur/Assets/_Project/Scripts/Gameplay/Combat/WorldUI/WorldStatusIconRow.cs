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
    /// </summary>
    internal sealed class WorldStatusIconRow
    {
        private const float POLL_INTERVAL = 0.15f;

        /// <summary>Seconds of blinking before a status runs out.</summary>
        private const float EXPIRY_WARNING = 1.2f;

        private readonly Transform _root;
        private readonly SpriteRenderer[] _icons;
        private readonly SpriteRenderer _overflow;
        private readonly float _side;
        private readonly float _gap;

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
            _gap = WorldBarGeometry.Texels(style.iconGapTexels);

            var go = new GameObject("Status");
            _root = go.transform;
            _root.SetParent(parent, false);
            _root.localRotation = Quaternion.identity;
            _root.localScale = Vector3.one;
            _root.gameObject.SetActive(false);

            _icons = new SpriteRenderer[Mathf.Max(1, style.maxIcons)];
            for (int i = 0; i < _icons.Length; i++)
                _icons[i] = MakePart("Icon" + i, sortingBase);
            _overflow = MakePart("Overflow", sortingBase);
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
            for (int i = 0; i < _icons.Length; i++) _icons[i].sortingOrder = order;
            _overflow.sortingOrder = order;
        }

        public void Layout(float centreY)
        {
            _centreY = centreY;
            _root.localPosition = new Vector3(0f, centreY, 0f);
        }

        public void SetAlpha(float alpha)
        {
            _alpha = alpha;
        }

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

            // Blink whatever is about to run out. Only the icons in the warning window are
            // written, so a steady row of statuses costs nothing per frame.
            for (int i = 0; i < _visible && i < _buffer.Count; i++)
            {
                float remaining = _buffer[i].EndTime - Time.time;
                if (remaining > EXPIRY_WARNING) continue;
                float phase = Mathf.Sin(Time.time * 12f) * 0.5f + 0.5f;
                float a = Mathf.Lerp(0.35f, 1f, phase);
                var c = TintFor(KindIndex(_buffer[i]));
                c.a *= _alpha * a;
                if (_icons[i].color != c) _icons[i].color = c;
            }
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
                    if (_icons[i].gameObject.activeSelf) _icons[i].gameObject.SetActive(false);
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
                if (!on) continue;

                int kind = KindIndex(_buffer[i]);
                _shownKinds.Add(kind);
                var sprite = WorldBarArt.Icon((StatusEffectKind)kind);
                if (sprite == null)
                {
                    // No drawing for this kind: leave the slot empty rather than showing another
                    // status's glyph, which would be a lie the player cannot check.
                    sr.gameObject.SetActive(false);
                    continue;
                }
                sr.sprite = sprite;
                sr.size = new Vector2(_side, _side);
                sr.transform.localPosition = new Vector3(WorldBarGeometry.SnapToTexel(x), 0f, 0f);
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
                var c = Color.white;
                c.a *= _alpha;
                _overflow.color = c;
            }
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
