using System.Collections.Generic;
using UnityEngine;
using Valkur.Data.Dungeon.Udemy;

namespace Valkur.Gameplay.World.Dungeon.Udemy.Builder
{
    /// <summary>
    /// Internal POCO carrying the in-flight state of a room while the builder
    /// places it. After successful generation, public consumers receive a
    /// <see cref="RoomData"/> snapshot via <c>GameEvents.OnRoomChanged</c>;
    /// <see cref="Room"/> stays inside the builder + runtime layers.
    ///
    /// Field naming intentionally mirrors Udemy's <c>Room</c> class for easy
    /// cross-reference with the original DungeonGunnerCourse code.
    /// </summary>
    public sealed class Room
    {
        public string id;
        public string templateID;
        public GameObject prefab;
        public string battleMusicId;
        public string ambientMusicId;
        public RoomNodeTypeSO roomNodeType;

        // World-space tile bounds (inclusive). Filled by the builder.
        public Vector2Int lowerBounds;
        public Vector2Int upperBounds;

        // Original template-local bounds. Used to derive the offset when
        // instantiating the prefab and when computing doorway positions.
        public Vector2Int templateLowerBounds;
        public Vector2Int templateUpperBounds;

        public Vector2Int[] spawnPositionArray = System.Array.Empty<Vector2Int>();
        public List<SpawnableEnemyByLevel> enemiesByLevelList = new List<SpawnableEnemyByLevel>();
        public List<RoomEnemySpawnParameters> roomLevelEnemySpawnParametersList
            = new List<RoomEnemySpawnParameters>();

        public List<string> childRoomIDList = new List<string>();
        public string parentRoomID = string.Empty;
        public List<Doorway> doorWayList = new List<Doorway>();

        public bool isPositioned;
        public bool isClearedOfEnemies;
        public bool isPreviouslyVisited;

        // Phase 4 will populate this with the actual InstantiatedRoom MonoBehaviour.
        // Kept as MonoBehaviour to avoid coupling the builder to any specific component.
        public MonoBehaviour instantiatedRoom;

        /// <summary>
        /// Translate this room's mutable state into a public read-only snapshot
        /// suitable for <c>GameEvents.OnRoomChanged</c> subscribers.
        /// </summary>
        public RoomData ToSnapshot()
        {
            int width = upperBounds.x - lowerBounds.x + 1;
            int height = upperBounds.y - lowerBounds.y + 1;
            var bounds = new RectInt(lowerBounds.x, lowerBounds.y, width, height);
            var entrance = new Vector2Int(
                (lowerBounds.x + upperBounds.x) / 2,
                (lowerBounds.y + upperBounds.y) / 2);

            return new RoomData(
                id: id,
                templateGuid: templateID,
                bounds: bounds,
                entrancePosition: entrance,
                isClearedOfEnemies: isClearedOfEnemies,
                isPreviouslyVisited: isPreviouslyVisited,
                roomNodeType: roomNodeType);
        }
    }
}
