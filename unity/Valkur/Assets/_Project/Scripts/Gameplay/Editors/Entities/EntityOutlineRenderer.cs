using UnityEngine;

namespace Valkur.Gameplay.Editors
{
    /// <summary>
    /// World-space rectangular outline that follows a target entity's
    /// <see cref="SpriteRenderer.bounds"/>. Used by <c>EntitiesRuntimeEditor</c>
    /// to highlight the selected NPC (yellow) and its same-monsterKey peers
    /// (orange), mirroring the active/same-template outlines used by the
    /// Buildings Editor.
    ///
    /// Implementation parallels <see cref="BuildingOutlineRenderer"/>: a
    /// single LineRenderer with 4 looped corners on the VFX sorting layer so
    /// the outline draws above the sprite without modifying its colour.
    /// </summary>
    [DisallowMultipleComponent]
    public class EntityOutlineRenderer : MonoBehaviour
    {
        private LineRenderer _line;
        private static Material s_lineMat;

        private SpriteRenderer _target;
        private Transform      _targetRoot;
        private float _thicknessWorld = 0.06f;
        private Color _color = Color.yellow;

        public void Configure(Color color, float thicknessWorld)
        {
            _color = color;
            _thicknessWorld = thicknessWorld;
            EnsureChildren();
            ApplyVisuals();
        }

        /// <summary>
        /// Follow the given entity. <paramref name="root"/> is the NPC root
        /// (so we can detect destruction even if the SpriteRenderer is on a child).
        /// </summary>
        public void Follow(Transform root, SpriteRenderer sr)
        {
            _targetRoot = root;
            _target = sr;
        }

        public void SetVisible(bool visible)
        {
            if (_line != null) _line.enabled = visible;
        }

        private void EnsureChildren()
        {
            if (_line != null) return;
            var lineGo = new GameObject("Line");
            lineGo.transform.SetParent(transform, false);
            _line = lineGo.AddComponent<LineRenderer>();
            _line.useWorldSpace = true;
            _line.loop = true;
            _line.positionCount = 4;
            _line.numCornerVertices = 0;
            _line.numCapVertices = 0;
            _line.alignment = LineAlignment.View;
            _line.sortingLayerName = "VFX";
            _line.sortingOrder = 5000;
            if (s_lineMat == null)
            {
                var sh = Shader.Find("Sprites/Default");
                if (sh == null) sh = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default");
                if (sh != null) s_lineMat = new Material(sh);
            }
            if (s_lineMat != null) _line.sharedMaterial = s_lineMat;
        }

        private void ApplyVisuals()
        {
            if (_line == null) return;
            _line.startColor = _color;
            _line.endColor   = _color;
            _line.startWidth = _thicknessWorld;
            _line.endWidth   = _thicknessWorld;
        }

        private void LateUpdate()
        {
            if (_line == null) return;
            if (_targetRoot == null || _target == null
                || !_targetRoot.gameObject.activeInHierarchy
                || _target.sprite == null)
            {
                SetVisible(false);
                return;
            }

            var b = _target.bounds;
            SetVisible(true);

            Vector3 bl = new Vector3(b.min.x, b.min.y, 0f);
            Vector3 br = new Vector3(b.max.x, b.min.y, 0f);
            Vector3 tr = new Vector3(b.max.x, b.max.y, 0f);
            Vector3 tl = new Vector3(b.min.x, b.max.y, 0f);
            _line.SetPosition(0, bl);
            _line.SetPosition(1, br);
            _line.SetPosition(2, tr);
            _line.SetPosition(3, tl);
        }
    }
}
