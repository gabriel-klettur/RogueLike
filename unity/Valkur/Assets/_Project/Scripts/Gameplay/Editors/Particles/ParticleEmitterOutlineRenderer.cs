using UnityEngine;

namespace Valkur.Gameplay.VFX
{
    /// <summary>
    /// World-space outline drawn around a ParticleEmitter, shaped like the area that
    /// emitter actually emits over. Used by the runtime Particles Editor (F1) to highlight
    /// hovered / selected / same-preset instances — mirrors the BuildingOutlineRenderer
    /// pattern from the Buildings Editor (F10).
    ///
    /// The shape comes from <see cref="ParticleFootprint"/>: a circle for the radial kinds,
    /// a rectangle for anything emitting from a spawn box. It used to be one fixed 0.45-unit
    /// circle for every preset, which marked the emitter's ORIGIN rather than its extent —
    /// a pollen field's marker covered a fifth of what it drew, and a portal's sat inside it.
    ///
    /// Implementation: one LineRenderer, <see cref="CIRCLE_SEGMENTS"/> vertices for a circle
    /// and four for a rectangle, plus a SpriteRenderer for the optional translucent fill.
    ///
    /// Two more LineRenderers draw the emitter's two AUTHORED boxes: the emission area its
    /// particles are born in, and the reach they travel to. Both are precalculated from the
    /// preset and the instance's own size overrides — deterministic the moment the emitter is
    /// selected, rather than accumulated by watching it — because they are not just readouts:
    /// they are the handles the author drags to resize the effect, and a handle that is still
    /// settling is a handle that moves under the cursor.
    ///
    /// A fifth LineRenderer highlights whichever edge is under the cursor or being dragged.
    /// </summary>
    [DisallowMultipleComponent]
    public class ParticleEmitterOutlineRenderer : MonoBehaviour
    {
        private const int   CIRCLE_SEGMENTS = 24;
        private const int   RECT_CORNERS    = 4;

        private LineRenderer   _line;
        private LineRenderer   _maxLine;
        private LineRenderer   _minLine;
        private LineRenderer   _edgeLine;
        private SpriteRenderer _fill;

        private static Texture2D  s_whiteTex;
        private static Sprite     s_whiteSprite;
        private static Material   s_lineMat;

        private Transform _target;
        private ParticleFootprint _footprint = ParticleFootprint.Default;
        private float     _thicknessWorld  = 0.06f;
        private Color     _color           = Color.cyan;
        private bool      _drawFill;
        private Color     _fillColor       = new Color(1f, 0f, 0f, 0.235f);

        // ── Authored boxes ────────────────────────────────────────────────────────

        private bool  _drawExtremes;
        private Color _maxColor = new Color(1f, 0.35f, 0.30f, 0.85f);
        private Color _minColor = new Color(0.35f, 1f, 0.55f, 0.85f);
        private Color _highlightColor = new Color(1f, 1f, 1f, 1f);
        private float _extremesThickness = 0.04f;

        private ParticleFootprint _minFootprint = ParticleFootprint.Default;
        private ParticleFootprint _maxFootprint = ParticleFootprint.Default;
        private bool _hasBoxes;

        private ParticleBoundsBox _highlightBox = ParticleBoundsBox.None;
        private ParticleBoundsEdge _highlightEdge = ParticleBoundsEdge.None;

        /// <summary>Configure visual style. Call once; Follow() + SetVisible() each frame.</summary>
        public void Configure(Color color, float thicknessWorld, bool drawFill, Color fillColor)
        {
            _color          = color;
            _thicknessWorld = thicknessWorld;
            _drawFill       = drawFill;
            _fillColor      = fillColor;
            EnsureChildren();
            ApplyVisuals();
        }

        /// <summary>
        /// Draw the emitter's emission and reach boxes alongside the live outline, in their
        /// own colours. Neither replaces the live outline: one says where the effect is now,
        /// the other two say what it was authored to be.
        /// </summary>
        public void ConfigureExtremes(bool enabled, Color minColor, Color maxColor, float thicknessWorld)
        {
            _drawExtremes = enabled;
            _minColor = minColor;
            _maxColor = maxColor;
            _extremesThickness = thicknessWorld;
            EnsureChildren();
            ApplyVisuals();
        }

        /// <summary>Colour of the edge highlight. Defaults to white.</summary>
        public void ConfigureHighlight(Color color)
        {
            _highlightColor = color;
            EnsureChildren();
            ApplyVisuals();
        }

        /// <summary>
        /// The two precalculated boxes to draw. Pushed by the editor, which derives them from
        /// the preset and the instance's overrides — the same call the drag handles resize.
        /// </summary>
        public void SetBoxes(ParticleFootprint emission, ParticleFootprint reach)
        {
            _minFootprint = emission;
            _maxFootprint = reach;
            _hasBoxes = true;
        }

        /// <summary>Which edge to draw thickened, if any.</summary>
        public void SetHighlight(ParticleBoundsBox box, ParticleBoundsEdge edge)
        {
            _highlightBox = box;
            _highlightEdge = edge;
        }

        /// <summary>The emission box currently drawn.</summary>
        public ParticleFootprint MinFootprint => _hasBoxes ? _minFootprint : _footprint;

        /// <summary>The reach box currently drawn.</summary>
        public ParticleFootprint MaxFootprint => _hasBoxes ? _maxFootprint : _footprint;

        /// <summary>
        /// Rate, in world units per second, at which each edge of the outline may pull IN.
        /// Growth is instantaneous. The measured footprint comes from the live particles, so
        /// it breathes as they spawn and die — a spark that flies wide for half a second
        /// would otherwise snap the box out and back. Chased in one direction only, the
        /// outline settles on the effect's usual extent and still covers the excursion the
        /// moment it happens.
        /// </summary>
        private const float SHRINK_RATE = 2.5f;

        /// <summary>
        /// Point at an emitter and size to its area in one call. These two always happen
        /// together: the renderers are pooled and reassigned to a different emitter every
        /// frame, so a footprint left over from the previous target frames the new one at
        /// the wrong size, and smoothing carried across a retarget would drag the old box to
        /// the new emitter.
        /// </summary>
        public void Track(Transform target, ParticleFootprint footprint)
        {
            bool retargeted = target != _target;
            _target = target;

            // A prediction is a worst-case bound over every module the preset uses, so it can
            // be several times the size of the effect that follows it. Easing from one into
            // the first real measurement at the shrink rate would leave an oversized box
            // hanging around the emitter for seconds; the switch is a cut, not a transition.
            bool leavingPrediction = _footprint.Predicted && !footprint.Predicted;

            if (retargeted)
            {
                // The boxes belong to the emitter that was being tracked, and this renderer is
                // pooled: without dropping them the next emitter is framed by the previous
                // one's authored size for a frame.
                _hasBoxes = false;
                _highlightBox = ParticleBoundsBox.None;
                _highlightEdge = ParticleBoundsEdge.None;
            }

            _footprint = (retargeted || leavingPrediction) ? footprint : Smoothed(footprint);
        }

        /// <summary>
        /// The area to draw, unsmoothed. Prefer <see cref="Track"/>, which keeps the
        /// footprint and the follow target in step.
        /// </summary>
        public void SetFootprint(ParticleFootprint footprint) => _footprint = footprint;

        private ParticleFootprint Smoothed(ParticleFootprint measured)
        {
            float step = SHRINK_RATE * Mathf.Max(0f, Time.unscaledDeltaTime);

            if (!measured.IsRect && !_footprint.IsRect)
            {
                float radius = Mathf.Max(measured.HalfWidth, _footprint.HalfWidth - step);
                return ParticleFootprint.Circle(measured.Center, radius);
            }

            Vector2 min = measured.Min, max = measured.Max;
            Vector2 prevMin = _footprint.Min, prevMax = _footprint.Max;

            min = new Vector2(Mathf.Min(min.x, prevMin.x + step), Mathf.Min(min.y, prevMin.y + step));
            max = new Vector2(Mathf.Max(max.x, prevMax.x - step), Mathf.Max(max.y, prevMax.y - step));

            Vector2 centre = (min + max) * 0.5f;
            return ParticleFootprint.Rect(centre, (max.x - min.x) * 0.5f, (max.y - min.y) * 0.5f);
        }

        /// <summary>The footprint currently being drawn.</summary>
        public ParticleFootprint Footprint => _footprint;

        /// <summary>Assign the emitter transform to track. Pass null to hide.</summary>
        public void Follow(Transform target) => _target = target;

        /// <summary>Show or hide all children without destroying them.</summary>
        public void SetVisible(bool visible)
        {
            if (_line != null) _line.enabled = visible;
            if (_fill != null) _fill.enabled = visible && _drawFill;
            if (_maxLine != null) _maxLine.enabled = visible && _drawExtremes;
            if (_minLine != null) _minLine.enabled = visible && _drawExtremes;
            if (_edgeLine != null)
                _edgeLine.enabled = visible && _drawExtremes && _highlightEdge != ParticleBoundsEdge.None;
        }

        private void EnsureChildren()
        {
            if (_line == null) _line = MakeLine("Line", 5000);

            // The envelopes draw UNDER the live outline: they are reference marks, and where
            // all three coincide the one the author is reading moment to moment has to win.
            if (_drawExtremes && _maxLine == null) _maxLine = MakeLine("MaxLine", 4997);
            if (_drawExtremes && _minLine == null) _minLine = MakeLine("MinLine", 4998);

            // Above everything else: it is the only line that answers "what will this drag
            // move", and it has to win wherever the three boxes coincide.
            if (_drawExtremes && _edgeLine == null)
            {
                _edgeLine = MakeLine("EdgeHighlight", 5001);
                _edgeLine.loop = false;
                _edgeLine.positionCount = 2;
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

        private LineRenderer MakeLine(string name, int sortingOrder)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);

            var line = go.AddComponent<LineRenderer>();
            line.useWorldSpace     = true;
            line.loop              = true;
            line.positionCount     = CIRCLE_SEGMENTS;
            line.numCornerVertices = 0;
            line.numCapVertices    = 0;
            line.alignment         = LineAlignment.View;
            line.sortingLayerName  = "VFX";
            line.sortingOrder      = sortingOrder;

            if (s_lineMat == null)
            {
                var sh = Shader.Find("Sprites/Default");
                if (sh == null) sh = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default");
                if (sh != null) s_lineMat = new Material(sh);
            }
            if (s_lineMat != null) line.sharedMaterial = s_lineMat;

            return line;
        }

        /// <summary>Draws the hovered / dragged edge as a thick segment over its own box.</summary>
        private void DrawHighlight(Vector3 origin)
        {
            if (_edgeLine == null) return;

            if (_highlightEdge == ParticleBoundsEdge.None || _highlightBox == ParticleBoundsBox.None)
            {
                _edgeLine.enabled = false;
                return;
            }

            var box = _highlightBox == ParticleBoundsBox.Emission ? MinFootprint : MaxFootprint;
            Vector3 centre = new Vector3(origin.x + box.Center.x, origin.y + box.Center.y, 0f);
            float hw = box.HalfWidth;
            float hh = box.HalfHeight;

            Vector3 a, b;
            switch (_highlightEdge)
            {
                case ParticleBoundsEdge.Left:
                    a = centre + new Vector3(-hw, -hh, 0f); b = centre + new Vector3(-hw, hh, 0f); break;
                case ParticleBoundsEdge.Right:
                    a = centre + new Vector3(hw, -hh, 0f);  b = centre + new Vector3(hw, hh, 0f);  break;
                case ParticleBoundsEdge.Bottom:
                    a = centre + new Vector3(-hw, -hh, 0f); b = centre + new Vector3(hw, -hh, 0f); break;
                default:
                    a = centre + new Vector3(-hw, hh, 0f);  b = centre + new Vector3(hw, hh, 0f);  break;
            }

            _edgeLine.enabled = true;
            if (_edgeLine.positionCount != 2) _edgeLine.positionCount = 2;
            _edgeLine.SetPosition(0, a);
            _edgeLine.SetPosition(1, b);
        }

        /// <summary>Writes one footprint into a LineRenderer as a rect or a circle loop.</summary>
        private static void DrawFootprint(LineRenderer line, Vector3 origin, ParticleFootprint f)
        {
            if (line == null) return;

            Vector3 centre = new Vector3(origin.x + f.Center.x, origin.y + f.Center.y, 0f);
            float hw = f.HalfWidth;
            float hh = f.HalfHeight;

            if (f.IsRect)
            {
                // positionCount is set every frame rather than at creation: the pool hands
                // the same renderer to a box emitter and a radial one on consecutive frames,
                // and a rect drawn into 24 slots leaves 20 stale circle vertices.
                if (line.positionCount != RECT_CORNERS) line.positionCount = RECT_CORNERS;
                line.SetPosition(0, centre + new Vector3(-hw, -hh, 0f));
                line.SetPosition(1, centre + new Vector3( hw, -hh, 0f));
                line.SetPosition(2, centre + new Vector3( hw,  hh, 0f));
                line.SetPosition(3, centre + new Vector3(-hw,  hh, 0f));
                return;
            }

            if (line.positionCount != CIRCLE_SEGMENTS) line.positionCount = CIRCLE_SEGMENTS;
            float step = 2f * Mathf.PI / CIRCLE_SEGMENTS;
            for (int i = 0; i < CIRCLE_SEGMENTS; i++)
            {
                float angle = i * step;
                line.SetPosition(i, centre + new Vector3(Mathf.Cos(angle) * hw, Mathf.Sin(angle) * hh, 0f));
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
            if (_maxLine != null)
            {
                _maxLine.startColor = _maxColor;
                _maxLine.endColor   = _maxColor;
                _maxLine.startWidth = _extremesThickness;
                _maxLine.endWidth   = _extremesThickness;
                _maxLine.enabled    = _drawExtremes;
            }
            if (_minLine != null)
            {
                _minLine.startColor = _minColor;
                _minLine.endColor   = _minColor;
                _minLine.startWidth = _extremesThickness;
                _minLine.endWidth   = _extremesThickness;
                _minLine.enabled    = _drawExtremes;
            }
            if (_edgeLine != null)
            {
                _edgeLine.startColor = _highlightColor;
                _edgeLine.endColor   = _highlightColor;
                // Three times the box line: the highlight has to read as "grabbable" at a
                // glance, and it sits on top of the very line it is highlighting.
                _edgeLine.startWidth = _extremesThickness * 3f;
                _edgeLine.endWidth   = _extremesThickness * 3f;
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

            // The footprint is offset from the emitter whenever the preset drifts its
            // particles one way — a leaf field's covered area hangs below the spawner,
            // because that is where the leaves have fallen to by the time they die.
            Vector3 origin = new Vector3(_target.position.x, _target.position.y, 0f);

            DrawFootprint(_line, origin, _footprint);

            if (_drawExtremes)
            {
                DrawFootprint(_maxLine, origin, MaxFootprint);
                DrawFootprint(_minLine, origin, MinFootprint);
                DrawHighlight(origin);
            }

            if (_drawFill && _fill != null)
            {
                _fill.transform.position = new Vector3(
                    origin.x + _footprint.Center.x, origin.y + _footprint.Center.y, 0f);
                _fill.transform.localScale =
                    new Vector3(_footprint.HalfWidth * 2f, _footprint.HalfHeight * 2f, 1f);
            }
        }
    }
}
