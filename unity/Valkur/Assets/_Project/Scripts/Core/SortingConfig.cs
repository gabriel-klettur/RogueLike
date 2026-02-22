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
        public const string LAYER_OVERHEAD = "Overhead";
        public const string LAYER_UI_WORLD = "UI_World";

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
        /// Convert a world Y position to a sortingOrder offset.
        /// Lower Y (further up on screen) gets lower order (drawn first / behind).
        /// Multiplied by -100 to give enough granularity between entities.
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
