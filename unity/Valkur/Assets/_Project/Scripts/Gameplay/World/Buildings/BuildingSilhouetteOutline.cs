using UnityEngine;
using Valkur.Core;

namespace Valkur.Gameplay.World
{
    /// <summary>
    /// Yellow silhouette highlight that hugs the FULL shape of a
    /// <see cref="BuildingObject"/> in player mode.
    ///
    /// Strategy: eight offset copies of the building's un-split sprite, tinted a
    /// solid colour by <c>Valkur/SpriteSolidColor</c>, placed on the WallsBottom
    /// layer just behind the footprint. The building's own Footprint + Canopy draw
    /// over each copy's centre, leaving only the offset fringe visible — a constant
    /// thickness outline that follows the art's silhouette exactly. Because each copy
    /// samples only its own sprite alpha, it is atlas-safe by construction and needs
    /// no per-sprite edge mesh or neighbour sampling.
    /// </summary>
    [DisallowMultipleComponent]
    public class BuildingSilhouetteOutline : MonoBehaviour
    {
        private static Material s_material;

        // 8 directions (cardinal + diagonal) give a ring with no visible corner gaps.
        private readonly Vector2[] _directions =
        {
            new Vector2( 1f,             0f),
            new Vector2(-1f,             0f),
            new Vector2( 0f,             1f),
            new Vector2( 0f,            -1f),
            new Vector2( 0.70710678f,  0.70710678f),
            new Vector2(-0.70710678f,  0.70710678f),
            new Vector2( 0.70710678f, -0.70710678f),
            new Vector2(-0.70710678f, -0.70710678f),
        };

        private BuildingObject  _target;
        private float           _thicknessWorld = 0.06f;
        private Color           _color = new Color(1f, 0.85f, 0.20f, 1f);
        private SpriteRenderer[] _copies;

        public void Configure(Color color, float thicknessWorld)
        {
            _color          = color;
            _thicknessWorld = thicknessWorld;
            EnsureChildren();
            ApplyVisuals();
        }

        public void Follow(BuildingObject target) => _target = target;

        public void SetVisible(bool visible)
        {
            if (_copies == null) return;
            for (int i = 0; i < _copies.Length; i++)
                if (_copies[i] != null) _copies[i].enabled = visible;
        }

        private void EnsureChildren()
        {
            if (_copies != null && _copies.Length == _directions.Length) return;

            _copies = new SpriteRenderer[_directions.Length];
            for (int i = 0; i < _directions.Length; i++)
            {
                // One-time allocation on first use — not in the hot path.
                var go = new GameObject("Outline_" + i);
                go.transform.SetParent(transform, false);

                var sr = go.AddComponent<SpriteRenderer>();
                sr.sortingLayerName = SortingConfig.LAYER_WALLS_BOTTOM;
                if (s_material == null)
                {
                    var sh = Shader.Find("Valkur/SpriteSolidColor");
                    if (sh == null) sh = Shader.Find("Sprites/Default");
                    if (sh != null)
                    {
                        s_material = new Material(sh)
                        {
                            name = "BuildingSilhouetteOutline_Mat",
                            hideFlags = HideFlags.HideAndDontSave
                        };
                    }
                }
                if (s_material != null) sr.sharedMaterial = s_material;

                _copies[i] = sr;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => s_material = null;

        private void ApplyVisuals()
        {
            if (_copies == null) return;
            for (int i = 0; i < _copies.Length; i++)
                if (_copies[i] != null) _copies[i].color = _color;
        }

        private void LateUpdate()
        {
            if (_target == null || !_target.gameObject.activeInHierarchy || _target.SourceSprite == null)
            {
                SetVisible(false);
                return;
            }

            SetVisible(true);

            Vector3 pos   = _target.transform.position;
            Vector3 scale = _target.transform.localScale;
            Sprite sprite = _target.SourceSprite;
            int order     = SortingConfig.YToSortingOrder(pos.y) - 1;

            for (int i = 0; i < _copies.Length; i++)
            {
                var sr   = _copies[i];
                Vector2 d = _directions[i] * _thicknessWorld;
                sr.transform.position   = new Vector3(pos.x + d.x, pos.y + d.y, 0f);
                sr.transform.localScale = scale;
                sr.sprite               = sprite;
                sr.sortingOrder         = order;
            }
        }
    }
}
