using Valkur.UI.Frontend;

namespace Valkur.Gameplay.Editors.General
{
    /// <summary>
    /// Which icon each launcher entry wears.
    ///
    /// <para><b>Keyed by the entry's LABEL, in one table</b>, rather than a field on
    /// <see cref="GeneralEditorEntry"/>: the registry builds entries in three different helpers
    /// and a glyph argument threaded through all of them is the shape where one site forgets it
    /// and an editor ships with a blank socket. A label with no row here answers
    /// <see cref="FrontendGlyph.None"/>, and <c>GeneralEditorIconTests</c> walks the live
    /// registry so that is a red test rather than an empty square on screen.</para>
    /// </summary>
    public static class GeneralEditorIcons
    {
        public static FrontendGlyph GlyphFor(string label)
        {
            switch (label)
            {
                case "Tile": return FrontendGlyph.Tile;
                case "Buildings": return FrontendGlyph.Buildings;
                case "Items": return FrontendGlyph.Items;
                case "Spells": return FrontendGlyph.Spells;
                case "Entities": return FrontendGlyph.Entities;
                case "Boss": return FrontendGlyph.Boss;
                case "FSM": return FrontendGlyph.Fsm;
                case "Map": return FrontendGlyph.Map;
                case "Inventory": return FrontendGlyph.Inventory;
                case "Particles": return FrontendGlyph.Particles;
                case "Spawners": return FrontendGlyph.Spawners;
                case "Lighting": return FrontendGlyph.Lighting;
                case "Time & Weather": return FrontendGlyph.Weather;
                case "Camera": return FrontendGlyph.Camera;
                case "Controls": return FrontendGlyph.Controls;
                case "Dungeon NodeGraph": return FrontendGlyph.NodeGraph;
                case "Skills": return FrontendGlyph.Skills;
                case "Economy": return FrontendGlyph.Economy;
                case "Muerte": return FrontendGlyph.Death;
                case "Misiones": return FrontendGlyph.Quests;
                case "Seed World": return FrontendGlyph.SeedWorld;
                case "Seleccion": return FrontendGlyph.Selection;
                case "Map Backups": return FrontendGlyph.Backups;
                case "Combat Ranges": return FrontendGlyph.CombatRanges;
                case "Debug HUD": return FrontendGlyph.DebugHud;
                case "Save Log": return FrontendGlyph.SaveLog;
                case "Pause Menu": return FrontendGlyph.Pause;
                case "Save Game": return FrontendGlyph.SaveGame;
                case "Load": return FrontendGlyph.LoadGame;
                case "Options": return FrontendGlyph.Options;
                case "Exit to Menu": return FrontendGlyph.Exit;
                default: return FrontendGlyph.None;
            }
        }
    }
}
