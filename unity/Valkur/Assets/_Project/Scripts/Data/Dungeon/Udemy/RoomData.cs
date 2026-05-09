using UnityEngine;

namespace Valkur.Data.Dungeon.Udemy
{
    /// <summary>
    /// Read-only snapshot of a positioned dungeon room, published over
    /// <c>GameEvents.OnRoomChanged</c> / <c>GameEvents.OnRoomEnemiesDefeated</c>.
    /// Subscribers (audio, minimap, spawners) get this lightweight POCO instead
    /// of the internal <c>Room</c> POCO so the builder layer can mutate freely.
    /// </summary>
    public sealed class RoomData
    {
        public string Id { get; }
        public string TemplateGuid { get; }
        public RectInt Bounds { get; }
        public Vector2Int EntrancePosition { get; }
        public bool IsClearedOfEnemies { get; }
        public bool IsPreviouslyVisited { get; }
        public RoomNodeTypeSO RoomNodeType { get; }

        public RoomData(
            string id,
            string templateGuid,
            RectInt bounds,
            Vector2Int entrancePosition,
            bool isClearedOfEnemies,
            bool isPreviouslyVisited,
            RoomNodeTypeSO roomNodeType)
        {
            Id = id;
            TemplateGuid = templateGuid;
            Bounds = bounds;
            EntrancePosition = entrancePosition;
            IsClearedOfEnemies = isClearedOfEnemies;
            IsPreviouslyVisited = isPreviouslyVisited;
            RoomNodeType = roomNodeType;
        }
    }
}
