using UnityEngine;

namespace Valkur.Gameplay.Editors.MultiSelect
{
    /// <summary>
    /// A world-space rectangle drawn around one selected item.
    ///
    /// <para>ONE SHAPE FOR ALL THREE DOMAINS, deliberately. The three editors each draw their
    /// own marker — a ring for a light, a footprint box for an emitter, an outline for a
    /// building — and reusing those here would have made a group of six read as six unrelated
    /// things that happen to be lit up. A group is one object now, so it gets one shape;
    /// WHICH domain each member belongs to is carried by the colour instead.</para>
    ///
    /// <para>Follows the same conventions as <c>LightOutlineRenderer</c>: a shared material
    /// reset on domain reload, <c>LineAlignment.View</c> so the box faces the camera, and a
    /// sorting LAYER by name with a small order — never a Z depth passed as an order, which
    /// is the bug <c>LightningBoltFX</c> and <c>ElementalProjectileVisual</c> both shipped.</para>
    /// </summary>
    [DisallowMultipleComponent]
    internal sealed class SelectionBoxOutline : MonoBehaviour
    {
        private const float THICKNESS      = 0.05f;
        private const int   SORTING_ORDER  = 5100;   // above the editors' own markers

        private LineRenderer _line;
        private static Material s_lineMat;

        /// <summary>
        /// Domain Reload is OFF, so a Material cached in a static survives Stop and comes back
        /// as a destroyed Unity object on the next Play. Reset rather than adding a line to
        /// <c>Tests/EditMode/Baselines/unreset-statics.txt</c> — a ratchet that keeps accepting
        /// new entries is a list.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => s_lineMat = null;

        private void Awake() => EnsureLine();

        private void EnsureLine()
        {
            if (_line != null) return;

            if (s_lineMat == null)
            {
                var sh = Shader.Find("Sprites/Default");
                if (sh == null) sh = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default");
                if (sh != null) s_lineMat = new Material(sh);
            }

            _line = gameObject.AddComponent<LineRenderer>();
            _line.useWorldSpace     = true;
            _line.loop              = true;
            _line.positionCount     = 4;
            _line.numCornerVertices  = 0;
            _line.numCapVertices     = 0;
            _line.alignment          = LineAlignment.View;
            _line.startWidth         = THICKNESS;
            _line.endWidth           = THICKNESS;
            _line.sortingLayerName   = "VFX";
            _line.sortingOrder       = SORTING_ORDER;
            if (s_lineMat != null) _line.sharedMaterial = s_lineMat;
        }

        /// <summary>Draw the box. <paramref name="primary"/> thickens it — the primary is what
        /// a move drags from and what a paste anchors on, so it has to be tellable apart.</summary>
        public void Draw(Rect rect, Color color, bool primary)
        {
            EnsureLine();
            if (_line == null) return;

            _line.enabled    = true;
            _line.startWidth = _line.endWidth = primary ? THICKNESS * 2f : THICKNESS;
            _line.startColor = _line.endColor = color;

            _line.SetPosition(0, new Vector3(rect.xMin, rect.yMin, 0f));
            _line.SetPosition(1, new Vector3(rect.xMax, rect.yMin, 0f));
            _line.SetPosition(2, new Vector3(rect.xMax, rect.yMax, 0f));
            _line.SetPosition(3, new Vector3(rect.xMin, rect.yMax, 0f));
        }

        public void Hide()
        {
            if (_line != null) _line.enabled = false;
        }
    }
}
