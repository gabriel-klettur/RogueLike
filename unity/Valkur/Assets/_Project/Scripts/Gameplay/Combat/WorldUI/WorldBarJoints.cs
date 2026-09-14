using UnityEngine;
using Valkur.Core;
using Valkur.Core.UI;

namespace Valkur.Gameplay.Combat
{
    /// <summary>
    /// The two texels of outline that make two rows one frame.
    ///
    /// <para>When the resource row shares the health row's top outline, every frame piece still
    /// arrives with its own chamfered corners — right for a row standing alone, and wrong at a
    /// joint: measured on the first joined capture, the shared row had a transparent texel at each
    /// end and a third where the mana bar's top-right corner meets the pip, and each one showed
    /// the ground through a hole in the middle of the instrument. A frame with holes in it reads as
    /// three pieces that happen to touch.</para>
    ///
    /// <para>Drawn UNDER the health row's own outline, so the low-health pulse that lives in that
    /// outline still beats along the whole shared row; only the corner texels, which the health
    /// frame does not draw, come from here.</para>
    /// </summary>
    internal sealed class WorldBarJoints
    {
        /// <summary>How many consecutive sorting orders the joints claim.</summary>
        internal const int SLOT_COUNT = 1;

        private readonly SpriteRenderer _seam;
        private readonly SpriteRenderer _corner;
        private Color _colour = Color.black;
        private float _alpha = 1f;

        public WorldBarJoints(Transform parent, int sortingBase, string namePrefix = "Joint")
        {
            _seam = MakePart(parent, namePrefix + "Seam", sortingBase);
            _corner = MakePart(parent, namePrefix + "Corner", sortingBase);
        }

        private static SpriteRenderer MakePart(Transform parent, string name, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localScale = Vector3.one;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = WorldBarArt.Solid;
            sr.drawMode = SpriteDrawMode.Sliced;
            sr.sharedMaterial = WorldBarArt.Material;
            sr.sortingLayerName = SortingConfig.LAYER_UI_WORLD;
            sr.sortingOrder = order;
            go.SetActive(false);
            return sr;
        }

        /// <summary>
        /// Place the joints. <paramref name="seamWidth"/> 0 hides the seam (the rows are not
        /// joined); a NaN <paramref name="cornerX"/> hides the inner corner (no mana beside a pip).
        /// </summary>
        public void Layout(float seamWidth, float seamY, float cornerX, float cornerY)
        {
            float t = WorldBarGeometry.TEXEL;
            bool seam = seamWidth > 0f;
            if (_seam.gameObject.activeSelf != seam) _seam.gameObject.SetActive(seam);
            if (seam)
            {
                _seam.size = new Vector2(seamWidth, t);
                _seam.transform.localPosition = new Vector3(0f, seamY, 0f);
            }

            bool corner = seam && !float.IsNaN(cornerX);
            if (_corner.gameObject.activeSelf != corner) _corner.gameObject.SetActive(corner);
            if (corner)
            {
                _corner.size = new Vector2(t, t);
                _corner.transform.localPosition = new Vector3(cornerX, cornerY, 0f);
            }
            Write();
        }

        public void SetColour(Color outline)
        {
            _colour = outline;
            Write();
        }

        public void SetAlpha(float alpha)
        {
            if (Mathf.Abs(alpha - _alpha) < 0.002f) return;
            _alpha = alpha;
            Write();
        }

        public void SetSortingBase(int order)
        {
            _seam.sortingOrder = order;
            _corner.sortingOrder = order;
        }

        private void Write()
        {
            var c = _colour;
            c.a *= _alpha;
            if (_seam.color != c) _seam.color = c;
            if (_corner.color != c) _corner.color = c;
        }
    }
}
