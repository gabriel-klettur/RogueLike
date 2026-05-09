using UnityEngine;

namespace Valkur.Data.Dungeon.Udemy
{
    /// <summary>
    /// One opening in a room template. The dungeon builder pairs doorways
    /// with opposite orientations to chain rooms together. When a doorway
    /// ends up unconnected after generation, <see cref="doorwayCopyTileWidth"/>
    /// / <see cref="doorwayCopyTileHeight"/> describe a small tile rectangle
    /// that gets stamped over the gap to seal the room visually.
    /// </summary>
    [System.Serializable]
    public class Doorway
    {
        [Tooltip("Local tilemap position of the doorway center, relative to the room template origin.")]
        public Vector2Int position;

        [Tooltip("Compass orientation of this doorway.")]
        public Orientation orientation = Orientation.None;

        [Tooltip("Optional door prefab spawned when this doorway is connected (animated open/close).")]
        public GameObject doorPrefab;

        [Tooltip("Upper-left start position of the tile rectangle copied to seal an unconnected doorway.")]
        public Vector2Int doorwayStartCopyPosition;

        [Tooltip("Width (tiles) of the rectangle copied to seal an unconnected doorway.")]
        public int doorwayCopyTileWidth;

        [Tooltip("Height (tiles) of the rectangle copied to seal an unconnected doorway.")]
        public int doorwayCopyTileHeight;

        [HideInInspector] public bool isConnected;
        [HideInInspector] public bool isUnavailable;
    }
}
