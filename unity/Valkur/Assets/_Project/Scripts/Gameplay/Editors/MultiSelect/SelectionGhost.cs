using System.Collections.Generic;
using UnityEngine;
using Valkur.UIKit;

namespace Valkur.Gameplay.Editors.MultiSelect
{
    /// <summary>
    /// The translucent preview of what is on the clipboard, following the pointer.
    ///
    /// <para>IT IS THE PASTE POSITION, NOT A DECORATION. The ghost and the paste read the same
    /// anchor arithmetic — each piece sits at its captured offset from the group's anchor, and
    /// the anchor sits on the cursor — so where the ghost is drawn is exactly where the copy
    /// lands. That is what lets the paste have no hidden correction of its own: the Buildings
    /// clipboard nudges its paste up by half the anchor's height so the SPRITE centres on the
    /// cursor, which is right when there is nothing on screen to check it against and wrong
    /// the moment there is.</para>
    ///
    /// <para>A building contributes its real sprites, captured from the live renderers at copy
    /// time. Rebuilding them from the template instead would re-slice the atlas page, which is
    /// the 20 ms-per-call trap this project measured at 60 % of its whole boot. A light or an
    /// emitter has no silhouette, so it contributes a translucent box in its domain colour —
    /// the honest picture of a thing whose position is all there is to preview.</para>
    /// </summary>
    [DisallowMultipleComponent]
    internal sealed class SelectionGhost : MonoBehaviour
    {
        /// <summary>Ghost alpha. High enough to read over any ground, low enough that nobody
        /// mistakes it for something already placed.</summary>
        private const float ALPHA = 0.45f;

        private const string SORTING_LAYER = "VFX";
        private const int    SORTING_ORDER = 5200;   // above the selection outlines (5100)

        /// <summary>One drawn piece of the clipboard, in the group's own anchor space.</summary>
        internal struct Piece
        {
            public Sprite  Sprite;      // null for a domain with no silhouette
            public Vector3 Offset;      // from the group's anchor
            public Vector3 Scale;       // the source renderer's lossy scale, or the box size
            public Color   Tint;        // the domain colour, for a box
            public int     Order;       // relative draw order inside the ghost
        }

        private readonly List<SpriteRenderer> _pieces = new List<SpriteRenderer>(32);
        private static Sprite s_boxSprite;

        /// <summary>
        /// Domain Reload is OFF, so a Sprite cached in a static survives Stop and comes back as
        /// a destroyed Unity object on the next Play. Reset it rather than adding a line to
        /// the unreset-statics baseline.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => s_boxSprite = null;

        /// <summary>Rebuild the ghost from a clipboard. Cheap enough to do on every copy —
        /// a clipboard is tens of pieces, not thousands.</summary>
        public void Build(IReadOnlyList<Piece> pieces)
        {
            Clear();
            if (pieces == null) return;

            for (int i = 0; i < pieces.Count; i++)
            {
                var p = pieces[i];
                var go = new GameObject($"Ghost_{i}");
                go.transform.SetParent(transform, false);
                go.transform.localPosition = p.Offset;
                go.transform.localScale    = p.Scale;

                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = p.Sprite != null ? p.Sprite : BoxSprite();
                var tint  = p.Sprite != null ? Color.white : p.Tint;
                sr.color  = UITheme.WithAlpha(tint, ALPHA);
                sr.sortingLayerName = SORTING_LAYER;
                sr.sortingOrder     = SORTING_ORDER + p.Order;
                _pieces.Add(sr);
            }
        }

        public void Clear()
        {
            for (int i = 0; i < _pieces.Count; i++)
            {
                if (_pieces[i] == null) continue;
                var go = _pieces[i].gameObject;
                go.SetActive(false);
                if (Application.isPlaying) Destroy(go);
                else                       DestroyImmediate(go);
            }
            _pieces.Clear();
        }

        public bool HasPieces => _pieces.Count > 0;

        public void SetVisible(bool visible)
        {
            if (gameObject.activeSelf != visible) gameObject.SetActive(visible);
        }

        /// <summary>Put the group's ANCHOR here. Every piece is already offset from it.</summary>
        public void MoveTo(Vector3 world)
        {
            transform.position = new Vector3(world.x, world.y, 0f);
        }

        /// <summary>A 1x1 white sprite, so a box piece is one scaled quad rather than four
        /// line segments. One texel is enough: it is stretched by the piece's own scale, and a
        /// flat fill has no detail to lose.</summary>
        private static Sprite BoxSprite()
        {
            if (s_boxSprite != null) return s_boxSprite;
            var tex = new Texture2D(1, 1, TextureFormat.RGBA32, mipChain: false)
            {
                filterMode = FilterMode.Point,
                hideFlags  = HideFlags.HideAndDontSave,
            };
            tex.SetPixel(0, 0, Color.white);
            tex.Apply();
            // FullRect, never the default Tight: Tight traces the alpha outline to build a
            // fitted mesh, and the whole point here is a plain quad.
            s_boxSprite = Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f),
                                        pixelsPerUnit: 1f, extrude: 0,
                                        meshType: SpriteMeshType.FullRect);
            s_boxSprite.hideFlags = HideFlags.HideAndDontSave;
            return s_boxSprite;
        }
    }
}
