using UnityEngine;

namespace Valkur.UI.Frontend
{
    /// <summary>
    /// Each glyph's accent colour and the way its particles move.
    ///
    /// <para><b>The colour is identity, never state.</b> Whether an editor is open, hovered or
    /// selected is said by the socket around the icon (glow, brackets, a lit gem), the same for
    /// all of them — the class selector learned that a per-class selection colour is a state
    /// nobody can read. The accent only answers "which one is this" at a glance, and the shape
    /// answers it without colour.</para>
    ///
    /// <para>Hues are spread so neighbours in the launcher's grid do not collapse into one
    /// another, and kept saturated but not neon: on the additive motes a colour that is already
    /// pale reads as white.</para>
    /// </summary>
    public static class FrontendIconTheme
    {
        private static readonly Color Grass = new Color(0.42f, 0.78f, 0.38f, 1f);
        private static readonly Color Brick = new Color(0.86f, 0.46f, 0.30f, 1f);
        private static readonly Color Leather = new Color(0.70f, 0.40f, 0.24f, 1f);
        private static readonly Color Fire = new Color(1.00f, 0.52f, 0.16f, 1f);
        private static readonly Color Teal = new Color(0.34f, 0.78f, 0.74f, 1f);
        private static readonly Color Crimson = new Color(0.90f, 0.24f, 0.30f, 1f);
        private static readonly Color Violet = new Color(0.66f, 0.50f, 0.96f, 1f);
        private static readonly Color Sea = new Color(0.40f, 0.68f, 0.92f, 1f);
        private static readonly Color Magenta = new Color(0.95f, 0.45f, 0.85f, 1f);
        private static readonly Color Arcane = new Color(0.60f, 0.36f, 0.96f, 1f);
        private static readonly Color Lamp = new Color(1.00f, 0.84f, 0.40f, 1f);
        private static readonly Color Sun = new Color(1.00f, 0.72f, 0.24f, 1f);
        private static readonly Color Lens = new Color(0.38f, 0.80f, 0.95f, 1f);
        private static readonly Color Amber = new Color(0.92f, 0.64f, 0.28f, 1f);
        private static readonly Color Slate = new Color(0.50f, 0.60f, 0.86f, 1f);
        private static readonly Color Leaf = new Color(0.38f, 0.86f, 0.54f, 1f);
        private static readonly Color Coin = new Color(1.00f, 0.78f, 0.28f, 1f);
        private static readonly Color Blood = new Color(0.95f, 0.20f, 0.20f, 1f);
        private static readonly Color Quest = new Color(0.96f, 0.55f, 0.20f, 1f);
        private static readonly Color Globe = new Color(0.28f, 0.64f, 0.90f, 1f);
        private static readonly Color Cursor = new Color(0.40f, 0.86f, 1.00f, 1f);
        private static readonly Color Moss = new Color(0.55f, 0.80f, 0.48f, 1f);
        private static readonly Color Target = new Color(0.95f, 0.32f, 0.26f, 1f);
        private static readonly Color Scope = new Color(0.45f, 0.90f, 0.60f, 1f);
        private static readonly Color Ink = new Color(0.60f, 0.70f, 0.96f, 1f);
        private static readonly Color Silver = new Color(0.78f, 0.80f, 0.86f, 1f);
        private static readonly Color Disk = new Color(0.36f, 0.55f, 0.92f, 1f);
        private static readonly Color Folder = new Color(0.95f, 0.70f, 0.30f, 1f);
        private static readonly Color Door = new Color(0.95f, 0.40f, 0.30f, 1f);

        /// <summary>A cold, SATURATED blue-white for falling flakes. Not grey: grey on additive is a smudge.</summary>
        public static readonly Color Frost = new Color(0.62f, 0.84f, 1.00f, 1f);

        public static Color AccentOf(FrontendGlyph glyph)
        {
            switch (glyph)
            {
                case FrontendGlyph.Tile: return Grass;
                case FrontendGlyph.Buildings: return Brick;
                case FrontendGlyph.Items: return Leather;
                case FrontendGlyph.Spells: return Fire;
                case FrontendGlyph.Entities: return Teal;
                case FrontendGlyph.Boss: return Crimson;
                case FrontendGlyph.Fsm: return Violet;
                case FrontendGlyph.Map: return Sea;
                case FrontendGlyph.Inventory: return Leather;
                case FrontendGlyph.Particles: return Magenta;
                case FrontendGlyph.Spawners: return Arcane;
                case FrontendGlyph.Lighting: return Lamp;
                case FrontendGlyph.Weather: return Sun;
                case FrontendGlyph.Camera: return Lens;
                case FrontendGlyph.Controls: return Amber;
                case FrontendGlyph.NodeGraph: return Slate;
                case FrontendGlyph.Skills: return Leaf;
                case FrontendGlyph.Economy: return Coin;
                case FrontendGlyph.Death: return Blood;
                case FrontendGlyph.Quests: return Quest;
                case FrontendGlyph.SeedWorld: return Globe;
                case FrontendGlyph.Selection: return Cursor;
                case FrontendGlyph.Backups: return Moss;
                case FrontendGlyph.CombatRanges: return Target;
                case FrontendGlyph.DebugHud: return Scope;
                case FrontendGlyph.SaveLog: return Ink;
                case FrontendGlyph.Pause: return Silver;
                case FrontendGlyph.SaveGame: return Disk;
                case FrontendGlyph.LoadGame: return Folder;
                case FrontendGlyph.Options: return Silver;
                case FrontendGlyph.Exit: return Door;
                default: return FrontendPalette.GoldLight;
            }
        }

        public static FrontendMoteStyle MotesOf(FrontendGlyph glyph)
        {
            switch (glyph)
            {
                case FrontendGlyph.Spells:
                case FrontendGlyph.Death:
                case FrontendGlyph.Boss:
                case FrontendGlyph.CombatRanges:
                case FrontendGlyph.Exit:
                    return FrontendMoteStyle.Embers;
                case FrontendGlyph.Weather:
                    return FrontendMoteStyle.Snow;
                case FrontendGlyph.Tile:
                case FrontendGlyph.Buildings:
                case FrontendGlyph.Map:
                case FrontendGlyph.NodeGraph:
                case FrontendGlyph.SeedWorld:
                    return FrontendMoteStyle.Dust;
                case FrontendGlyph.Particles:
                case FrontendGlyph.Spawners:
                case FrontendGlyph.Fsm:
                case FrontendGlyph.Camera:
                    return FrontendMoteStyle.Orbit;
                case FrontendGlyph.Lighting:
                case FrontendGlyph.Economy:
                case FrontendGlyph.Skills:
                case FrontendGlyph.Items:
                case FrontendGlyph.Selection:
                case FrontendGlyph.Quests:
                    return FrontendMoteStyle.Glints;
                default:
                    return FrontendMoteStyle.Sparks;
            }
        }
    }
}
