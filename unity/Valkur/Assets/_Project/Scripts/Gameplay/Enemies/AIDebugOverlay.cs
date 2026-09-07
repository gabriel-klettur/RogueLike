using System.Collections.Generic;
using UnityEngine;
using Valkur.Core;
using Valkur.Gameplay.FSM;

namespace Valkur.Gameplay.Enemies
{
    /// <summary>
    /// Draws what each monster can PERCEIVE, in the game view, on demand.
    ///
    /// <para>Everything about hostile AI was invisible: <c>DebugHUD</c> and <c>TargetHUD</c>
    /// print the state name and nothing in the project ever drew an aggro radius, a field of
    /// view, a leash or the line to the current target. So every perception question — "why
    /// did that one not see me", "why did this one stop", "is it hunting me or the summon" —
    /// was answered by reading numbers out of an asset and guessing, and the two bugs that
    /// cost the most (an aggro ring 30 units wide, and a leash that had never been
    /// implemented) are both things a single frame of this would have shown.</para>
    ///
    /// <para>Drawn with <see cref="LineRenderer"/> children, not <c>GL</c> — as
    /// <c>TilemapColliderDebugOverlay</c> records, <c>GL.Lines</c> in <c>OnRenderObject</c> is
    /// invisible under the URP 2D renderer. Costs nothing while off: the component is created
    /// by the <c>ai</c> console command and destroys every line it made when switched off.</para>
    /// </summary>
    [AddComponentMenu("")]          // Hidden — created by the `ai` console command
    [DisallowMultipleComponent]
    public sealed class AIDebugOverlay : MonoBehaviour
    {
        private const float LINE_WIDTH = 0.04f;
        private const int   CIRCLE_SEGMENTS = 40;
        private const float Z = -0.2f;

        private static readonly Color AggroColor  = new Color(1f, 0.55f, 0.1f, 0.55f);
        private static readonly Color LeashColor  = new Color(0.3f, 0.7f, 1f, 0.35f);
        private static readonly Color FovColor    = new Color(1f, 0.95f, 0.3f, 0.75f);
        private static readonly Color TargetColor = new Color(1f, 0.2f, 0.2f, 0.9f);
        private static readonly Color SearchColor = new Color(0.6f, 0.3f, 1f, 0.9f);

        private static Material s_material;

        /// <summary>The live overlay, or null when the feature is off.</summary>
        public static AIDebugOverlay Active { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Active = null;
            s_material = null;
        }

        /// <summary>
        /// Turn the overlay on or off. Returns the number of monsters being drawn, which is
        /// what the console reports back — "0" is itself the answer to a common question.
        /// </summary>
        public static int SetEnabled(bool on)
        {
            if (!on)
            {
                if (Active != null) Destroy(Active.gameObject);
                Active = null;
                return 0;
            }

            if (Active == null)
            {
                var go = new GameObject("[AI Debug Overlay]");
                Active = go.AddComponent<AIDebugOverlay>();
            }

            var monsters = EntityRegistry.Monsters;
            return monsters != null ? monsters.Count : 0;
        }

        /// <summary>True while the overlay is drawing.</summary>
        public static bool IsOn => Active != null;

        // ── Drawing ───────────────────────────────────────────────────────────────

        private readonly List<LineRenderer> _pool = new List<LineRenderer>();
        private int _used;

        private void OnDestroy()
        {
            if (Active == this) Active = null;
        }

        private void LateUpdate()
        {
            _used = 0;

            var monsters = EntityRegistry.Monsters;
            if (monsters != null)
            {
                for (int i = 0; i < monsters.Count; i++)
                    DrawMonster(monsters[i]);
            }

            // Everything past the high-water mark of this frame is left over from a frame
            // with more monsters in it. Disabling rather than destroying keeps the pool warm
            // for the next spawn wave.
            for (int i = _used; i < _pool.Count; i++)
                if (_pool[i] != null) _pool[i].enabled = false;
        }

        private void DrawMonster(GameObject monster)
        {
            if (monster == null || !monster.activeInHierarchy) return;

            var brain = monster.GetComponent<FSMMonsterBrain>();
            var fsm = brain != null ? brain.FSM : null;
            if (fsm == null) return;

            Vector2 pos = monster.transform.position;

            float aggro = fsm.GetContextFloat("aggro_range", 0f);
            if (aggro > 0f) Circle(pos, aggro, AggroColor);

            // The leash is drawn around HOME, not around the monster: the whole question it
            // answers is "how far is this thing allowed to get from where it started".
            if (fsm.Context.ContainsKey(FSMHomeAnchor.KeyX))
            {
                var home = new Vector2(fsm.GetContextFloat(FSMHomeAnchor.KeyX),
                                       fsm.GetContextFloat(FSMHomeAnchor.KeyY));
                Circle(home, FSMTuning.LeashRange(fsm, aggro), LeashColor);
            }

            float fov = FSMTuning.FovDegrees(fsm);
            if (fov < 360f)
            {
                var c = fsm.GetContext<FSMComponents>(FSMComponents.KEY);
                Vector2 facing = FSMPerception.FacingOf(c);
                float half = fov * 0.5f;
                float reach = Mathf.Max(1f, aggro);
                Segment(pos, pos + Rotate(facing, half) * reach, FovColor);
                Segment(pos, pos + Rotate(facing, -half) * reach, FovColor);
            }

            // Who it is actually hunting — which is not always the player, and is the one
            // thing no HUD in the project could answer once allied summons existed.
            var components = fsm.GetContext<FSMComponents>(FSMComponents.KEY);
            var target = components?.Target(fsm);
            if (target != null && fsm.CurrentState is ChaseState)
                Segment(pos, target.transform.position, TargetColor);

            // Where a searching monster thinks the target went.
            if (fsm.CurrentState is SearchState && FSMTargetMemory.Has(fsm))
                Segment(pos, FSMTargetMemory.Position(fsm), SearchColor);
        }

        private void Segment(Vector2 a, Vector2 b, Color color)
        {
            var lr = Next(color, 2);
            lr.SetPosition(0, new Vector3(a.x, a.y, Z));
            lr.SetPosition(1, new Vector3(b.x, b.y, Z));
        }

        private void Circle(Vector2 centre, float radius, Color color)
        {
            if (radius <= 0f) return;
            var lr = Next(color, CIRCLE_SEGMENTS + 1);
            for (int i = 0; i <= CIRCLE_SEGMENTS; i++)
            {
                float a = i / (float)CIRCLE_SEGMENTS * Mathf.PI * 2f;
                lr.SetPosition(i, new Vector3(centre.x + Mathf.Cos(a) * radius,
                                              centre.y + Mathf.Sin(a) * radius, Z));
            }
        }

        private LineRenderer Next(Color color, int points)
        {
            LineRenderer lr;
            if (_used < _pool.Count)
            {
                lr = _pool[_used];
            }
            else
            {
                var go = new GameObject("_AIDebugLine");
                go.transform.SetParent(transform, false);
                lr = go.AddComponent<LineRenderer>();
                lr.useWorldSpace = true;
                lr.widthMultiplier = LINE_WIDTH;
                lr.numCapVertices = 0;
                lr.sortingLayerName = SortingConfig.LAYER_OVERLAY;
                lr.sortingOrder = 500;
                lr.sharedMaterial = EnsureMaterial();
                _pool.Add(lr);
            }

            _used++;
            lr.enabled = true;
            lr.positionCount = points;
            lr.startColor = color;
            lr.endColor = color;
            return lr;
        }

        private static Material EnsureMaterial()
        {
            if (s_material == null)
                s_material = new Material(Shader.Find("Sprites/Default"));
            return s_material;
        }

        private static Vector2 Rotate(Vector2 v, float degrees)
        {
            float rad = degrees * Mathf.Deg2Rad;
            float cos = Mathf.Cos(rad), sin = Mathf.Sin(rad);
            return new Vector2(v.x * cos - v.y * sin, v.x * sin + v.y * cos);
        }
    }
}
