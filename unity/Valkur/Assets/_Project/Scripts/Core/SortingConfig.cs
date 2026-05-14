namespace Valkur.Core
{
    /// <summary>
    /// Central sorting layer and Z-layer configuration.
    /// Maps to Python's config_z_layer.py Z_LAYERS and map/model/layer.py Layer enum.
    /// 
    /// Unity sorting strategy:
    /// - SortingLayers define broad render groups (Background, Ground, Entities, Overhead, UI).
    /// - Within each SortingLayer, sortingOrder provides fine-grained control.
    /// - For entities on the same layer, Y-position determines draw order (lower Y = behind).
    /// </summary>
    public static class SortingConfig
    {
        // --- Sorting Layer Names (must match Tags & Layers > Sorting Layers in Unity) ---
        public const string LAYER_BACKGROUND = "Background";
        public const string LAYER_GROUND = "Ground";
        public const string LAYER_FLOOR_DECALS = "FloorDecals";
        public const string LAYER_OBJECTS_LOW = "ObjectsLow";
        public const string LAYER_WALLS_BOTTOM = "WallsBottom";
        public const string LAYER_ENTITIES = "Entities";
        public const string LAYER_DECORATIONS = "Decorations";
        public const string LAYER_WALLS_TOP = "WallsTop";
        public const string LAYER_OBJECTS_HIGH = "ObjectsHigh";
        public const string LAYER_PROJECTILES = "Projectiles";
        public const string LAYER_VFX = "VFX";
        public const string LAYER_OVERHEAD = "Overhead";
        /// <summary>
        /// Entities (Player, NPCs) whose <see cref="Gameplay.World.Layering.VisualLayerOccupant.CurrentVisualLayer"/>
        /// equals 8 (OverheadDetails) render here so they appear in front of every
        /// painted tilemap layer. Sits strictly between <see cref="LAYER_OVERHEAD"/>
        /// and <see cref="LAYER_UI_WORLD"/> so in-world UI (health bars, mana bars)
        /// still draws above the elevated player sprite.
        /// </summary>
        public const string LAYER_ENTITIES_OVERHEAD = "EntitiesOverhead";
        public const string LAYER_UI_WORLD = "UI_World";
        public const string LAYER_OVERLAY = "Overlay";

        // --- Entity Z-Layer base orders (within Entities sorting layer) ---
        // Maps to Python's Z_LAYERS dict. Used as base sortingOrder before Y-offset.
        public const int Z_BACKGROUND = 0;
        public const int Z_GROUND = 100;
        public const int Z_LOW_OBJECT = 200;
        public const int Z_BUILDING_LOW = 300;
        public const int Z_ENTITY = 400;
        public const int Z_BUILDING_HIGH = 500;
        public const int Z_SKY = 600;
        public const int Z_UI = 1000;

        /// <summary>
        /// Multiplier applied to user-authored Z tier offsets (BuildingObject's
        /// <c>ZBottomOffset</c> / <c>ZTopOffset</c>). The Y-sort formula
        /// <c>YToSortingOrder</c> contributes ±100 of sortingOrder per world
        /// unit of vertical distance, so a naive <c>zOffset + ySortOrder</c>
        /// addition lets even a 0.1-unit Y diff outrank a +8 Z tier — the
        /// authored value was effectively ignored.
        ///
        /// **Why 2000 specifically:** Unity's <c>SpriteRenderer.sortingOrder</c>
        /// is internally truncated to a 16-bit short (range ±32767) for the
        /// sort comparison. With Z_TIER_SCALE = 2000:
        ///   • Max practical Z (±10 typical, ±15 extreme) stays in
        ///     [−30000, +30000] — fits inside the short window.
        ///   • A single Z tier dominates any Y diff &lt; 20 world units; +10 Z
        ///     dominates Y diff &lt; 200 units. Both far exceed the typical
        ///     "two adjacent buildings in the same zone" use case.
        ///   • Headroom for Y-sort within a zone: ±127 world units before the
        ///     packed value overflows short — far above the 50-unit zone span.
        ///
        /// A larger constant (we tried 100000 first) overflowed and wrapped
        /// to garbage values like -27880, surfacing as Z+8 buildings rendering
        /// BEHIND Z=0 buildings even after the fix.
        /// </summary>
        public const int Z_TIER_SCALE = 2000;

        /// <summary>
        /// Convert a world Y position to a sortingOrder offset.
        /// Higher worldY (further up on screen / "deeper" in 2D top-down)
        /// gets a more-negative order so it draws BEHIND entities at lower
        /// worldY. Multiplied by -100 to give enough granularity between
        /// entities.
        /// </summary>
        public static int YToSortingOrder(float worldY)
        {
            return -(int)(worldY * 100f);
        }

        /// <summary>
        /// Compute final sortingOrder for an entity given its Z-layer base and Y position.
        /// </summary>
        public static int ComputeSortingOrder(int zLayerBase, float worldY)
        {
            return zLayerBase + YToSortingOrder(worldY);
        }
    }
}
