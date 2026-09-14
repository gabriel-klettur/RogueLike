namespace Valkur.UI.Frontend
{
    /// <summary>
    /// Every icon the frontend kit can draw. Append only: a glyph is chosen by value by callers
    /// that keep their own table (the General Editor's launcher), and reordering would give every
    /// entry the neighbour's picture.
    /// </summary>
    public enum FrontendGlyph
    {
        None = 0,

        // Authoring editors
        Tile,
        Buildings,
        Items,
        Spells,
        Entities,
        Boss,
        Fsm,
        Map,
        Inventory,
        Particles,
        Spawners,
        Lighting,
        Weather,
        Camera,
        Controls,
        NodeGraph,
        Skills,
        Economy,
        Death,
        Quests,
        SeedWorld,

        // Tools
        Selection,
        Backups,
        CombatRanges,
        DebugHud,
        SaveLog,

        // Session
        Pause,
        SaveGame,
        LoadGame,
        Options,
        Exit,
    }

    /// <summary>How an icon's particles move when it answers an event. Chosen to say what the thing IS.</summary>
    public enum FrontendMoteStyle
    {
        /// <summary>Gold sparks thrown up and falling back. The default.</summary>
        Sparks = 0,

        /// <summary>Embers rising and slowing — fire, death, danger.</summary>
        Embers = 1,

        /// <summary>Soft flakes drifting down — weather.</summary>
        Snow = 2,

        /// <summary>Earth dust puffed sideways and settling — ground, world, rooms.</summary>
        Dust = 3,

        /// <summary>A ring of motes blown straight outward — portals, targets, emitters.</summary>
        Orbit = 4,

        /// <summary>A few bright glints that twinkle and fade in place — light, coin, knowledge.</summary>
        Glints = 5,
    }
}
