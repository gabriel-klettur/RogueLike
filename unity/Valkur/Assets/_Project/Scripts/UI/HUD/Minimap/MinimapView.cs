using UnityEngine;
using UnityEngine.UI;
using Valkur.Data;

namespace Valkur.UI.HUD
{
    /// <summary>Everything a map view needs to know about this frame, gathered once.</summary>
    public struct MinimapFrame
    {
        public MinimapStyle Style;
        public Texture Atlas;
        public Vector4 AtlasRect;
        public Texture Fog;
        public Vector4 FogRect;
        public bool HasPlayer;
        public Vector2 Player;
        public Vector2 Facing;
        public float Time;
        public Color DayTint;
        public float Desaturate;
        public float Whiten;
        public float PingRadius;
        public float PingStrength;
        /// <summary>Half size in world units of what the game camera shows, or zero.</summary>
        public Vector2 CameraHalfExtent;
        public Vector2 CameraCentre;
        public bool Spirit;
        /// <summary>Fog of war, for <see cref="MinimapReveal"/> gating. Null with fog off.</summary>
        public MinimapFogMap FogMap;
        /// <summary>World radius the player can currently see creatures in.</summary>
        public float SightRadius;
        /// <summary>
        /// Inside an interior, items outside the room are in ANOTHER coordinate space — the town
        /// the room was loaded over — and are dropped rather than pinned. The first build pinned
        /// the player's outdoor destination to the rim of a bedroom, "211 m" away.
        /// </summary>
        public bool ConfineToBounds;
        public Rect Bounds;
    }

    /// <summary>
    /// One map on screen: a terrain graphic driven by the composite shader, and three quad
    /// layers over it (glows under, glyphs, sparks over). The minimap is one of these with a
    /// circular shape; the world map is one with a rectangular shape and a pannable centre.
    /// </summary>
    public sealed class MinimapView
    {
        private static readonly int IdFog       = Shader.PropertyToID("_FogTex");
        private static readonly int IdAtlasRect = Shader.PropertyToID("_AtlasRect");
        private static readonly int IdFogRect   = Shader.PropertyToID("_FogRect");
        private static readonly int IdView      = Shader.PropertyToID("_View");
        private static readonly int IdShape     = Shader.PropertyToID("_Shape");
        private static readonly int IdPlayer    = Shader.PropertyToID("_Player");
        private static readonly int IdLook      = Shader.PropertyToID("_Look");
        private static readonly int IdLook2     = Shader.PropertyToID("_Look2");
        private static readonly int IdTint      = Shader.PropertyToID("_Tint");
        private static readonly int IdWeather   = Shader.PropertyToID("_Weather");
        private static readonly int IdFogLight  = Shader.PropertyToID("_FogLight");
        private static readonly int IdFogDark   = Shader.PropertyToID("_FogDark");
        private static readonly int IdFrontier  = Shader.PropertyToID("_Frontier");
        private static readonly int IdCloud     = Shader.PropertyToID("_Cloud");
        private static readonly int IdVoid      = Shader.PropertyToID("_Void");
        private static readonly int IdFog2      = Shader.PropertyToID("_Fog2");
        private static readonly int IdPing      = Shader.PropertyToID("_Ping");
        private static readonly int IdPingColor = Shader.PropertyToID("_PingColor");

        public readonly RawImage Map;
        public readonly MinimapQuadGraphic Under;
        public readonly MinimapQuadGraphic Glyphs;
        public readonly MinimapQuadGraphic Over;
        private readonly Material _material;
        private readonly bool _circle;
        private readonly System.Func<Vector2, Vector2> _project;

        /// <summary>A glyph as it was drawn this frame, for hover picking.</summary>
        public struct Drawn { public Vector2 Local; public float Radius; public string Label; public Vector2 World; }

        /// <summary>An edge pin as it was drawn this frame, for its distance label.</summary>
        public struct Pin { public Vector2 Local; public Vector2 Inward; public float Distance; public Color Color; }

        private readonly System.Collections.Generic.List<Drawn> _drawn = new System.Collections.Generic.List<Drawn>(96);
        private readonly System.Collections.Generic.List<Pin> _pins = new System.Collections.Generic.List<Pin>(8);

        /// <summary>Edge pins drawn this frame.</summary>
        public System.Collections.Generic.IReadOnlyList<Pin> Pins => _pins;

        /// <summary>World position at the centre of the view.</summary>
        public Vector2 Centre;

        /// <summary>World units from the centre to the top edge.</summary>
        public float HalfHeightWorld = 24f;

        /// <summary>Multiplier on every glyph size (the world map draws them larger).</summary>
        public float GlyphScale = 1f;

        /// <summary>Canvas units an edge pin sits inside the rim.</summary>
        public float EdgeInset = 8f;

        /// <summary>
        /// How far toward full brightness remembered ground is lifted. The dial dims what the
        /// player cannot currently see; the world map is a record of what they KNOW, and at the
        /// dial's dimming most of it read as a second, darker fog.
        /// </summary>
        public float RememberedLift;

        public MinimapView(RawImage map, MinimapQuadGraphic under, MinimapQuadGraphic glyphs, MinimapQuadGraphic over,
                           Material material, bool circle)
        {
            Map = map;
            Under = under;
            Glyphs = glyphs;
            Over = over;
            _material = material;
            _circle = circle;
            _project = WorldToLocal;
            if (Map != null) Map.material = _material;
        }

        public bool IsCircle => _circle;

        /// <summary>Half size of the map rect in canvas units.</summary>
        public Vector2 HalfSize => Map != null ? Map.rectTransform.rect.size * 0.5f : Vector2.one;

        /// <summary>World units from the centre to each edge.</summary>
        public Vector2 HalfExtent
        {
            get
            {
                var hs = HalfSize;
                float aspect = hs.y > 0.01f ? hs.x / hs.y : 1f;
                return new Vector2(HalfHeightWorld * aspect, HalfHeightWorld);
            }
        }

        public Vector2 WorldToLocal(Vector2 world) => MinimapProjection.WorldToLocal(world, Centre, HalfExtent, HalfSize);
        public Vector2 LocalToWorld(Vector2 local) => MinimapProjection.LocalToWorld(local, Centre, HalfExtent, HalfSize);

        /// <summary>Canvas units per world unit at the current zoom.</summary>
        public float UnitsPerWorld => MinimapProjection.UnitsPerWorld(HalfHeightWorld, HalfSize.y);

        /// <summary>Radius of the circular view in canvas units.</summary>
        public float Radius => HalfSize.x;

        /// <summary>Draw one frame.</summary>
        public void Draw(in MinimapFrame f, MinimapScene scene, MinimapFx fx)
        {
            ApplyMaterial(in f);

            Under.Clear();
            Glyphs.Clear();
            Over.Clear();
            _drawn.Clear();
            _pins.Clear();

            DrawCameraFrame(in f);
            DrawItems(in f, scene);
            DrawPlayer(in f);
            if (fx != null) fx.Draw(Under, Over, _project, _circle ? Radius : 0f);

            Under.Commit();
            Glyphs.Commit();
            Over.Commit();
        }

        private void ApplyMaterial(in MinimapFrame f)
        {
            var s = f.Style;
            Map.texture = f.Atlas != null ? f.Atlas : Texture2D.blackTexture;
            var m = _material;
            m.SetTexture(IdFog, f.Fog != null ? f.Fog : Texture2D.blackTexture);
            m.SetVector(IdAtlasRect, f.AtlasRect);
            m.SetVector(IdFogRect, f.FogRect);
            var he = HalfExtent;
            m.SetVector(IdView, new Vector4(Centre.x, Centre.y, he.x, he.y));
            m.SetVector(IdShape, _circle ? new Vector4(1f, 0.012f, 0.42f, 0.38f) : new Vector4(0f, 0f, 0.35f, 0f));
            m.SetVector(IdPlayer, new Vector4(f.Player.x, f.Player.y, s.revealRadius, f.HasPlayer ? 1f : 0f));
            m.SetVector(IdLook, new Vector4(s.saturation, s.contrast, s.brightness, s.edgeInk));
            // The grid only reads at the world map's scale; on the dial it is noise.
            float grid = _circle ? s.gridStrength * 0.5f : s.gridStrength;
            float remB = Mathf.Lerp(s.rememberedBrightness, 1f, RememberedLift);
            float remS = Mathf.Lerp(s.rememberedSaturation, 1f, RememberedLift);
            m.SetVector(IdLook2, new Vector4(remB, remS, grid, Mathf.Max(1f, s.gridSpacing)));
            var tint = f.DayTint;
            m.SetColor(IdTint, new Color(tint.r, tint.g, tint.b, s.dayNightTint));
            m.SetVector(IdWeather, new Vector4(f.Desaturate, f.Whiten, f.Time, 0f));
            m.SetColor(IdFogLight, f.Spirit ? Color.Lerp(s.fogInkLight, new Color(0.12f, 0.16f, 0.24f), 0.6f) : s.fogInkLight);
            m.SetColor(IdFogDark, s.fogInkDark);
            m.SetColor(IdFrontier, s.frontierGlow);
            m.SetVector(IdCloud, new Vector4(s.cloudStrength, s.cloudScale, s.cloudSpeed, 0f));
            m.SetColor(IdVoid, s.voidColor);
            m.SetVector(IdFog2, new Vector4(s.unexploredEcho, s.hatchStrength, Mathf.Max(0.1f, s.hatchSpacing), s.waterShimmer));
            m.SetVector(IdPing, new Vector4(f.PingRadius, f.PingStrength, 1.4f, 0f));
            m.SetColor(IdPingColor, s.frontierGlow);
        }

        private void DrawItems(in MinimapFrame f, MinimapScene scene)
        {
            var s = f.Style;
            var items = scene.Items;
            float r = Radius;
            Vector2 hs = HalfSize;
            for (int i = 0; i < items.Count; i++)
            {
                var it = items[i];
                if (f.ConfineToBounds && !f.Bounds.Contains(it.World)) continue;
                if (!IsRevealed(in f, in it)) continue;
                if (it.Reveal == MinimapReveal.Seen && f.HasPlayer)
                {
                    // Fade in over the last few units of sight, so a creature walking into view
                    // arrives instead of popping.
                    float dist = (it.World - f.Player).magnitude;
                    it.Color.a *= Mathf.Clamp01((f.SightRadius - dist) / 3f);
                    if (it.Color.a <= 0.01f) continue;
                }
                float size = it.Size * GlyphScale;
                Vector2 local = WorldToLocal(it.World);
                if (it.Badge) local.y += size * 0.62f;
                if (it.Bob) local.y += Mathf.Sin(f.Time * 3.1f + (it.Key & 1023) * 0.37f) * 1.6f * GlyphScale;

                bool inside = _circle
                    ? MinimapProjection.InsideCircle(local, r - size * 0.35f)
                    : Mathf.Abs(local.x) <= hs.x - size * 0.35f && Mathf.Abs(local.y) <= hs.y - size * 0.35f;

                if (!inside)
                {
                    if (!it.EdgePin) continue;
                    DrawEdgePin(in f, local, it.Icon, it.Color, s.edgePinSize * GlyphScale);
                    if (f.HasPlayer) RecordPin(local, it.Color, (it.World - f.Player).magnitude);
                    continue;
                }

                if (it.Halo > 0f)
                {
                    float breathe = 0.62f + 0.38f * Mathf.Sin(f.Time * 2.2f + (it.Key & 255) * 0.21f);
                    var hc = it.Color;
                    hc.a = it.Halo * breathe * 0.7f;
                    Under.Add(local, size * 2.8f, MinimapIcon.Glow, hc);
                }
                Glyphs.Add(local, size, it.Icon, it.Color);
                _drawn.Add(new Drawn { Local = local, Radius = Mathf.Max(6f, size * 0.6f), Label = it.Label, World = it.World });
            }
        }

        private void RecordPin(Vector2 local, Color color, float distance)
        {
            Vector2 p = local;
            if (_circle) MinimapProjection.ClampToRim(ref p, Radius - EdgeInset, out _);
            else MinimapProjection.ClampToRect(ref p, HalfSize - new Vector2(EdgeInset, EdgeInset), out _);
            Vector2 dir = local.sqrMagnitude > 1e-4f ? local.normalized : Vector2.up;
            _pins.Add(new Pin { Local = p, Inward = -dir, Distance = distance, Color = color });
        }

        /// <summary>The label of the glyph nearest a map-local point, if one is under it.</summary>
        public bool TryPick(Vector2 local, out string label, out Vector2 at)
        {
            label = null;
            at = default;
            float best = float.MaxValue;
            for (int i = _drawn.Count - 1; i >= 0; i--)   // later glyphs are drawn on top
            {
                var d = _drawn[i];
                float dist = (d.Local - local).sqrMagnitude;
                if (dist > d.Radius * d.Radius || dist >= best) continue;
                best = dist;
                label = d.Label;
                at = d.Local;
            }
            return label != null;
        }

        private static bool IsRevealed(in MinimapFrame f, in MinimapItem it)
        {
            switch (it.Reveal)
            {
                case MinimapReveal.Seen:
                    return !f.HasPlayer || (it.World - f.Player).sqrMagnitude <= f.SightRadius * f.SightRadius;
                case MinimapReveal.Explored:
                    return f.FogMap == null || f.FogMap.IsExplored(it.World);
                default:
                    return true;
            }
        }

        private void DrawEdgePin(in MinimapFrame f, Vector2 local, MinimapIcon icon, Color color, float size)
        {
            float bearing;
            Vector2 p = local;
            if (_circle) MinimapProjection.ClampToRim(ref p, Radius - EdgeInset, out bearing);
            else MinimapProjection.ClampToRect(ref p, HalfSize - new Vector2(EdgeInset, EdgeInset), out bearing);

            // Chevron on the rim pointing out, the icon just inside it. A pulse on the chevron
            // is the one moving thing a pin has — it says "that way", not "look at me".
            float pulse = 0.75f + 0.25f * Mathf.Sin(f.Time * 4f);
            var chev = color;
            chev.a *= pulse;
            Vector2 dir = local.sqrMagnitude > 1e-4f ? local.normalized : Vector2.up;
            Under.Add(p, size * 2.2f, MinimapIcon.Glow, new Color(color.r, color.g, color.b, 0.35f * pulse));
            Glyphs.Add(p + dir * (size * 0.35f), size * 0.9f, MinimapIcon.Chevron, chev, bearing);
            Glyphs.Add(p - dir * (size * 0.55f), size * 0.95f, icon, color);
        }

        private void DrawPlayer(in MinimapFrame f)
        {
            if (!f.HasPlayer) return;
            var s = f.Style;
            Vector2 local = WorldToLocal(f.Player);
            float size = s.playerSize * GlyphScale;
            if (!_circle)
            {
                var hs = HalfSize;
                if (Mathf.Abs(local.x) > hs.x || Mathf.Abs(local.y) > hs.y)
                {
                    DrawEdgePin(in f, local, MinimapIcon.Arrow, s.playerColor, s.edgePinSize * GlyphScale);
                    return;
                }
            }

            var glow = f.Spirit ? new Color(0.55f, 0.8f, 1f, 0.55f) : new Color(1f, 0.92f, 0.7f, 0.42f);
            Under.Add(local, size * 2.3f, MinimapIcon.Glow, glow);
            float rot = MinimapProjection.HeadingRotation(f.Facing);
            var col = f.Spirit ? new Color(0.72f, 0.9f, 1f, 1f) : s.playerColor;
            Glyphs.Add(local, size, MinimapIcon.Arrow, col, rot);
        }

        /// <summary>
        /// The rectangle the game camera is showing, as a faint frame — the one thing a map
        /// can say about scale that no number can. Skipped when it would not fit the dial.
        /// </summary>
        private void DrawCameraFrame(in MinimapFrame f)
        {
            if (f.CameraHalfExtent.x <= 0f) return;
            Vector2 c = WorldToLocal(f.CameraCentre);
            float upw = UnitsPerWorld;
            Vector2 h = f.CameraHalfExtent * upw;
            if (_circle)
            {
                float corner = (c + h).magnitude;
                corner = Mathf.Max(Mathf.Max(corner, (c - h).magnitude),
                                   Mathf.Max((c + new Vector2(h.x, -h.y)).magnitude, (c + new Vector2(-h.x, h.y)).magnitude));
                if (corner > Radius - 4f) return;
            }
            var col = new Color(1f, 0.95f, 0.8f, _circle ? 0.16f : 0.22f);
            float t = 1.2f;
            Under.Add(c + new Vector2(0f, h.y), new Vector2(h.x * 2f, t), MinimapIcon.Bar, col);
            Under.Add(c - new Vector2(0f, h.y), new Vector2(h.x * 2f, t), MinimapIcon.Bar, col);
            Under.Add(c + new Vector2(h.x, 0f), new Vector2(t, h.y * 2f), MinimapIcon.Bar, col);
            Under.Add(c - new Vector2(h.x, 0f), new Vector2(t, h.y * 2f), MinimapIcon.Bar, col);
        }
    }
}
