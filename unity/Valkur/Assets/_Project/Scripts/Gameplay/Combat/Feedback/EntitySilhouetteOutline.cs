using UnityEngine;

namespace Valkur.Gameplay.Combat
{
    /// <summary>
    /// Solid-colour silhouette highlight that hugs the FULL shape of an entity's
    /// sprite. Used by <see cref="PlayerTargetSelector"/> to mark the NPC the player
    /// has clicked on.
    ///
    /// Strategy is the one <c>BuildingSilhouetteOutline</c> already proved: eight
    /// offset copies of the target's own sprite, tinted a flat colour by
    /// <c>Valkur/SpriteSolidColor</c> and drawn one sorting order BEHIND the entity.
    /// The entity's own body draws over each copy's centre, leaving only the offset
    /// fringe — a constant-thickness outline that follows the art's silhouette
    /// exactly. Each copy samples only its own sprite alpha, so it is atlas-safe by
    /// construction and needs no edge mesh or neighbour sampling.
    ///
    /// The rig deliberately lives at the SCENE ROOT rather than under the player or
    /// under the target. Under the player it would inherit the player's scale; under
    /// the target the offsets would have to be divided back out by that target's
    /// scale, and a target destroyed mid-frame would take the rig with it. Unparented,
    /// <c>localScale</c> IS <c>lossyScale</c> and the offsets are plain world units.
    /// </summary>
    [DisallowMultipleComponent]
    public class EntitySilhouetteOutline : MonoBehaviour
    {
        private static Material s_material;

        // 8 directions (cardinal + diagonal) give a ring with no visible corner gaps.
        // Instance rather than static: a `static readonly` array is a mutable static
        // the Domain-Reload ratchet cannot see reset, and there is exactly one rig.
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

        private SpriteRenderer[] _copies;
        private Transform        _targetRoot;
        private SpriteRenderer   _target;
        private Color            _color = new Color(1f, 0.85f, 0.20f, 1f);
        private float            _thicknessWorld = 0.06f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => s_material = null;

        public void Configure(Color color, float thicknessWorld)
        {
            _color = color;
            _thicknessWorld = thicknessWorld;
            EnsureChildren();
            ApplyVisuals();
        }

        /// <summary>
        /// Follow the given entity. <paramref name="root"/> is the entity root so
        /// destruction is detected even when the SpriteRenderer sits on a child.
        /// Pass null to stop following.
        /// </summary>
        public void Follow(Transform root, SpriteRenderer sr)
        {
            _targetRoot = root;
            _target     = sr;
            if (root == null || sr == null) SetVisible(false);
        }

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
                if (s_material == null)
                {
                    var sh = Shader.Find("Valkur/SpriteSolidColor");
                    if (sh == null) sh = Shader.Find("Sprites/Default");
                    if (sh != null)
                    {
                        s_material = new Material(sh)
                        {
                            name      = "EntitySilhouetteOutline_Mat",
                            hideFlags = HideFlags.HideAndDontSave
                        };
                    }
                }
                if (s_material != null) sr.sharedMaterial = s_material;

                _copies[i] = sr;
            }
        }

        private void ApplyVisuals()
        {
            if (_copies == null) return;
            for (int i = 0; i < _copies.Length; i++)
                if (_copies[i] != null) _copies[i].color = _color;
        }

        private void LateUpdate()
        {
            if (_copies == null) return;

            if (_targetRoot == null || _target == null
                || !_targetRoot.gameObject.activeInHierarchy
                || !_target.enabled
                || _target.sprite == null)
            {
                SetVisible(false);
                return;
            }

            SetVisible(true);

            Transform t = _target.transform;
            Vector3 pos       = t.position;
            Quaternion rot    = t.rotation;
            Vector3 lossy     = t.lossyScale;
            Sprite sprite     = _target.sprite;
            // One order behind the body so the body covers every copy's centre and
            // only the offset fringe survives. YSortEntity rewrites the entity's own
            // order whenever it walks, so this is re-read every frame rather than
            // captured once.
            int layerId       = _target.sortingLayerID;
            int order         = _target.sortingOrder - 1;

            for (int i = 0; i < _copies.Length; i++)
            {
                var sr = _copies[i];
                if (sr == null) continue;

                Vector2 d = _directions[i] * _thicknessWorld;
                sr.transform.position   = new Vector3(pos.x + d.x, pos.y + d.y, pos.z);
                sr.transform.rotation   = rot;
                sr.transform.localScale = lossy;
                sr.sprite               = sprite;
                sr.flipX                = _target.flipX;
                sr.flipY                = _target.flipY;
                sr.sortingLayerID       = layerId;
                sr.sortingOrder         = order;
            }
        }
    }
}
