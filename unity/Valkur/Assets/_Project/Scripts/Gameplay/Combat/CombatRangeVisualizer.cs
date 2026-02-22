using UnityEngine;
using Valkur.Gameplay.FSM;

namespace Valkur.Gameplay.Combat
{
    /// <summary>
    /// Runtime visual overlay that draws attack range circles for entities with MeleeCombat.
    /// Toggle with F2. Shows:
    /// - Player melee range (blue arc toward mouse)
    /// - NPC aggro range (yellow circle)
    /// - NPC melee range (red arc toward player)
    /// - NPC facing direction line (green)
    /// Hooks into Camera.onPostRender for reliable GL rendering.
    /// </summary>
    public class CombatRangeVisualizer : MonoBehaviour
    {
        [Header("Settings")]
        [SerializeField] private bool showOnStart = false;
        [SerializeField] private int circleSegments = 48;

        [Header("Colors")]
        [SerializeField] private Color playerMeleeColor = new Color(0.3f, 0.6f, 1f, 0.6f);
        [SerializeField] private Color npcMeleeColor = new Color(1f, 0.3f, 0.2f, 0.5f);
        [SerializeField] private Color npcAggroColor = new Color(1f, 0.9f, 0.2f, 0.15f);
        [SerializeField] private Color npcFacingColor = new Color(0.2f, 1f, 0.3f, 0.6f);

        private bool _visible;
        private Material _glMaterial;

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

            _glMaterial = new Material(Shader.Find("Hidden/Internal-Colored"));
            _glMaterial.hideFlags = HideFlags.HideAndDontSave;
            _glMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            _glMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            _glMaterial.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
            _glMaterial.SetInt("_ZWrite", 0);
            _glMaterial.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);
        }

        private void OnEnable()
        {
            Camera.onPostRender += OnCameraPostRender;
        }

        private void OnDisable()
        {
            Camera.onPostRender -= OnCameraPostRender;
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.F2))
            {
                _visible = !_visible;
                Debug.Log($"[CombatRangeVisualizer] Ranges {(_visible ? "ON" : "OFF")}");
            }
        }

        private void OnCameraPostRender(Camera cam)
        {
            if (!_visible || _glMaterial == null) return;
            if (cam != Camera.main) return;

            _glMaterial.SetPass(0);
            GL.PushMatrix();
            GL.LoadProjectionMatrix(cam.projectionMatrix);
            GL.modelview = cam.worldToCameraMatrix;

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
                    DrawCircle(player.transform.position, range, new Color(playerMeleeColor.r, playerMeleeColor.g, playerMeleeColor.b, 0.12f));
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
                float aggroRange = brain.FSM != null ? brain.FSM.GetContextFloat("aggro_range", 5f) : 5f;
                DrawCircle(pos, aggroRange, npcAggroColor);

                // Melee range + facing direction
                float meleeRange = brain.FSM != null ? brain.FSM.GetContextFloat("melee_range", 1.5f) : 1.5f;
                var npcCombat = brain.GetComponent<MeleeCombat>();
                float npcArc = npcCombat != null ? npcCombat.ArcDegrees : 90f;

                // Determine NPC facing: toward player if in range, else last move direction
                Vector2 npcFacing = Vector2.down;
                if (player != null)
                {
                    Vector2 toPlayer = ((Vector2)player.transform.position - (Vector2)pos);
                    if (toPlayer.sqrMagnitude > 0.01f)
                        npcFacing = toPlayer.normalized;
                }

                DrawArc(pos, npcFacing, meleeRange, npcArc, npcMeleeColor);
                DrawCircle(pos, meleeRange, new Color(npcMeleeColor.r, npcMeleeColor.g, npcMeleeColor.b, 0.1f));

                // Facing direction line
                DrawLine(pos, (Vector3)pos + (Vector3)(npcFacing * meleeRange * 0.8f), npcFacingColor);
            }

            GL.PopMatrix();
        }

        private void DrawCircle(Vector3 center, float radius, Color color)
        {
            GL.Begin(GL.LINES);
            GL.Color(color);

            float step = 360f / circleSegments;
            for (int i = 0; i < circleSegments; i++)
            {
                float a1 = i * step * Mathf.Deg2Rad;
                float a2 = (i + 1) * step * Mathf.Deg2Rad;
                GL.Vertex3(center.x + Mathf.Cos(a1) * radius, center.y + Mathf.Sin(a1) * radius, 0f);
                GL.Vertex3(center.x + Mathf.Cos(a2) * radius, center.y + Mathf.Sin(a2) * radius, 0f);
            }

            GL.End();
        }

        private void DrawArc(Vector3 center, Vector2 direction, float radius, float arcDegrees, Color color)
        {
            float baseAngle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
            float halfArc = arcDegrees * 0.5f;
            int segments = Mathf.Max(8, (int)(circleSegments * arcDegrees / 360f));
            float step = arcDegrees / segments;

            GL.Begin(GL.LINES);
            GL.Color(color);

            for (int i = 0; i < segments; i++)
            {
                float a1 = (baseAngle - halfArc + i * step) * Mathf.Deg2Rad;
                float a2 = (baseAngle - halfArc + (i + 1) * step) * Mathf.Deg2Rad;
                GL.Vertex3(center.x + Mathf.Cos(a1) * radius, center.y + Mathf.Sin(a1) * radius, 0f);
                GL.Vertex3(center.x + Mathf.Cos(a2) * radius, center.y + Mathf.Sin(a2) * radius, 0f);
            }

            // Side lines from center to arc edges
            float startAngle = (baseAngle - halfArc) * Mathf.Deg2Rad;
            float endAngle = (baseAngle + halfArc) * Mathf.Deg2Rad;

            GL.Vertex3(center.x, center.y, 0f);
            GL.Vertex3(center.x + Mathf.Cos(startAngle) * radius, center.y + Mathf.Sin(startAngle) * radius, 0f);

            GL.Vertex3(center.x, center.y, 0f);
            GL.Vertex3(center.x + Mathf.Cos(endAngle) * radius, center.y + Mathf.Sin(endAngle) * radius, 0f);

            GL.End();
        }

        private void DrawLine(Vector3 from, Vector3 to, Color color)
        {
            GL.Begin(GL.LINES);
            GL.Color(color);
            GL.Vertex3(from.x, from.y, 0f);
            GL.Vertex3(to.x, to.y, 0f);
            GL.End();
        }

        private void OnDestroy()
        {
            Camera.onPostRender -= OnCameraPostRender;
            if (_glMaterial != null)
                DestroyImmediate(_glMaterial);
            if (_instance == this)
                _instance = null;
        }
    }
}
