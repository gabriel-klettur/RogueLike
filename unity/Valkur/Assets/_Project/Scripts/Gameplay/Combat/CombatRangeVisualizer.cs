using System.Collections.Generic;
using UnityEngine;
using Valkur.Gameplay.FSM;

namespace Valkur.Gameplay.Combat
{
    /// <summary>
    /// Runtime visual overlay that draws attack range circles/arcs for entities.
    /// Toggle with F2. Uses LineRenderer objects (URP-compatible).
    /// - Player melee range (blue arc toward mouse)
    /// - NPC aggro range (yellow circle)
    /// - NPC melee range (red arc toward player)
    /// - NPC facing direction line (green)
    /// </summary>
    public class CombatRangeVisualizer : MonoBehaviour
    {
        [Header("Settings")]
        [SerializeField] private bool showOnStart = false;
        [SerializeField] private int circleSegments = 48;
        [SerializeField] private float lineWidth = 0.03f;

        [Header("Colors")]
        [SerializeField] private Color playerMeleeColor = new Color(0.3f, 0.6f, 1f, 0.6f);
        [SerializeField] private Color npcMeleeColor = new Color(1f, 0.3f, 0.2f, 0.5f);
        [SerializeField] private Color npcAggroColor = new Color(1f, 0.9f, 0.2f, 0.15f);
        [SerializeField] private Color npcFacingColor = new Color(0.2f, 1f, 0.3f, 0.6f);

        private bool _visible;
        private Material _lineMaterial;
        private readonly List<LineRenderer> _activeLines = new List<LineRenderer>();
        private readonly List<LineRenderer> _pool = new List<LineRenderer>();
        private int _lineIndex;

        private static CombatRangeVisualizer _instance;
        public static CombatRangeVisualizer Instance => _instance;

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;
            _visible = showOnStart;

            _lineMaterial = new Material(Shader.Find("Sprites/Default"));
            _lineMaterial.hideFlags = HideFlags.HideAndDontSave;
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.F2))
            {
                _visible = !_visible;
                Debug.Log($"[CombatRangeVisualizer] Ranges {(_visible ? "ON" : "OFF")}");
            }
        }

        private void LateUpdate()
        {
            // Reset all lines — return to pool
            for (int i = 0; i < _activeLines.Count; i++)
            {
                if (_activeLines[i] != null)
                {
                    _activeLines[i].enabled = false;
                    _pool.Add(_activeLines[i]);
                }
            }
            _activeLines.Clear();
            _lineIndex = 0;

            if (!_visible) return;

            var player = GameObject.FindGameObjectWithTag("Player");

            // Player ranges
            if (player != null)
            {
                var pc = player.GetComponent<PlayerController>();
                Vector2 facing = pc != null ? pc.FacingDirection : Vector2.right;

                var combat = player.GetComponent<MeleeCombat>();
                if (combat != null)
                {
                    float range = combat.Range;
                    float arc = combat.ArcDegrees;
                    DrawArc(player.transform.position, facing, range, arc, playerMeleeColor);
                    DrawCircle(player.transform.position, range,
                        new Color(playerMeleeColor.r, playerMeleeColor.g, playerMeleeColor.b, 0.12f));
                }
            }

            // NPC ranges
            var brains = FindObjectsOfType<FSMMonsterBrain>();
            foreach (var brain in brains)
            {
                if (brain == null || !brain.enabled) continue;
                var health = brain.GetComponent<Health>();
                if (health != null && health.IsDead) continue;

                Vector3 pos = brain.transform.position;

                // Aggro range
                float aggroRange = brain.FSM != null
                    ? brain.FSM.GetContextFloat("aggro_range", 5f) : 5f;
                DrawCircle(pos, aggroRange, npcAggroColor);

                // Melee range + facing direction
                float meleeRange = brain.FSM != null
                    ? brain.FSM.GetContextFloat("melee_range", 1.5f) : 1.5f;
                var npcCombat = brain.GetComponent<MeleeCombat>();
                float npcArc = npcCombat != null ? npcCombat.ArcDegrees : 90f;

                Vector2 npcFacing = Vector2.down;
                if (player != null)
                {
                    Vector2 toPlayer = ((Vector2)player.transform.position - (Vector2)pos);
                    if (toPlayer.sqrMagnitude > 0.01f)
                        npcFacing = toPlayer.normalized;
                }

                DrawArc(pos, npcFacing, meleeRange, npcArc, npcMeleeColor);
                DrawCircle(pos, meleeRange,
                    new Color(npcMeleeColor.r, npcMeleeColor.g, npcMeleeColor.b, 0.1f));

                // Facing direction line
                DrawLine(pos, (Vector3)pos + (Vector3)(npcFacing * meleeRange * 0.8f), npcFacingColor);
            }
        }

        private LineRenderer GetLine()
        {
            LineRenderer lr;
            if (_pool.Count > 0)
            {
                lr = _pool[_pool.Count - 1];
                _pool.RemoveAt(_pool.Count - 1);
            }
            else
            {
                var go = new GameObject($"_RangeViz_{_lineIndex}");
                go.transform.SetParent(transform);
                lr = go.AddComponent<LineRenderer>();
                lr.material = _lineMaterial;
                lr.useWorldSpace = true;
                lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                lr.receiveShadows = false;
                lr.sortingLayerName = "Overlay";
                lr.sortingOrder = 999;
            }

            lr.enabled = true;
            lr.startWidth = lineWidth;
            lr.endWidth = lineWidth;
            _activeLines.Add(lr);
            _lineIndex++;
            return lr;
        }

        private void DrawCircle(Vector3 center, float radius, Color color)
        {
            var lr = GetLine();
            lr.startColor = color;
            lr.endColor = color;
            lr.loop = true;
            lr.positionCount = circleSegments;

            float step = 360f / circleSegments;
            for (int i = 0; i < circleSegments; i++)
            {
                float a = i * step * Mathf.Deg2Rad;
                lr.SetPosition(i, new Vector3(
                    center.x + Mathf.Cos(a) * radius,
                    center.y + Mathf.Sin(a) * radius,
                    0f));
            }
        }

        private void DrawArc(Vector3 center, Vector2 direction, float radius,
            float arcDegrees, Color color)
        {
            float baseAngle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
            float halfArc = arcDegrees * 0.5f;
            int segments = Mathf.Max(8, (int)(circleSegments * arcDegrees / 360f));
            float step = arcDegrees / segments;

            // Arc outline + two side lines back to center
            // Points: center → arc start → ... → arc end → center
            var lr = GetLine();
            lr.startColor = color;
            lr.endColor = color;
            lr.loop = false;
            lr.positionCount = segments + 3;

            lr.SetPosition(0, center);
            for (int i = 0; i <= segments; i++)
            {
                float a = (baseAngle - halfArc + i * step) * Mathf.Deg2Rad;
                lr.SetPosition(i + 1, new Vector3(
                    center.x + Mathf.Cos(a) * radius,
                    center.y + Mathf.Sin(a) * radius,
                    0f));
            }
            lr.SetPosition(segments + 2, center);
        }

        private void DrawLine(Vector3 from, Vector3 to, Color color)
        {
            var lr = GetLine();
            lr.startColor = color;
            lr.endColor = color;
            lr.loop = false;
            lr.positionCount = 2;
            lr.SetPosition(0, from);
            lr.SetPosition(1, to);
        }

        private void OnDestroy()
        {
            if (_lineMaterial != null)
                DestroyImmediate(_lineMaterial);
            if (_instance == this)
                _instance = null;
        }
    }
}
