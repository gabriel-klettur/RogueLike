using UnityEngine;
using UnityEngine.UI;

namespace Valkur.Gameplay.Editors
{
    /// <summary>
    /// Procedural UI graphic that paints a small icon evoking the look of a
    /// particle preset (lightning bolts, aura halo, smoke puffs, slash arc, …).
    ///
    /// Used by <c>SpellsRuntimeEditor</c> picker when a spell has no sprite —
    /// gives the catalog a "particle preview" feel similar to the Python
    /// editor's live <c>ParticlePreviewManager</c>, but rendered statically as
    /// triangle primitives so we can show 30+ slots cheaply.
    ///
    /// Configure once via <see cref="Configure"/>; mesh is rebuilt on demand.
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public class SpellPreviewGraphic : MaskableGraphic
    {
        [SerializeField] private string _kind = "explosion";
        [SerializeField] private Color _primary = Color.white;
        [SerializeField] private Color _secondary = Color.white;

        public void Configure(string kind, Color primary, Color secondary)
        {
            _kind = string.IsNullOrEmpty(kind) ? "explosion" : kind.ToLowerInvariant();
            _primary = primary;
            _secondary = secondary;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            var rect = GetPixelAdjustedRect();
            var c = new Vector2(rect.x + rect.width * 0.5f, rect.y + rect.height * 0.5f);
            float r = Mathf.Min(rect.width, rect.height) * 0.5f;

            switch (_kind)
            {
                case "lightning":
                case "chain_lightning":         DrawLightning(vh, c, r); break;
                case "aura":                    DrawAura(vh, c, r); break;
                case "dash":                    DrawDash(vh, c, r); break;
                case "slash":                   DrawSlash(vh, c, r); break;
                case "smoke":
                case "smoke_emitter":           DrawSmoke(vh, c, r); break;
                case "beam":
                case "laser":                   DrawBeam(vh, c, r); break;
                case "arcane_flame":            DrawFlame(vh, c, r); break;
                case "firework":
                case "firework_launch":        DrawFirework(vh, c, r); break;
                case "teleport":
                case "vortex":
                case "vortex_field":            DrawSpiral(vh, c, r); break;
                case "wall":                    DrawWall(vh, c, r); break;
                case "mine":
                case "trap":                    DrawMine(vh, c, r); break;
                case "cone":
                case "cone_breath":             DrawCone(vh, c, r); break;
                case "shield":
                case "sphere_magic_shield":     DrawShield(vh, c, r); break;
                case "boomerang":               DrawBoomerang(vh, c, r); break;
                case "meteor":                  DrawMeteor(vh, c, r); break;
                case "puddle":                  DrawPuddle(vh, c, r); break;
                case "summon":                  DrawStar(vh, c, r); break;
                case "totem":                   DrawTotem(vh, c, r); break;
                case "projectile":              DrawProjectile(vh, c, r); break;
                case "explosion":
                case "area":
                default:                        DrawExplosion(vh, c, r); break;
            }
        }

        // ── primitives ─────────────────────────────────────────────────────

        private static void AddTri(VertexHelper vh, Vector2 a, Vector2 b, Vector2 c, Color col)
        {
            int i = vh.currentVertCount;
            UIVertex v = UIVertex.simpleVert; v.color = col;
            v.position = a; vh.AddVert(v);
            v.position = b; vh.AddVert(v);
            v.position = c; vh.AddVert(v);
            vh.AddTriangle(i, i + 1, i + 2);
        }

        private static void AddQuad(VertexHelper vh, Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color col)
        {
            int i = vh.currentVertCount;
            UIVertex v = UIVertex.simpleVert; v.color = col;
            v.position = a; vh.AddVert(v);
            v.position = b; vh.AddVert(v);
            v.position = c; vh.AddVert(v);
            v.position = d; vh.AddVert(v);
            vh.AddTriangle(i, i + 1, i + 2);
            vh.AddTriangle(i, i + 2, i + 3);
        }

        private static void AddLine(VertexHelper vh, Vector2 from, Vector2 to, float thickness, Color col)
        {
            var d = to - from;
            if (d.sqrMagnitude < 1e-6f) return;
            d.Normalize();
            var n = new Vector2(-d.y, d.x) * thickness * 0.5f;
            AddQuad(vh, from - n, to - n, to + n, from + n, col);
        }

        private static void AddCircle(VertexHelper vh, Vector2 center, float radius, int segments, Color col)
        {
            int start = vh.currentVertCount;
            UIVertex v = UIVertex.simpleVert; v.color = col;
            v.position = center; vh.AddVert(v);
            for (int i = 0; i <= segments; i++)
            {
                float a = (i / (float)segments) * Mathf.PI * 2f;
                v.position = center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius;
                vh.AddVert(v);
            }
            for (int i = 1; i <= segments; i++)
                vh.AddTriangle(start, start + i, start + i + 1);
        }

        private static void AddRing(VertexHelper vh, Vector2 center, float innerR, float outerR, int segments, Color col)
        {
            int start = vh.currentVertCount;
            UIVertex v = UIVertex.simpleVert; v.color = col;
            for (int i = 0; i <= segments; i++)
            {
                float a = (i / (float)segments) * Mathf.PI * 2f;
                var dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                v.position = center + dir * innerR; vh.AddVert(v);
                v.position = center + dir * outerR; vh.AddVert(v);
            }
            for (int i = 0; i < segments; i++)
            {
                int b = start + i * 2;
                vh.AddTriangle(b, b + 1, b + 3);
                vh.AddTriangle(b, b + 3, b + 2);
            }
        }

        // ── drawers ────────────────────────────────────────────────────────

        private void DrawLightning(VertexHelper vh, Vector2 c, float r)
        {
            var col = _primary; col.a = 1f;
            float t = r * 0.12f;
            for (int b = 0; b < 3; b++)
            {
                float ox = (b - 1) * r * 0.42f;
                Vector2 p0 = c + new Vector2(ox + r * 0.10f,  r * 0.85f);
                Vector2 p1 = c + new Vector2(ox - r * 0.18f,  r * 0.30f);
                Vector2 p2 = c + new Vector2(ox + r * 0.18f, -r * 0.20f);
                Vector2 p3 = c + new Vector2(ox - r * 0.10f, -r * 0.85f);
                AddLine(vh, p0, p1, t, col);
                AddLine(vh, p1, p2, t, col);
                AddLine(vh, p2, p3, t, col);
            }
        }

        private void DrawAura(VertexHelper vh, Vector2 c, float r)
        {
            var soft = _primary; soft.a = 0.30f;
            var mid  = _primary; mid.a  = 0.55f;
            var hot  = _primary; hot.a  = 1.00f;
            AddCircle(vh, c, r * 0.95f, 28, soft);
            AddRing(vh, c, r * 0.55f, r * 0.72f, 28, mid);
            AddCircle(vh, c, r * 0.18f, 14, hot);
        }

        private void DrawDash(VertexHelper vh, Vector2 c, float r)
        {
            var col = _primary; col.a = 0.85f;
            float t = r * 0.10f;
            for (int i = -1; i <= 1; i++)
            {
                Vector2 from = c + new Vector2(-r * 0.85f, i * r * 0.35f);
                Vector2 to   = c + new Vector2( r * 0.45f, i * r * 0.35f);
                AddLine(vh, from, to, t, col);
            }
            // arrow head
            AddTri(vh,
                c + new Vector2( r * 0.85f,  0),
                c + new Vector2( r * 0.40f,  r * 0.45f),
                c + new Vector2( r * 0.40f, -r * 0.45f), col);
        }

        private void DrawSlash(VertexHelper vh, Vector2 c, float r)
        {
            var col = _primary; col.a = 0.95f;
            float t = r * 0.18f;
            int steps = 14;
            float startAng = -50f, endAng = 130f;
            Vector2 prev = Vector2.zero;
            for (int i = 0; i <= steps; i++)
            {
                float a = Mathf.Lerp(startAng, endAng, i / (float)steps) * Mathf.Deg2Rad;
                Vector2 p = c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r * 0.72f;
                if (i > 0) AddLine(vh, prev, p, t, col);
                prev = p;
            }
        }

        private void DrawSmoke(VertexHelper vh, Vector2 c, float r)
        {
            var col = _primary; col.a = 0.75f;
            AddCircle(vh, c + new Vector2(-r * 0.35f, -r * 0.15f), r * 0.45f, 16, col);
            AddCircle(vh, c + new Vector2( r * 0.35f, -r * 0.05f), r * 0.50f, 16, col);
            AddCircle(vh, c + new Vector2( 0f,         r * 0.30f), r * 0.55f, 16, col);
        }

        private void DrawBeam(VertexHelper vh, Vector2 c, float r)
        {
            var glow = _primary; glow.a = 0.40f;
            var core = _primary; core.a = 1.00f;
            AddLine(vh, c + new Vector2(-r, 0), c + new Vector2(r, 0), r * 0.55f, glow);
            AddLine(vh, c + new Vector2(-r, 0), c + new Vector2(r, 0), r * 0.20f, core);
        }

        private void DrawFlame(VertexHelper vh, Vector2 c, float r)
        {
            var cool = _secondary.a > 0.01f ? _secondary : _primary; cool.a = 0.6f;
            var hot  = _primary; hot.a = 1f;
            AddTri(vh,
                c + new Vector2(-r * 0.7f, -r * 0.7f),
                c + new Vector2( r * 0.7f, -r * 0.7f),
                c + new Vector2( 0f,        r * 0.9f), cool);
            AddTri(vh,
                c + new Vector2(-r * 0.4f, -r * 0.5f),
                c + new Vector2( r * 0.4f, -r * 0.5f),
                c + new Vector2( 0f,        r * 0.5f), hot);
        }

        private void DrawFirework(VertexHelper vh, Vector2 c, float r)
        {
            var col = _primary; col.a = 0.95f;
            float t = r * 0.08f;
            int rays = 10;
            for (int i = 0; i < rays; i++)
            {
                float a = (i / (float)rays) * Mathf.PI * 2f;
                var dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                AddLine(vh, c + dir * r * 0.20f, c + dir * r * 0.85f, t, col);
            }
            AddCircle(vh, c, r * 0.18f, 12, col);
        }

        private void DrawSpiral(VertexHelper vh, Vector2 c, float r)
        {
            var col = _primary; col.a = 0.95f;
            float t = r * 0.10f;
            int steps = 32;
            Vector2 prev = c;
            for (int i = 1; i <= steps; i++)
            {
                float p = i / (float)steps;
                float ang = p * Mathf.PI * 4f;
                float rad = r * 0.85f * p;
                Vector2 v = c + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * rad;
                AddLine(vh, prev, v, t * (1f - p * 0.5f), col);
                prev = v;
            }
        }

        private void DrawWall(VertexHelper vh, Vector2 c, float r)
        {
            var col = _primary; col.a = 1f;
            AddQuad(vh,
                c + new Vector2(-r * 0.85f, -r * 0.55f),
                c + new Vector2( r * 0.85f, -r * 0.55f),
                c + new Vector2( r * 0.85f,  r * 0.55f),
                c + new Vector2(-r * 0.85f,  r * 0.55f), col);
            var dark = col * 0.55f; dark.a = 1f;
            AddLine(vh, c + new Vector2(-r * 0.85f, 0), c + new Vector2(r * 0.85f, 0), r * 0.05f, dark);
            AddLine(vh, c + new Vector2( 0,        -r * 0.55f), c + new Vector2(0, 0), r * 0.05f, dark);
            AddLine(vh, c + new Vector2(-r * 0.4f,  0),         c + new Vector2(-r * 0.4f, r * 0.55f), r * 0.05f, dark);
            AddLine(vh, c + new Vector2( r * 0.4f,  0),         c + new Vector2( r * 0.4f, r * 0.55f), r * 0.05f, dark);
        }

        private void DrawMine(VertexHelper vh, Vector2 c, float r)
        {
            var col = _primary; col.a = 1f;
            AddCircle(vh, c, r * 0.55f, 24, col);
            float t = r * 0.10f;
            int spikes = 8;
            for (int i = 0; i < spikes; i++)
            {
                float a = (i / (float)spikes) * Mathf.PI * 2f;
                var dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                AddLine(vh, c + dir * r * 0.50f, c + dir * r * 0.90f, t, col);
            }
        }

        private void DrawCone(VertexHelper vh, Vector2 c, float r)
        {
            var col = _primary; col.a = 0.85f;
            AddTri(vh,
                c + new Vector2(-r * 0.85f, -r * 0.7f),
                c + new Vector2( r * 0.85f, -r * 0.7f),
                c + new Vector2( 0f,         r * 0.85f), col);
        }

        private void DrawShield(VertexHelper vh, Vector2 c, float r)
        {
            var fill    = _primary; fill.a = 0.30f;
            var outline = _primary; outline.a = 1f;
            AddCircle(vh, c, r * 0.85f, 32, fill);
            AddRing(vh, c, r * 0.78f, r * 0.88f, 32, outline);
        }

        private void DrawBoomerang(VertexHelper vh, Vector2 c, float r)
        {
            var col = _primary; col.a = 1f;
            AddTri(vh,
                c + new Vector2(-r * 0.80f, -r * 0.20f),
                c + new Vector2( r * 0.20f,  r * 0.60f),
                c + new Vector2( 0f,         0f), col);
            AddTri(vh,
                c + new Vector2( r * 0.80f, -r * 0.20f),
                c + new Vector2(-r * 0.20f,  r * 0.60f),
                c + new Vector2( 0f,         0f), col);
        }

        private void DrawMeteor(VertexHelper vh, Vector2 c, float r)
        {
            var trail = _primary; trail.a = 0.4f;
            var head  = _primary; head.a  = 1f;
            AddLine(vh,
                c + new Vector2(-r * 0.85f,  r * 0.85f),
                c + new Vector2( r * 0.40f, -r * 0.40f),
                r * 0.15f, trail);
            AddCircle(vh, c + new Vector2(r * 0.40f, -r * 0.40f), r * 0.30f, 16, head);
        }

        private void DrawPuddle(VertexHelper vh, Vector2 c, float r)
        {
            var col = _primary; col.a = 0.85f;
            int seg = 24;
            int start = vh.currentVertCount;
            UIVertex v = UIVertex.simpleVert; v.color = col;
            v.position = c; vh.AddVert(v);
            for (int i = 0; i <= seg; i++)
            {
                float a = (i / (float)seg) * Mathf.PI * 2f;
                v.position = c + new Vector2(Mathf.Cos(a) * r * 0.85f, Mathf.Sin(a) * r * 0.45f);
                vh.AddVert(v);
            }
            for (int i = 1; i <= seg; i++) vh.AddTriangle(start, start + i, start + i + 1);
        }

        private void DrawStar(VertexHelper vh, Vector2 c, float r)
        {
            var col = _primary; col.a = 1f;
            int points = 5;
            float outerR = r * 0.85f, innerR = r * 0.40f;
            int start = vh.currentVertCount;
            UIVertex v = UIVertex.simpleVert; v.color = col;
            v.position = c; vh.AddVert(v);
            for (int i = 0; i < points * 2; i++)
            {
                float a = -Mathf.PI / 2f + i * (Mathf.PI / points);
                float rad = (i % 2 == 0) ? outerR : innerR;
                v.position = c + new Vector2(Mathf.Cos(a) * rad, Mathf.Sin(a) * rad);
                vh.AddVert(v);
            }
            int n = points * 2;
            for (int i = 0; i < n; i++)
                vh.AddTriangle(start, start + 1 + i, start + 1 + ((i + 1) % n));
        }

        private void DrawTotem(VertexHelper vh, Vector2 c, float r)
        {
            var col = _primary; col.a = 1f;
            var dark = col * 0.55f; dark.a = 1f;
            AddQuad(vh,
                c + new Vector2(-r * 0.40f, -r * 0.85f),
                c + new Vector2( r * 0.40f, -r * 0.85f),
                c + new Vector2( r * 0.40f,  r * 0.85f),
                c + new Vector2(-r * 0.40f,  r * 0.85f), col);
            for (int i = -1; i <= 1; i++)
                AddLine(vh,
                    c + new Vector2(-r * 0.40f, i * r * 0.40f),
                    c + new Vector2( r * 0.40f, i * r * 0.40f),
                    r * 0.06f, dark);
        }

        private void DrawProjectile(VertexHelper vh, Vector2 c, float r)
        {
            var trail = _primary; trail.a = 0.4f;
            var head  = _primary; head.a  = 1f;
            // streak left → right
            AddLine(vh, c + new Vector2(-r * 0.85f, 0), c + new Vector2(r * 0.30f, 0), r * 0.18f, trail);
            AddCircle(vh, c + new Vector2(r * 0.40f, 0), r * 0.32f, 16, head);
        }

        private void DrawExplosion(VertexHelper vh, Vector2 c, float r)
        {
            var core = _primary; core.a = 1f;
            var dot  = _primary; dot.a  = 0.7f;
            AddCircle(vh, c, r * 0.28f, 16, core);
            int n = 8;
            for (int i = 0; i < n; i++)
            {
                float a = (i / (float)n) * Mathf.PI * 2f;
                var dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                AddCircle(vh, c + dir * r * 0.65f, r * 0.12f, 10, dot);
            }
        }
    }
}
