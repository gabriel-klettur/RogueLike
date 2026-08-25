using UnityEngine;

namespace Valkur.Gameplay.TileEditor
{
    /// <summary>
    /// Minimal surface shared by <see cref="TerrainMap"/>, <see cref="CollisionTagMap"/>
    /// and <see cref="World.Layering.LayerJumpMap"/>. A single method is enough — passing
    /// null/empty already clears the cell in all three implementations.
    /// </summary>
    public interface ITileMetadataMap
    {
        void Set(Vector3Int cell, string value);
    }

    /// <summary>
    /// Before/after of ONE cell in a parallel metadata map (terrain, collision tag, or
    /// layer-jump target) that must be reverted in the same <see cref="TileEditBatch"/>
    /// as the visual <see cref="TileEdit"/>s of the same stroke. Exact mirror of
    /// <see cref="TileEdit"/> (Position + OldValue/NewValue + Target) — the same optional
    /// Target field <see cref="TileEdit.TargetTilemap"/> already uses, generalized from
    /// "a specific Tilemap" to "any metadata sink".
    /// </summary>
    public struct MetadataEdit
    {
        public Vector3Int Position;
        public string OldValue;
        public string NewValue;
        public ITileMetadataMap Target;

        public MetadataEdit(Vector3Int position, string oldValue, string newValue, ITileMetadataMap target)
        {
            Position = position;
            OldValue = oldValue;
            NewValue = newValue;
            Target = target;
        }
    }
}
