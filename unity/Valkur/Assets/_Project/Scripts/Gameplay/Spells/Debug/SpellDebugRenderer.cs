using System.Collections.Generic;
using TMPro;
using UnityEngine;
using Valkur.Core;

namespace Valkur.Gameplay.Spells.Debugging
{
    /// <summary>
    /// Draws whatever <see cref="SpellDebugAreas"/> recorded, and keeps drawing it until the
    /// next cast replaces it.
    ///
    /// <para>Pooled <c>LineRenderer</c>s rather than <c>Gizmos</c> or <c>GL</c>: gizmos do not
    /// exist in a built player and are invisible in the Game view, and custom GL drawing under
    /// the URP 2D renderer needs <c>RenderPipelineManager.endCameraRendering</c> because
    /// <c>Camera.current</c> is null in <c>OnRenderObject</c> - a trap this project has already
    /// paid for once. <c>CombatRangeVisualizer</c> settled on line renderers for the same
    /// reasons and this reuses its shape.</para>
    ///
    /// <para><b>Geometry is rebuilt only when the record changes.</b> The overlay is a still
    /// picture of a cast that has already happened; rebuilding forty circles every frame to
    /// redraw the same forty circles is work nobody asked for. <see cref="SpellDebugAreas.Version"/>
    /// is the signal.</para>
    ///
    /// <para>Sorting lives on <c>Overlay</c>, the topmost world sorting layer, because an area
    /// drawn under a wall top is an area the author cannot compare against the effect. Note
    /// this is a sorting LAYER, never a Z depth - passing a Z as a sorting order is the
    /// <c>LightningBoltFX</c> bug and it has shipped twice in this repository.</para>
    /// </summary>
    public sealed class SpellDebugRenderer : SingletonMonoBehaviour<SpellDebugRenderer>
    {
        private const int CIRCLE_SEGMENTS = 56;
        private const float LINE_WIDTH = 0.045f;
        private const float POINT_TICK = 0.28f;
        private const int SORTING_ORDER = 900;
        private const float LABEL_FONT_SIZE = 2.4f;

        /// <summary>
        /// One colour per role, and they are deliberately far apart in hue rather than shades of
        /// one family: the whole question the overlay answers is "is the red circle where the
        /// particles are", and two roles a few percent apart in colour is the contrast defect
        /// this project has already recorded for the Controls editor and the world bars.
        /// </summary>
        [SelfHealingStatic("A constant lookup table: written once at type init, never mutated, " +
                           "and holding no scene reference. It is also read-only, so the ratchet's " +
                           "own accepted reset shapes (stsfld, field.Clear()) are both unavailable " +
                           "to it -- giving it a reset hook would be giving it one it cannot satisfy.")]
        private static readonly Dictionary<SpellDebugRole, Color> RoleColors =
            new Dictionary<SpellDebugRole, Color>
            {
                { SpellDebugRole.Origin,    new Color(1.00f, 1.00f, 1.00f, 0.95f) },
                { SpellDebugRole.Aim,       new Color(0.40f, 0.85f, 1.00f, 0.85f) },
                { SpellDebugRole.Reach,     new Color(0.55f, 0.55f, 0.62f, 0.55f) },
                { SpellDebugRole.Placement, new Color(1.00f, 0.85f, 0.20f, 0.95f) },
                { SpellDebugRole.Damage,    new Color(1.00f, 0.20f, 0.25f, 0.95f) },
                { SpellDebugRole.Trigger,   new Color(1.00f, 0.55f, 0.10f, 0.85f) },
                { SpellDebugRole.Splash,    new Color(1.00f, 0.35f, 0.85f, 0.90f) },
                { SpellDebugRole.Path,      new Color(0.45f, 1.00f, 0.45f, 0.70f) },
                { SpellDebugRole.Visual,    new Color(0.65f, 0.40f, 1.00f, 0.80f) },
                { SpellDebugRole.Muzzle,    new Color(0.30f, 1.00f, 0.80f, 0.95f) },
            };

        private Material _lineMaterial;
        private readonly List<LineRenderer> _lines = new List<LineRenderer>();
        private readonly List<TextMeshPro> _labels = new List<TextMeshPro>();
        private int _lineCursor;
        private int _labelCursor;
        private int _builtVersion = -1;
        private readonly HashSet<string> _labelsDrawn = new HashSet<string>();
        private readonly List<Vector2> _labelPositions = new List<Vector2>();

        public static Color ColorFor(SpellDebugRole role)
        {
            Color c;
            return RoleColors.TryGetValue(role, out c) ? c : Color.white;
        }

        protected override void OnSingletonAwake()
        {
            var shader = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default")
                      ?? Shader.Find("Sprites/Default");
            _lineMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
        }

        private void LateUpdate()
        {
            // The record is a world-space snapshot and nothing in it moves, so the only reason
            // to touch the scene is that the record itself changed.
            if (_builtVersion == SpellDebugAreas.Version) return;
            _builtVersion = SpellDebugAreas.Version;

            Recycle();
            if (!SpellDebugAreas.Enabled) return;

            var shapes = SpellDebugAreas.Current;
            for (int i = 0; i < shapes.Count; i++) Draw(shapes[i]);
        }

        private void Draw(SpellDebugShape shape)
        {
            Color color = ColorFor(shape.Role);

            switch (shape.Kind)
            {
                case SpellDebugKind.Point:
                    DrawCross(shape.A, POINT_TICK, color);
                    PlaceLabel(shape.A + new Vector2(0f, POINT_TICK * 1.4f), shape.Label, color);
                    break;

                case SpellDebugKind.Circle:
                    DrawArcPoints(shape.A, Vector2.right, shape.Radius, 360f, color, closed: true);
                    PlaceLabel(shape.A + new Vector2(0f, shape.Radius), shape.Label, color);
                    break;

                case SpellDebugKind.Sector:
                    DrawSector(shape.A, shape.Direction, shape.Radius, shape.Angle, color);
                    PlaceLabel(shape.A + shape.Direction * (shape.Radius * 0.65f), shape.Label, color);
                    break;

                case SpellDebugKind.Segment:
                    DrawCapsule(shape.A, shape.B, shape.Radius, color);
                    PlaceLabel(Vector2.Lerp(shape.A, shape.B, 0.5f) +
                               new Vector2(0f, shape.Radius + 0.2f), shape.Label, color);
                    break;

                case SpellDebugKind.Rect:
                    DrawRect(shape.A, shape.Size, shape.Angle, color);
                    PlaceLabel(shape.A + new Vector2(0f, shape.Size.y * 0.5f), shape.Label, color);
                    break;
            }
        }

        // -- Primitives -----------------------------------------------------------

        private void DrawArcPoints(Vector2 centre, Vector2 from, float radius, float sweepDegrees,
                                   Color color, bool closed)
        {
            int segments = Mathf.Max(3, Mathf.RoundToInt(CIRCLE_SEGMENTS * (sweepDegrees / 360f)));
            var line = NextLine(color, segments + 1, closed);
            float startAngle = Mathf.Atan2(from.y, from.x) * Mathf.Rad2Deg - sweepDegrees * 0.5f;

            for (int i = 0; i <= segments; i++)
            {
                float a = (startAngle + sweepDegrees * (i / (float)segments)) * Mathf.Deg2Rad;
                line.SetPosition(i, new Vector3(centre.x + Mathf.Cos(a) * radius,
                                                centre.y + Mathf.Sin(a) * radius, 0f));
            }
        }

        /// <summary>
        /// A sector is its arc plus the two radii that close it. Drawing only the arc is what
        /// makes a cone read as a curved line floating in front of the caster - the same
        /// mistake the flame breath shipped with, where a LineRenderer outline was taken for a
        /// filled shape.
        /// </summary>
        private void DrawSector(Vector2 centre, Vector2 direction, float radius,
                                float arcDegrees, Color color)
        {
            float arc = Mathf.Clamp(arcDegrees, 0f, 360f);
            if (arc >= 359.5f) { DrawArcPoints(centre, direction, radius, 360f, color, true); return; }

            int segments = Mathf.Max(2, Mathf.RoundToInt(CIRCLE_SEGMENTS * (arc / 360f)));
            var line = NextLine(color, segments + 3, closed: true);
            float baseAngle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
            float startAngle = baseAngle - arc * 0.5f;

            line.SetPosition(0, centre);
            for (int i = 0; i <= segments; i++)
            {
                float a = (startAngle + arc * (i / (float)segments)) * Mathf.Deg2Rad;
                line.SetPosition(i + 1, new Vector3(centre.x + Mathf.Cos(a) * radius,
                                                    centre.y + Mathf.Sin(a) * radius, 0f));
            }
            line.SetPosition(segments + 2, centre);
        }

        /// <summary>
        /// A beam's damage volume is a capsule, so the outline is two parallel sides and two
        /// end caps. Drawn as the real shape rather than as a bare centre line, because the
        /// question an author has about a beam is almost always about its WIDTH.
        /// </summary>
        private void DrawCapsule(Vector2 from, Vector2 to, float halfWidth, Color color)
        {
            Vector2 axis = to - from;
            if (axis.sqrMagnitude < 0.000001f) { DrawCross(from, Mathf.Max(halfWidth, POINT_TICK), color); return; }

            Vector2 dir = axis.normalized;
            Vector2 side = new Vector2(-dir.y, dir.x) * Mathf.Max(halfWidth, 0.01f);

            var line = NextLine(color, 5, closed: true);
            line.SetPosition(0, (Vector3)(from + side));
            line.SetPosition(1, (Vector3)(to + side));
            line.SetPosition(2, (Vector3)(to - side));
            line.SetPosition(3, (Vector3)(from - side));
            line.SetPosition(4, (Vector3)(from + side));

            // The spine, so direction of travel is readable when the capsule is thin.
            var spine = NextLine(new Color(color.r, color.g, color.b, color.a * 0.5f), 2, closed: false);
            spine.SetPosition(0, (Vector3)from);
            spine.SetPosition(1, (Vector3)to);
        }

        private void DrawRect(Vector2 centre, Vector2 size, float rotationDegrees, Color color)
        {
            Quaternion rot = Quaternion.Euler(0f, 0f, rotationDegrees);
            Vector2 half = size * 0.5f;
            var line = NextLine(color, 5, closed: true);
            line.SetPosition(0, centre + (Vector2)(rot * new Vector2(-half.x, -half.y)));
            line.SetPosition(1, centre + (Vector2)(rot * new Vector2(half.x, -half.y)));
            line.SetPosition(2, centre + (Vector2)(rot * new Vector2(half.x, half.y)));
            line.SetPosition(3, centre + (Vector2)(rot * new Vector2(-half.x, half.y)));
            line.SetPosition(4, centre + (Vector2)(rot * new Vector2(-half.x, -half.y)));
        }

        private void DrawCross(Vector2 at, float size, Color color)
        {
            var h = NextLine(color, 2, closed: false);
            h.SetPosition(0, new Vector3(at.x - size, at.y, 0f));
            h.SetPosition(1, new Vector3(at.x + size, at.y, 0f));
            var v = NextLine(color, 2, closed: false);
            v.SetPosition(0, new Vector3(at.x, at.y - size, 0f));
            v.SetPosition(1, new Vector3(at.x, at.y + size, 0f));
        }

        // -- Pools ----------------------------------------------------------------

        private void Recycle()
        {
            for (int i = 0; i < _lines.Count; i++)
                if (_lines[i] != null) _lines[i].enabled = false;
            for (int i = 0; i < _labels.Count; i++)
                if (_labels[i] != null) _labels[i].gameObject.SetActive(false);
            _lineCursor = 0;
            _labelCursor = 0;
            _labelsDrawn.Clear();
            _labelPositions.Clear();
        }

        private LineRenderer NextLine(Color color, int positions, bool closed)
        {
            LineRenderer line;
            if (_lineCursor < _lines.Count)
            {
                line = _lines[_lineCursor];
            }
            else
            {
                var go = new GameObject("SpellDebugLine");
                go.transform.SetParent(transform, false);
                line = go.AddComponent<LineRenderer>();
                line.useWorldSpace = true;
                line.numCapVertices = 2;
                line.numCornerVertices = 2;
                line.alignment = LineAlignment.View;
                line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                line.receiveShadows = false;
                line.sharedMaterial = _lineMaterial;
                line.sortingLayerName = SortingConfig.LAYER_OVERLAY;
                line.sortingOrder = SORTING_ORDER;
                _lines.Add(line);
            }
            _lineCursor++;

            line.enabled = true;
            line.loop = closed;
            line.positionCount = positions;
            line.startWidth = LINE_WIDTH;
            line.endWidth = LINE_WIDTH;
            line.startColor = color;
            line.endColor = color;
            return line;
        }

        /// <summary>
        /// Put a label at a point, or decide not to.
        ///
        /// <para>Two suppressions, and both were measured on a live capture rather than
        /// guessed. A meteor shower resolves eight impacts of identical size, so eight copies
        /// of "impacto 1.1 u" were printed across the same patch of ground and the rings
        /// underneath them stopped being readable - a repeated label says nothing the first one
        /// did not. And coincident labels (the anchor and the muzzle sit half a unit apart)
        /// overprinted each other into an unreadable smear, so anything landing on top of a
        /// label already placed is nudged down until it clears.</para>
        /// </summary>
        private void PlaceLabel(Vector2 at, string text, Color color)
        {
            if (string.IsNullOrEmpty(text)) return;
            if (!_labelsDrawn.Add(text)) return;

            // The clearance test is a RECTANGLE, not a circle, because a label is a wide, short
            // thing: "ancla Hands" and "salida" sit half a unit apart -- clear of any sane
            // radius -- and still print straight through each other, which is what the first
            // live capture showed. Measured on the shipped font at this size, a label runs
            // about three units wide and a third of a unit tall.
            const float LABEL_HALF_WIDTH = 1.7f;
            const float LABEL_ROW_HEIGHT = 0.34f;
            for (int guard = 0; guard < 10; guard++)
            {
                bool clear = true;
                for (int i = 0; i < _labelPositions.Count; i++)
                {
                    Vector2 d = _labelPositions[i] - at;
                    if (Mathf.Abs(d.x) >= LABEL_HALF_WIDTH) continue;
                    if (Mathf.Abs(d.y) >= LABEL_ROW_HEIGHT) continue;
                    clear = false;
                    break;
                }
                if (clear) break;
                at.y -= LABEL_ROW_HEIGHT;
            }
            _labelPositions.Add(at);

            TextMeshPro label;
            if (_labelCursor < _labels.Count)
            {
                label = _labels[_labelCursor];
            }
            else
            {
                var go = new GameObject("SpellDebugLabel");
                go.transform.SetParent(transform, false);
                label = go.AddComponent<TextMeshPro>();
                // TMP resolves its font in Awake, which never runs on a component added in Edit
                // Mode - GetPreferredValues then throws from inside TMP. Assigning it here costs
                // nothing in Play Mode and is what keeps a fixture measurable.
                if (label.font == null && TMP_Settings.defaultFontAsset != null)
                    label.font = TMP_Settings.defaultFontAsset;
                label.fontSize = LABEL_FONT_SIZE;
                label.alignment = TextAlignmentOptions.Center;
                label.enableWordWrapping = false;
                label.fontStyle = FontStyles.Bold;
                var mr = label.GetComponent<MeshRenderer>();
                if (mr != null)
                {
                    mr.sortingLayerName = SortingConfig.LAYER_OVERLAY;
                    mr.sortingOrder = SORTING_ORDER + 1;
                }
                _labels.Add(label);
            }
            _labelCursor++;

            label.gameObject.SetActive(true);
            label.transform.position = new Vector3(at.x, at.y, 0f);
            label.text = text;
            label.color = color;
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            if (_lineMaterial == null) return;
            if (Application.isPlaying) Destroy(_lineMaterial);
            else DestroyImmediate(_lineMaterial);
        }
    }
}
