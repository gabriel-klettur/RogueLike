using System.Collections.Generic;

namespace Valkur.Gameplay.World.Dungeon.Udemy.Runtime
{
    /// <summary>
    /// Maps the conventional child-GameObject names found inside a room prefab
    /// to Valkur's global <see cref="TilemapLayerSetup.TilemapLayer"/> enum.
    /// Authors organize the prefab with one child Tilemap per layer, named
    /// either with the Valkur enum name (preferred) or with the legacy Udemy
    /// tag stripped of "Tilemap" (allowed for vendor prefabs).
    ///
    /// Unmapped prefab children are silently skipped — they don't break the
    /// stamp, but their tiles never make it into the world.
    /// </summary>
    public static class RoomTilemapLayerMapping
    {
        // Preferred names match Valkur's TilemapLayer enum exactly. Aliases keep
        // Udemy-style "ground"/"front"/"decoration1" prefabs importable as-is,
        // and the "TilemapN_*" series matches the DungeonGunner Catacombs/
        // Sorcery prefab convention.
        private static readonly Dictionary<string, TilemapLayerSetup.TilemapLayer> NameToLayer
            = new Dictionary<string, TilemapLayerSetup.TilemapLayer>(System.StringComparer.OrdinalIgnoreCase)
            {
                { "Ground", TilemapLayerSetup.TilemapLayer.Ground },
                { "groundTilemap", TilemapLayerSetup.TilemapLayer.Ground },
                { "Tilemap1_Ground", TilemapLayerSetup.TilemapLayer.Ground },

                { "FloorDecals", TilemapLayerSetup.TilemapLayer.FloorDecals },
                { "Decoration1", TilemapLayerSetup.TilemapLayer.FloorDecals },
                { "decoration1Tilemap", TilemapLayerSetup.TilemapLayer.FloorDecals },
                { "Tilemap2_Decoration1", TilemapLayerSetup.TilemapLayer.FloorDecals },

                { "Decorations", TilemapLayerSetup.TilemapLayer.Decorations },
                { "Decoration2", TilemapLayerSetup.TilemapLayer.Decorations },
                { "decoration2Tilemap", TilemapLayerSetup.TilemapLayer.Decorations },
                { "Tilemap3_Decoration2", TilemapLayerSetup.TilemapLayer.Decorations },

                { "WallsTop", TilemapLayerSetup.TilemapLayer.WallsTop },
                { "Front", TilemapLayerSetup.TilemapLayer.WallsTop },
                { "frontTilemap", TilemapLayerSetup.TilemapLayer.WallsTop },
                { "Tilemap4_Front", TilemapLayerSetup.TilemapLayer.WallsTop },

                { "Collision", TilemapLayerSetup.TilemapLayer.Collision },
                { "collisionTilemap", TilemapLayerSetup.TilemapLayer.Collision },
                { "Tilemap5_Collision", TilemapLayerSetup.TilemapLayer.Collision },
            };

        /// <summary>True when the given child GameObject name resolves to a known layer.</summary>
        public static bool TryResolve(string childName, out TilemapLayerSetup.TilemapLayer layer)
            => NameToLayer.TryGetValue(childName, out layer);
    }
}
