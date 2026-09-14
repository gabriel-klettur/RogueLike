using UnityEngine;

namespace Valkur.UI.Frontend
{
    /// <summary>
    /// The icon library: each <see cref="FrontendGlyph"/> as a handful of parts in the unit box.
    ///
    /// <para><b>A silhouette first, a detail second.</b> At 46 px an icon is read by its outline
    /// before anything inside it, so each glyph is one recognisable shape (a house, a flame, a
    /// gear) with at most two inner details. Metal is the frame of a thing, the accent is what
    /// makes it THAT thing, and the accent colour comes from <see cref="FrontendIconTheme"/> so the
    /// shape still says what it is to a player who cannot tell the colours apart.</para>
    /// </summary>
    public static partial class FrontendGlyphs
    {
        private const FrontendGlyphTone M = FrontendGlyphTone.Metal;
        private const FrontendGlyphTone A = FrontendGlyphTone.Accent;
        private const FrontendGlyphTone H = FrontendGlyphTone.AccentHot;
        private const FrontendGlyphTone D = FrontendGlyphTone.Dark;
        private const FrontendGlyphTone L = FrontendGlyphTone.Light;

        private static Vector2 V(float x, float y) => new Vector2(x, y);

        /// <summary>Adds <paramref name="glyph"/>'s parts to <paramref name="p"/>. False for <see cref="FrontendGlyph.None"/>.</summary>
        public static bool Paint(FrontendGlyph glyph, FrontendGlyphPainter p)
        {
            switch (glyph)
            {
                case FrontendGlyph.Tile: Tile(p); return true;
                case FrontendGlyph.Buildings: Buildings(p); return true;
                case FrontendGlyph.Items: Items(p); return true;
                case FrontendGlyph.Spells: Spells(p); return true;
                case FrontendGlyph.Entities: Entities(p); return true;
                case FrontendGlyph.Boss: Boss(p); return true;
                case FrontendGlyph.Fsm: Fsm(p); return true;
                case FrontendGlyph.Map: Map(p); return true;
                case FrontendGlyph.Inventory: Inventory(p); return true;
                case FrontendGlyph.Particles: Particles(p); return true;
                case FrontendGlyph.Spawners: Spawners(p); return true;
                case FrontendGlyph.Lighting: Lighting(p); return true;
                case FrontendGlyph.Weather: Weather(p); return true;
                case FrontendGlyph.Camera: Camera(p); return true;
                case FrontendGlyph.Controls: Controls(p); return true;
                case FrontendGlyph.NodeGraph: NodeGraph(p); return true;
                case FrontendGlyph.Skills: Skills(p); return true;
                case FrontendGlyph.Economy: Economy(p); return true;
                case FrontendGlyph.Death: Death(p); return true;
                case FrontendGlyph.Quests: Quests(p); return true;
                case FrontendGlyph.SeedWorld: SeedWorld(p); return true;
                case FrontendGlyph.Selection: Selection(p); return true;
                case FrontendGlyph.Backups: Backups(p); return true;
                case FrontendGlyph.CombatRanges: CombatRanges(p); return true;
                case FrontendGlyph.DebugHud: DebugHud(p); return true;
                case FrontendGlyph.SaveLog: SaveLog(p); return true;
                case FrontendGlyph.Pause: Pause(p); return true;
                case FrontendGlyph.SaveGame: SaveGame(p); return true;
                case FrontendGlyph.LoadGame: LoadGame(p); return true;
                case FrontendGlyph.Options: Options(p); return true;
                case FrontendGlyph.Exit: Exit(p); return true;
                default: return false;
            }
        }

        // ── Authoring editors ────────────────────────────────────────────────

        private static void Tile(FrontendGlyphPainter p)
        {
            p.Rect(-0.8f, 0.06f, -0.06f, 0.8f, A);
            p.Rect(0.06f, 0.06f, 0.8f, 0.8f, M);
            p.Rect(-0.8f, -0.8f, -0.06f, -0.06f, M);
            p.Rect(0.06f, -0.8f, 0.8f, -0.06f, A);
        }

        private static void Buildings(FrontendGlyphPainter p)
        {
            p.Rect(-0.6f, -0.82f, 0.6f, 0.12f, M);
            p.Tri(V(-0.88f, 0.08f), V(0f, 0.86f), V(0.88f, 0.08f), A);
            p.Rect(-0.17f, -0.82f, 0.17f, -0.22f, D);
            p.Rect(0.26f, -0.34f, 0.46f, -0.1f, L);
        }

        private static void Items(FrontendGlyphPainter p)
        {
            p.Line(V(-0.45f, -0.45f), V(0.78f, 0.78f), 0.24f, L);
            p.Line(V(-0.62f, -0.12f), V(-0.12f, -0.62f), 0.17f, M);
            p.Line(V(-0.42f, -0.42f), V(-0.74f, -0.74f), 0.15f, A);
            p.Disc(-0.8f, -0.8f, 0.12f, M);
        }

        private static void Spells(FrontendGlyphPainter p)
        {
            // Three tongues over a round base, then a small hot heart low in the flame: a drop
            // shape with a pale core read as water on the first capture.
            p.Disc(0f, -0.4f, 0.48f, A);
            p.Tri(V(-0.48f, -0.34f), V(-0.42f, 0.42f), V(0.02f, -0.1f), A);
            p.Tri(V(-0.34f, -0.3f), V(0.1f, 0.94f), V(0.44f, -0.3f), A);
            p.Tri(V(0.1f, -0.2f), V(0.5f, 0.3f), V(0.48f, -0.36f), A);
            p.Disc(0f, -0.5f, 0.2f, H);
            p.Tri(V(-0.18f, -0.46f), V(0.04f, 0.12f), V(0.2f, -0.46f), H);
        }

        private static void Entities(FrontendGlyphPainter p)
        {
            p.Tri(V(-0.7f, -0.85f), V(0f, 0.2f), V(0.7f, -0.85f), A);
            p.Disc(0f, 0.46f, 0.3f, M);
        }

        private static void Boss(FrontendGlyphPainter p)
        {
            p.Tri(V(-0.72f, -0.3f), V(-0.58f, 0.58f), V(-0.28f, -0.3f), M);
            p.Tri(V(-0.3f, -0.3f), V(0f, 0.8f), V(0.3f, -0.3f), M);
            p.Tri(V(0.28f, -0.3f), V(0.58f, 0.58f), V(0.72f, -0.3f), M);
            p.Rect(-0.74f, -0.66f, 0.74f, -0.24f, M);
            p.Disc(0f, -0.45f, 0.13f, A);
            p.Disc(-0.47f, -0.45f, 0.09f, A);
            p.Disc(0.47f, -0.45f, 0.09f, A);
        }

        private static void Fsm(FrontendGlyphPainter p)
        {
            p.Line(V(-0.5f, 0.45f), V(0.5f, 0.45f), 0.11f, M);
            p.Line(V(0.5f, 0.45f), V(0f, -0.5f), 0.11f, M);
            p.Line(V(0f, -0.5f), V(-0.5f, 0.45f), 0.11f, M);
            p.Disc(-0.5f, 0.45f, 0.27f, A);
            p.Disc(0.5f, 0.45f, 0.27f, M);
            p.Disc(0f, -0.5f, 0.27f, H);
        }

        private static void Map(FrontendGlyphPainter p)
        {
            p.Rect(-0.82f, -0.62f, -0.27f, 0.72f, M);
            p.Rect(-0.27f, -0.72f, 0.27f, 0.62f, A);
            p.Rect(0.27f, -0.62f, 0.82f, 0.72f, M);
            p.Line(V(-0.14f, -0.2f), V(0.14f, 0.08f), 0.09f, D);
            p.Line(V(-0.14f, 0.08f), V(0.14f, -0.2f), 0.09f, D);
        }

        private static void Inventory(FrontendGlyphPainter p)
        {
            p.Rect(-0.78f, -0.72f, 0.78f, 0.1f, M);
            p.Rect(-0.78f, 0.1f, 0.78f, 0.58f, A);
            p.Rect(-0.78f, 0.02f, 0.78f, 0.16f, D);
            p.Rect(-0.13f, -0.2f, 0.13f, 0.22f, L);
        }

        private static void Particles(FrontendGlyphPainter p)
        {
            p.Diamond(0f, 0f, 0.24f, 0.9f, H);
            p.Diamond(0f, 0f, 0.9f, 0.24f, H);
            p.Diamond(0.62f, 0.62f, 0.13f, 0.13f, L);
            p.Diamond(-0.6f, -0.58f, 0.11f, 0.11f, A);
            p.Disc(-0.58f, 0.62f, 0.09f, L);
        }

        private static void Spawners(FrontendGlyphPainter p)
        {
            p.Ring(0f, 0f, 0.62f, 0.24f, A);
            p.Disc(0f, 0f, 0.26f, H);
            p.Diamond(0f, 0.9f, 0.1f, 0.1f, M);
            p.Diamond(0.9f, 0f, 0.1f, 0.1f, M);
            p.Diamond(-0.9f, 0f, 0.1f, 0.1f, M);
        }

        private static void Lighting(FrontendGlyphPainter p)
        {
            for (int i = 0; i < 5; i++)
            {
                float a = (30f + i * 30f) * Mathf.Deg2Rad;
                var d = V(Mathf.Cos(a), Mathf.Sin(a));
                p.Line(V(0f, 0.18f) + d * 0.66f, V(0f, 0.18f) + d * 0.88f, 0.1f, A);
            }
            p.Rect(-0.22f, -0.72f, 0.22f, -0.26f, M);
            p.Disc(0f, 0.18f, 0.48f, H);
        }

        private static void Weather(FrontendGlyphPainter p)
        {
            p.Disc(0.32f, 0.36f, 0.42f, A);
            p.Disc(-0.32f, -0.22f, 0.36f, L);
            p.Disc(0.16f, -0.16f, 0.42f, L);
            p.Rect(-0.68f, -0.6f, 0.58f, -0.26f, L);
        }

        private static void Camera(FrontendGlyphPainter p)
        {
            p.Rect(-0.36f, 0.38f, 0.06f, 0.6f, M);
            p.Rect(-0.82f, -0.52f, 0.82f, 0.42f, M);
            p.Disc(0f, -0.05f, 0.38f, D);
            p.Disc(0f, -0.05f, 0.24f, A);
            p.Disc(-0.08f, 0.04f, 0.07f, L);
        }

        private static void Controls(FrontendGlyphPainter p)
        {
            p.Rect(-0.2f, -0.78f, 0.2f, 0.78f, M);
            p.Rect(-0.78f, -0.2f, 0.78f, 0.2f, M);
            p.Tri(V(-0.1f, 0.5f), V(0f, 0.66f), V(0.1f, 0.5f), D);
            p.Tri(V(-0.1f, -0.5f), V(0f, -0.66f), V(0.1f, -0.5f), D);
            p.Disc(0f, 0f, 0.14f, A);
        }

        private static void NodeGraph(FrontendGlyphPainter p)
        {
            p.Line(V(-0.5f, 0.5f), V(0.5f, 0.5f), 0.14f, D);
            p.Line(V(0.5f, 0.5f), V(0f, -0.5f), 0.14f, D);
            p.Rect(-0.82f, 0.2f, -0.2f, 0.82f, M);
            p.Rect(0.2f, 0.2f, 0.82f, 0.82f, A);
            p.Rect(-0.31f, -0.82f, 0.31f, -0.2f, M);
        }

        private static void Skills(FrontendGlyphPainter p)
        {
            p.Line(V(0f, -0.58f), V(-0.52f, 0.42f), 0.1f, M);
            p.Line(V(0f, -0.58f), V(0.52f, 0.42f), 0.1f, M);
            p.Diamond(0f, -0.58f, 0.3f, 0.3f, H);
            p.Diamond(-0.52f, 0.42f, 0.27f, 0.27f, A);
            p.Diamond(0.52f, 0.42f, 0.27f, 0.27f, A);
        }

        private static void Economy(FrontendGlyphPainter p)
        {
            p.Rect(-0.72f, -0.8f, 0.42f, -0.54f, M);
            p.Rect(-0.66f, -0.5f, 0.36f, -0.24f, M);
            p.Disc(0.24f, 0.3f, 0.5f, M);
            p.Disc(0.24f, 0.3f, 0.31f, A);
            p.Rect(0.18f, 0.12f, 0.3f, 0.48f, L);
        }

        private static void Death(FrontendGlyphPainter p)
        {
            p.Rect(-0.36f, -0.78f, 0.36f, -0.28f, L);
            p.Disc(0f, 0.16f, 0.62f, L);
            p.Disc(-0.26f, 0.08f, 0.17f, D);
            p.Disc(0.26f, 0.08f, 0.17f, D);
            p.Disc(-0.26f, 0.08f, 0.07f, H);
            p.Disc(0.26f, 0.08f, 0.07f, H);
            p.Tri(V(-0.08f, -0.24f), V(0f, -0.08f), V(0.08f, -0.24f), D);
        }

        private static void Quests(FrontendGlyphPainter p)
        {
            p.Rect(-0.56f, -0.66f, 0.56f, 0.66f, L);
            p.Rect(-0.72f, 0.54f, 0.72f, 0.82f, M);
            p.Rect(-0.72f, -0.82f, 0.72f, -0.54f, M);
            p.Rect(-0.09f, -0.06f, 0.09f, 0.4f, A);
            p.Disc(0f, -0.3f, 0.1f, A);
        }

        private static void SeedWorld(FrontendGlyphPainter p)
        {
            p.Line(V(0f, 0.3f), V(0f, 0.82f), 0.09f, M);
            p.Diamond(0.22f, 0.7f, 0.22f, 0.1f, H);
            p.Diamond(-0.2f, 0.6f, 0.19f, 0.09f, H);
            p.Disc(0f, -0.22f, 0.6f, A);
            p.Disc(-0.2f, -0.08f, 0.22f, H);
            p.Disc(0.25f, -0.42f, 0.16f, H);
        }

        // ── Tools ─────────────────────────────────────────────────────────────

        private static void Selection(FrontendGlyphPainter p)
        {
            foreach (var c in new[] { V(-0.8f, 0.8f), V(0.8f, 0.8f), V(-0.8f, -0.8f), V(0.8f, -0.8f) })
            {
                p.Rect(Mathf.Min(c.x, c.x - Mathf.Sign(c.x) * 0.36f), c.y - 0.06f, Mathf.Max(c.x, c.x - Mathf.Sign(c.x) * 0.36f), c.y + 0.06f, A);
                p.Rect(c.x - 0.06f, Mathf.Min(c.y, c.y - Mathf.Sign(c.y) * 0.36f), c.x + 0.06f, Mathf.Max(c.y, c.y - Mathf.Sign(c.y) * 0.36f), A);
            }
            p.Line(V(0.08f, -0.18f), V(0.36f, -0.62f), 0.15f, L);
            p.Tri(V(-0.24f, 0.52f), V(-0.24f, -0.46f), V(0.46f, -0.04f), L);
        }

        private static void Backups(FrontendGlyphPainter p)
        {
            p.Rect(-0.42f, -0.52f, 0.68f, 0.7f, M);
            p.Rect(-0.62f, -0.72f, 0.48f, 0.5f, L);
            p.Ring(0.42f, -0.44f, 0.3f, 0.12f, A);
            p.Line(V(0.42f, -0.44f), V(0.42f, -0.26f), 0.08f, A);
            p.Line(V(0.42f, -0.44f), V(0.56f, -0.44f), 0.08f, A);
        }

        private static void CombatRanges(FrontendGlyphPainter p)
        {
            p.Ring(0f, 0f, 0.74f, 0.13f, M);
            p.Ring(0f, 0f, 0.44f, 0.13f, A);
            p.Disc(0f, 0f, 0.16f, H);
        }

        private static void DebugHud(FrontendGlyphPainter p)
        {
            p.Rect(-0.86f, -0.86f, 0.86f, -0.72f, M);
            p.Rect(-0.7f, -0.72f, -0.34f, -0.12f, A);
            p.Rect(-0.18f, -0.72f, 0.18f, 0.34f, H);
            p.Rect(0.34f, -0.72f, 0.7f, 0.72f, A);
        }

        private static void SaveLog(FrontendGlyphPainter p)
        {
            p.Rect(-0.6f, -0.8f, 0.6f, 0.8f, L);
            p.Rect(-0.4f, 0.36f, 0.4f, 0.48f, D);
            p.Rect(-0.4f, 0.06f, 0.3f, 0.18f, D);
            p.Rect(-0.4f, -0.24f, 0.4f, -0.12f, D);
            p.Rect(-0.4f, -0.54f, 0.1f, -0.42f, D);
            p.Disc(0.34f, -0.52f, 0.13f, A);
        }

        // ── Session ───────────────────────────────────────────────────────────

        private static void Pause(FrontendGlyphPainter p)
        {
            p.Ring(0f, 0f, 0.82f, 0.1f, A);
            p.Rect(-0.42f, -0.5f, -0.1f, 0.5f, M);
            p.Rect(0.1f, -0.5f, 0.42f, 0.5f, M);
        }

        private static void SaveGame(FrontendGlyphPainter p)
        {
            p.Rect(-0.72f, -0.76f, 0.72f, 0.76f, A);
            p.Rect(-0.46f, -0.7f, 0.46f, -0.06f, L);
            p.Rect(-0.36f, 0.3f, 0.36f, 0.76f, M);
            p.Rect(0.1f, 0.4f, 0.24f, 0.68f, D);
        }

        private static void LoadGame(FrontendGlyphPainter p)
        {
            p.Rect(-0.82f, 0.42f, -0.2f, 0.62f, M);
            p.Rect(-0.82f, -0.66f, 0.82f, 0.46f, M);
            p.Rect(-0.82f, -0.66f, 0.82f, 0.18f, A);
            p.Rect(-0.08f, -0.52f, 0.08f, -0.12f, L);
            p.Tri(V(-0.26f, -0.14f), V(0f, 0.26f), V(0.26f, -0.14f), L);
        }

        private static void Options(FrontendGlyphPainter p)
        {
            for (int i = 0; i < 8; i++)
            {
                float a = i * 45f * Mathf.Deg2Rad;
                var d = V(Mathf.Cos(a), Mathf.Sin(a));
                p.Line(d * 0.4f, d * 0.82f, 0.24f, M);
            }
            p.Disc(0f, 0f, 0.56f, M);
            p.Disc(0f, 0f, 0.22f, D);
            p.Disc(0f, 0f, 0.1f, A);
        }

        private static void Exit(FrontendGlyphPainter p)
        {
            p.Rect(-0.72f, -0.82f, 0.1f, 0.82f, M);
            p.Rect(-0.57f, -0.66f, -0.05f, 0.66f, D);
            p.Line(V(-0.22f, 0f), V(0.5f, 0f), 0.17f, H);
            p.Tri(V(0.42f, 0.3f), V(0.88f, 0f), V(0.42f, -0.3f), H);
        }
    }
}
