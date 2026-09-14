using System.Collections.Generic;
using TMPro;
using UnityEngine;
using Valkur.Core;

namespace Valkur.Gameplay.Spells.Debugging
{
    /// <summary>
    /// Draws the colliders of every live entity, and freezes the ones the last spell collision
    /// reached until the next collision replaces them. Driven by <see cref="EntityCollisionDebug"/>.
    ///
    /// <para><b>Two layers with two lifetimes.</b> The LIVE layer is every entity's colliders
    /// where they are now, redrawn each frame because entities walk. The SNAPSHOT layer is a
    /// still picture of the moment a spell reached something: a thick outline of every collider
    /// the target carried, green when damage landed and amber when a query returned it and no
    /// damage followed, plus a cross at the point the executor's narrow phase measured. Read
    /// together with <c>areas on</c>, the red area, the frozen collider and the measured point are
    /// the three quantities any "the area covers it and nothing happened" report is about.</para>
    ///
    /// <para>Line widths are wider than <see cref="SpellDebugRenderer"/>'s on purpose: the two
    /// overlays share most of the hue wheel, and weight is what keeps a frozen collider from
    /// reading as one more spell shape.</para>
    ///
    /// <para>Sorting is <c>Overlay</c>, one order under the spell overlay's lines so an area
    /// drawn over a collider stays readable - a sorting LAYER, never a Z depth.</para>
    /// </summary>
    public sealed class EntityCollisionDebugRenderer : SingletonMonoBehaviour<EntityCollisionDebugRenderer>
    {
        private const float LIVE_WIDTH = 0.035f;
        private const float SNAPSHOT_WIDTH = 0.085f;
        private const float POINT_TICK = 0.3f;
        private const int SORTING_ORDER = 898;
        private const float LABEL_FONT_SIZE = 2.4f;
        private const float RESUBSCRIBE_SECONDS = 1f;

        // Instance fields, not static tables: nothing else reads them, and a static would need
        // a reset hook it has no state to justify.
        // Three live kinds, three colours: the FOOTPRINT (what stands on the ground and hits
        // walls), the HURTBOX (what a spell can land on) and anything else (a perception trigger).
        private readonly Color _liveBody = new Color(0.40f, 0.85f, 1.00f, 0.75f);
        private readonly Color _liveHurt = new Color(1.00f, 0.55f, 0.85f, 0.70f);
        private readonly Color _liveOther = new Color(0.70f, 0.70f, 0.78f, 0.30f);
        private readonly Color _snapHit = new Color(0.25f, 1.00f, 0.35f, 1.00f);
        private readonly Color _snapHitOther = new Color(0.25f, 1.00f, 0.35f, 0.45f);
        private readonly Color _snapTouched = new Color(1.00f, 0.78f, 0.10f, 1.00f);
        private readonly Color _snapTouchedOther = new Color(1.00f, 0.78f, 0.10f, 0.45f);
        private readonly Color _pointAccepted = new Color(1.00f, 1.00f, 1.00f, 1.00f);
        private readonly Color _pointRejected = new Color(1.00f, 0.30f, 0.30f, 1.00f);

        private Material _lineMaterial;
        private readonly List<LineRenderer> _lines = new List<LineRenderer>();
        private readonly List<TextMeshPro> _labels = new List<TextMeshPro>();
        private int _lineCursor;
        private int _labelCursor;

        private readonly List<Collider2D> _colliderScratch = new List<Collider2D>(8);
        private readonly List<Vector2> _pointScratch = new List<Vector2>(64);
        private readonly List<int> _lengthScratch = new List<int>(4);

        private bool _subscribed;
        private float _nextResubscribe;
        private bool _drewLastFrame;

        protected override void OnSingletonAwake()
        {
            var shader = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default")
                      ?? Shader.Find("Sprites/Default");
            _lineMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
        }

        private void LateUpdate()
        {
            if (!EntityCollisionDebug.Enabled)
            {
                Unsubscribe();
                if (_drewLastFrame) { BeginFrame(); EndFrame(); _drewLastFrame = false; }
                return;
            }

            MaintainSubscription();

            BeginFrame();
            DrawLive(EntityRegistry.Player);
            DrawLiveList(EntityRegistry.Monsters);
            DrawLiveList(EntityRegistry.NPCs);

            var contacts = EntityCollisionDebug.Current;
            for (int i = 0; i < contacts.Count; i++) DrawSnapshot(contacts[i]);
            EndFrame();
            _drewLastFrame = true;
        }

        // -- Hit event ------------------------------------------------------------

        /// <summary>
        /// <c>GameEvents.Clear()</c> runs on every scene transition and drops this handler with
        /// no notice, so the subscription is re-asserted once a second while the overlay is on.
        /// Remove-then-add keeps it single.
        /// </summary>
        private void MaintainSubscription()
        {
            if (_subscribed && Time.unscaledTime < _nextResubscribe) return;
            GameEvents.OnHitDealt -= OnHitDealt;
            GameEvents.OnHitDealt += OnHitDealt;
            _subscribed = true;
            _nextResubscribe = Time.unscaledTime + RESUBSCRIBE_SECONDS;
        }

        private void Unsubscribe()
        {
            if (!_subscribed) return;
            GameEvents.OnHitDealt -= OnHitDealt;
            _subscribed = false;
        }

        private static void OnHitDealt(GameObject attacker, GameObject victim, int damage)
        {
            EntityCollisionDebug.Hit(victim, damage);
        }

        // -- Live layer -----------------------------------------------------------

        private void DrawLiveList(IReadOnlyList<GameObject> entities)
        {
            for (int i = 0; i < entities.Count; i++) DrawLive(entities[i]);
        }

        private void DrawLive(GameObject entity)
        {
            if (entity == null || !entity.activeInHierarchy) return;

            Collider2D body = EntityColliderConfigurator.GetBodyCollider(entity);
            _colliderScratch.Clear();
            entity.GetComponentsInChildren(false, _colliderScratch);
            for (int i = 0; i < _colliderScratch.Count; i++)
            {
                Collider2D c = _colliderScratch[i];
                if (c == null || !c.enabled) continue;
                _pointScratch.Clear();
                _lengthScratch.Clear();
                ColliderOutline.Build(c, _pointScratch, _lengthScratch);

                bool isBody = c == body;
                Color color = isBody ? _liveBody
                    : Combat.EntityColliderRig.IsHurtbox(c) ? _liveHurt
                    : _liveOther;
                int cursor = 0;
                for (int k = 0; k < _lengthScratch.Count; k++)
                {
                    DrawLoop(_pointScratch, cursor, _lengthScratch[k], color, LIVE_WIDTH);
                    cursor += _lengthScratch[k];
                }
            }
            _colliderScratch.Clear();
        }

        // -- Snapshot layer -------------------------------------------------------

        private void DrawSnapshot(EntityContact contact)
        {
            bool hit = contact.State == EntityContactState.Hit;
            for (int i = 0; i < contact.Loops.Count; i++)
            {
                bool isBody = i < contact.LoopIsBody.Count && contact.LoopIsBody[i];
                Color color = hit
                    ? (isBody ? _snapHit : _snapHitOther)
                    : (isBody ? _snapTouched : _snapTouchedOther);
                DrawLoop(contact.Loops[i], color, isBody ? SNAPSHOT_WIDTH : SNAPSHOT_WIDTH * 0.5f);
            }

            string text = hit
                ? "GOLPE " + contact.EntityName + "  -" + contact.Damage +
                  (contact.HitCount > 1 ? " (x" + contact.HitCount + ")" : "")
                : contact.EntityName + ": tocado, SIN daño";
            PlaceLabel(contact.LabelAnchor, text, hit ? _snapHit : _snapTouched);

            if (!contact.HasTestPoint) return;
            Color pc = contact.TestPointAccepted ? _pointAccepted : _pointRejected;
            DrawCross(contact.TestPoint, POINT_TICK, pc);
            PlaceLabel(contact.TestPoint + new Vector2(0f, -POINT_TICK * 1.6f),
                (string.IsNullOrEmpty(contact.TestPointLabel) ? "punto evaluado" : contact.TestPointLabel) +
                (contact.TestPointAccepted ? ": dentro" : ": FUERA"), pc);
        }

        // -- Primitives -----------------------------------------------------------

        private void DrawLoop(List<Vector2> points, int start, int count, Color color, float width)
        {
            if (count < 2) return;
            var line = NextLine(color, count, closed: true, width);
            for (int i = 0; i < count; i++)
            {
                Vector2 p = points[start + i];
                line.SetPosition(i, new Vector3(p.x, p.y, 0f));
            }
        }

        private void DrawLoop(Vector2[] points, Color color, float width)
        {
            if (points == null || points.Length < 2) return;
            var line = NextLine(color, points.Length, closed: true, width);
            for (int i = 0; i < points.Length; i++)
                line.SetPosition(i, new Vector3(points[i].x, points[i].y, 0f));
        }

        private void DrawCross(Vector2 at, float size, Color color)
        {
            var h = NextLine(color, 2, closed: false, SNAPSHOT_WIDTH * 0.7f);
            h.SetPosition(0, new Vector3(at.x - size, at.y - size, 0f));
            h.SetPosition(1, new Vector3(at.x + size, at.y + size, 0f));
            var v = NextLine(color, 2, closed: false, SNAPSHOT_WIDTH * 0.7f);
            v.SetPosition(0, new Vector3(at.x - size, at.y + size, 0f));
            v.SetPosition(1, new Vector3(at.x + size, at.y - size, 0f));
        }

        // -- Pools ----------------------------------------------------------------

        private void BeginFrame()
        {
            _lineCursor = 0;
            _labelCursor = 0;
        }

        /// <summary>Hides whatever this frame did not reuse. Nothing is toggled that is still in use.</summary>
        private void EndFrame()
        {
            for (int i = _lineCursor; i < _lines.Count; i++)
                if (_lines[i] != null && _lines[i].enabled) _lines[i].enabled = false;
            for (int i = _labelCursor; i < _labels.Count; i++)
                if (_labels[i] != null && _labels[i].gameObject.activeSelf) _labels[i].gameObject.SetActive(false);
        }

        private LineRenderer NextLine(Color color, int positions, bool closed, float width)
        {
            LineRenderer line;
            if (_lineCursor < _lines.Count)
            {
                line = _lines[_lineCursor];
            }
            else
            {
                var go = new GameObject("EntityCollisionLine");
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

            if (!line.enabled) line.enabled = true;
            line.loop = closed;
            line.positionCount = positions;
            line.startWidth = width;
            line.endWidth = width;
            line.startColor = color;
            line.endColor = color;
            return line;
        }

        private void PlaceLabel(Vector2 at, string text, Color color)
        {
            if (string.IsNullOrEmpty(text)) return;

            TextMeshPro label;
            if (_labelCursor < _labels.Count)
            {
                label = _labels[_labelCursor];
            }
            else
            {
                var go = new GameObject("EntityCollisionLabel");
                go.transform.SetParent(transform, false);
                label = go.AddComponent<TextMeshPro>();
                // TMP resolves its font in Awake, which never runs on a component added in Edit Mode.
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
                    mr.sortingOrder = SORTING_ORDER + 4;
                }
                _labels.Add(label);
            }
            _labelCursor++;

            if (!label.gameObject.activeSelf) label.gameObject.SetActive(true);
            label.transform.position = new Vector3(at.x, at.y, 0f);
            if (label.text != text) label.text = text;
            label.color = color;
        }

        protected override void OnDestroy()
        {
            Unsubscribe();
            base.OnDestroy();
            if (_lineMaterial == null) return;
            if (Application.isPlaying) Destroy(_lineMaterial);
            else DestroyImmediate(_lineMaterial);
        }
    }
}
