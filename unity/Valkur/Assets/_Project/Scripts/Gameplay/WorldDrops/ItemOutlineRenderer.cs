using UnityEngine;
using Valkur.Gameplay.Inventory;

namespace Valkur.Gameplay.WorldDrops
{
    /// <summary>
    /// World-space rectangular outline drawn around a <see cref="WorldPickup"/>.
    /// Shared by the F7 Items Editor (authoring hover/select) and the in-game
    /// <see cref="WorldDropInteractor"/> on the player (gameplay hover/select
    /// /drag with range limit). Mirrors the <see cref="BuildingOutlineRenderer"/>
    /// approach: a <see cref="LineRenderer"/> loop on the VFX sorting layer,
    /// anchored to <see cref="SpriteRenderer"/>.bounds so the highlight tracks
    /// the drop's render size.
    ///
    /// Visual contract:
    ///   • Hover (default mode) → cyan, thickness ~0.06 wu.
    ///   • Hover (delete mode)  → red,  thickness ~0.10 wu.
    ///   • Active selection     → yellow, thickness ~0.10 wu.
    ///
    /// One <see cref="LineRenderer"/> with loop = true and four corners. The
    /// renderer sits on the VFX sorting layer so it draws above the drop
    /// sprite. No fill — items are point-like, so a translucent tint would
    /// drown the icon.
    /// </summary>
    [DisallowMultipleComponent]
    public class ItemOutlineRenderer : MonoBehaviour
    {
        private LineRenderer _line;
        private static Material s_lineMat;

        private WorldPickup _target;
        private float _thicknessWorld = 0.06f;
        private Color _color = Color.cyan;
        private float _padding = 0.04f; // extra wu so the outline doesn't kiss the sprite edge.

        public void Configure(Color color, float thicknessWorld, float padding = 0.04f)
        {
            _color = color;
            _thicknessWorld = thicknessWorld;
            _padding = padding;
            EnsureChildren();
            ApplyVisuals();
        }

        public void Follow(WorldPickup target) => _target = target;

        public void SetVisible(bool visible)
        {
            if (_line != null) _line.enabled = visible;
        }

        private void EnsureChildren()
        {
            if (_line != null) return;

            _line = gameObject.AddComponent<LineRenderer>();
            _line.useWorldSpace      = true;
            _line.loop               = true;
            _line.positionCount      = 4;
            _line.numCornerVertices  = 0;
            _line.numCapVertices     = 0;
            _line.alignment          = LineAlignment.View;
            _line.sortingLayerName   = "VFX";
            _line.sortingOrder       = 5000;

            if (s_lineMat == null)
            {
                var sh = Shader.Find("Sprites/Default");
                if (sh == null) sh = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default");
                if (sh != null) s_lineMat = new Material(sh) { name = "ItemOutline_Line" };
            }
            if (s_lineMat != null) _line.sharedMaterial = s_lineMat;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_lineMat = null;
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
            if (_line == null || _target == null || _target.gameObject == null
                || !_target.gameObject.activeInHierarchy)
            {
                SetVisible(false);
                return;
            }

            var sr = _target.GetComponent<SpriteRenderer>();
            if (sr == null || sr.sprite == null)
            {
                SetVisible(false);
                return;
            }

            var b = sr.bounds; // already includes the pickup's localScale
            float p = _padding;
            Vector3 bl = new Vector3(b.min.x - p, b.min.y - p, 0f);
            Vector3 br = new Vector3(b.max.x + p, b.min.y - p, 0f);
            Vector3 tr = new Vector3(b.max.x + p, b.max.y + p, 0f);
            Vector3 tl = new Vector3(b.min.x - p, b.max.y + p, 0f);

            SetVisible(true);
            _line.SetPosition(0, bl);
            _line.SetPosition(1, br);
            _line.SetPosition(2, tr);
            _line.SetPosition(3, tl);
        }
    }
}
