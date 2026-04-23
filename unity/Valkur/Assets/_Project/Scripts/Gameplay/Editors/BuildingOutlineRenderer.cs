using UnityEngine;
using Valkur.Gameplay.World;

namespace Valkur.Gameplay.Editors
{
    /// <summary>
    /// World-space rectangular outline + optional translucent fill, used by the
    /// runtime Buildings Editor to highlight hovered/selected buildings.
    ///
    /// Mirrors Python's pygame.draw.rect calls in
    /// `roguelike_editors/buildings/building_editor_view.py`:
    ///   - hovered & not in remove mode: cyan (0,255,255) thickness 2
    ///   - hovered & remove mode:        red  (255,0,0) thickness 3 + red fill alpha 60
    ///   - active selection:             yellow (255,215,0) thickness 5
    ///
    /// Implementation: a single LineRenderer (loop=true, 4 corners) plus an optional
    /// SpriteRenderer using a 1×1 white texture for the fill. All children sit on the
    /// VFX sorting layer so they render above buildings/entities.
    /// </summary>
    [DisallowMultipleComponent]
    public class BuildingOutlineRenderer : MonoBehaviour
    {
        private LineRenderer _line;
        private SpriteRenderer _fill;
        private static Texture2D s_whiteTex;
        private static Sprite s_whiteSprite;
        private static Material s_lineMat;

        private BuildingObject _target;
        private float _thicknessWorld = 0.06f;
        private Color _color = Color.cyan;
        private bool _drawFill;
        private Color _fillColor = new Color(1f, 0f, 0f, 0.235f); // alpha 60/255

        public void Configure(Color color, float thicknessWorld, bool drawFill, Color fillColor)
        {
            _color = color;
            _thicknessWorld = thicknessWorld;
            _drawFill = drawFill;
            _fillColor = fillColor;
            EnsureChildren();
            ApplyVisuals();
        }

        public void Follow(BuildingObject target) => _target = target;

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

            if (_fill == null)
            {
                var fillGo = new GameObject("Fill");
                fillGo.transform.SetParent(transform, false);
                _fill = fillGo.AddComponent<SpriteRenderer>();
                _fill.sortingLayerName = "VFX";
                _fill.sortingOrder = 4999;
                if (s_whiteSprite == null)
                {
                    s_whiteTex = new Texture2D(1, 1);
                    s_whiteTex.SetPixel(0, 0, Color.white);
                    s_whiteTex.Apply();
                    s_whiteSprite = Sprite.Create(s_whiteTex,
                        new Rect(0, 0, 1, 1), new Vector2(0.5f, 0f), 1f);
                }
                _fill.sprite = s_whiteSprite;
            }
        }

        private void ApplyVisuals()
        {
            if (_line != null)
            {
                _line.startColor = _color;
                _line.endColor = _color;
                _line.startWidth = _thicknessWorld;
                _line.endWidth = _thicknessWorld;
            }
            if (_fill != null)
            {
                _fill.color = _fillColor;
                _fill.enabled = _drawFill;
            }
        }

        private void LateUpdate()
        {
            if (_target == null || _line == null) { SetVisible(false); return; }
            if (!_target.gameObject.activeInHierarchy) { SetVisible(false); return; }

            // World rect of the building: bottom-center anchor at _target.transform.position
            // Width / height come from the bottom + top sprite renderers if available.
            if (!_target.TryGetWorldRect(out var rect)) { SetVisible(false); return; }

            SetVisible(true);

            // 4 corners, CCW
            Vector3 bl = new Vector3(rect.xMin, rect.yMin, 0f);
            Vector3 br = new Vector3(rect.xMax, rect.yMin, 0f);
            Vector3 tr = new Vector3(rect.xMax, rect.yMax, 0f);
            Vector3 tl = new Vector3(rect.xMin, rect.yMax, 0f);
            _line.SetPosition(0, bl);
            _line.SetPosition(1, br);
            _line.SetPosition(2, tr);
            _line.SetPosition(3, tl);

            if (_drawFill)
            {
                // SpriteRenderer with bottom-center pivot: place at bottom-center, scale to size
                _fill.transform.position = new Vector3((rect.xMin + rect.xMax) * 0.5f, rect.yMin, 0f);
                _fill.transform.localScale = new Vector3(rect.width, rect.height, 1f);
            }
        }
    }
}
