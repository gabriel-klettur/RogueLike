using System.Collections.Generic;
using Valkur.Gameplay.World.Dungeon.Udemy.Builder;

namespace Valkur.Gameplay.World.Dungeon.Udemy.Spawning
{
    /// <summary>
    /// Process-wide lookup of in-flight <see cref="Room"/> POCOs by their
    /// node id. <c>GameEvents.OnRoomChanged</c> fires with primitive payload
    /// (roomId + bounds), so subscribers that need the full Room (spawner,
    /// audio swap, minimap) come back here to retrieve it.
    ///
    /// Lifecycle: <c>UdemyDungeonStrategy</c> calls <see cref="Register"/>
    /// for every room it generates, and <see cref="Clear"/> on cleanup.
    /// Static state is reset on Play Mode enter via the
    /// <c>SubsystemRegistration</c> hook so Domain-Reload-OFF doesn't carry
    /// stale entries between sessions.
    /// </summary>
    public static class RoomRegistry
    {
        private static readonly Dictionary<string, Room> _byId = new Dictionary<string, Room>();

        public static int Count => _byId.Count;

        public static void Register(Room room)
        {
            if (room == null || string.IsNullOrEmpty(room.id)) return;
            _byId[room.id] = room;
        }

        public static void Unregister(string roomId)
        {
            if (string.IsNullOrEmpty(roomId)) return;
            _byId.Remove(roomId);
        }

        public static Room Get(string roomId)
        {
            if (string.IsNullOrEmpty(roomId)) return null;
            return _byId.TryGetValue(roomId, out var room) ? room : null;
        }

        public static void Clear() => _byId.Clear();

#if UNITY_2022_OR_NEWER || true
        [UnityEngine.RuntimeInitializeOnLoadMethod(
            UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnPlayModeEnter() => Clear();
#endif
    }
}
