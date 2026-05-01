using UnityEngine;

namespace Valkur.Gameplay.VFX
{
    /// <summary>
    /// World-space circular outline drawn around a ParticleEmitter position.
    /// Used by the runtime Particles Editor (F1) to highlight hovered / selected /
    /// same-preset emitter instances — mirrors the BuildingOutlineRenderer pattern
    /// from the Buildings Editor (F10).
    ///
    /// Implementation: a LineRenderer with <see cref="CIRCLE_SEGMENTS"/> vertices
    /// approximating a circle of configurable radius. A separate thin SpriteRenderer
    /// provides an optional translucent fill disc.
    /// </summary>
    [DisallowMultipleComponent]
    public class ParticleEmitterOutlineRenderer : MonoBehaviour
    {
        private const int   CIRCLE_SEGMENTS = 24;
        private const float DEFAULT_RADIUS  = 0.40f;

        private LineRenderer   _line;
        private SpriteRenderer _fill;

        private static Texture2D  s_whiteTex;
        private static Sprite     s_whiteSprite;
        private static Material   s_lineMat;

        private Transform _target;
        private float     _radius          = DEFAULT_RADIUS;
        private float     _thicknessWorld  = 0.06f;
        private Color     _color           = Color.cyan;
        private bool      _drawFill;
        private Color     _fillColor       = new Color(1f, 0f, 0f, 0.235f);

        /// <summary>Configure visual style. Call once; Follow() + SetVisible() each frame.</summary>
        public void Configure(Color color, float thicknessWorld, float radius,
                              bool drawFill, Color fillColor)
        {
            _color          = color;
            _thicknessWorld = thicknessWorld;
            _radius         = radius > 0f ? radius : DEFAULT_RADIUS;
            _drawFill       = drawFill;
            _fillColor      = fillColor;
            EnsureChildren();
            ApplyVisuals();
        }

        /// <summary>Assign the emitter transform to track. Pass null to hide.</summary>
        public void Follow(Transform target) => _target = target;

        /// <summary>Show or hide all children without destroying them.</summary>
        public void SetVisible(bool visible)
        {
            if (_line != null) _line.enabled = visible;
            if (_fill != null) _fill.enabled = visible && _drawFill;
        }

        private void EnsureChildren()
        {
            if (_line == null)
            {
                var lineGo = new GameObject("Line");
                lineGo.transform.SetParent(transform, false);
                _line = lineGo.AddComponent<LineRenderer>();
                _line.useWorldSpace  = true;
                _line.loop           = true;
                _line.positionCount  = CIRCLE_SEGMENTS;
                _line.numCornerVertices = 0;
                _line.numCapVertices    = 0;
                _line.alignment      = LineAlignment.View;
                _line.sortingLayerName = "VFX";
                _line.sortingOrder   = 5000;

                if (s_lineMat == null)
                {
                    var sh = Shader.Find("Sprites/Default");
                    if (sh == null) sh = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default");
                    if (sh != null) s_lineMat = new Material(sh);
                }
                if (s_lineMat != null) _line.sharedMaterial = s_lineMat;
            }

            if (_fill == null)
            {
                var fillGo = new GameObject("Fill");
                fillGo.transform.SetParent(transform, false);
                _fill = fillGo.AddComponent<SpriteRenderer>();
                _fill.sortingLayerName = "VFX";
                _fill.sortingOrder     = 4999;

                if (s_whiteSprite == null)
                {
                    s_whiteTex = new Texture2D(1, 1);
                    s_whiteTex.SetPixel(0, 0, Color.white);
                    s_whiteTex.Apply();
                    s_whiteSprite = Sprite.Create(s_whiteTex,
                        new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);
                }
                _fill.sprite = s_whiteSprite;
            }
        }

        private void ApplyVisuals()
        {
            if (_line != null)
            {
                _line.startColor = _color;
                _line.endColor   = _color;
                _line.startWidth = _thicknessWorld;
                _line.endWidth   = _thicknessWorld;
            }
            if (_fill != null)
            {
                _fill.color   = _fillColor;
                _fill.enabled = _drawFill;
            }
        }

        private void LateUpdate()
        {
            if (_target == null || _line == null) { SetVisible(false); return; }
            if (!_target.gameObject.activeInHierarchy) { SetVisible(false); return; }

            SetVisible(true);

            Vector3 center = new Vector3(_target.position.x, _target.position.y, 0f);

            // Draw circle with CIRCLE_SEGMENTS line segments.
            float step = 2f * Mathf.PI / CIRCLE_SEGMENTS;
            for (int i = 0; i < CIRCLE_SEGMENTS; i++)
            {
                float angle = i * step;
                _line.SetPosition(i, center + new Vector3(
                    Mathf.Cos(angle) * _radius,
                    Mathf.Sin(angle) * _radius,
                    0f));
            }

            if (_drawFill && _fill != null)
            {
                _fill.transform.position   = center;
                _fill.transform.localScale = new Vector3(_radius * 2f, _radius * 2f, 1f);
            }
        }
    }
}
