using UnityEngine;

namespace Valkur.UI.HUD
{
    /// <summary>
    /// The one destination the PLAYER chose — set by clicking the world map, drawn on both
    /// maps with an edge pin, and cleared when they arrive.
    ///
    /// <para>Static because both maps read it and it describes the session, not a widget. It
    /// is deliberately not persisted: a pin is a note to self for the next few minutes, and a
    /// stale pin from yesterday's session pointing somewhere the player no longer cares about
    /// is worse than none.</para>
    /// </summary>
    public static class MinimapWaypoint
    {
        /// <summary>Distance at which arriving clears the pin.</summary>
        public const float ArrivalRadius = 2.5f;

        private static bool s_has;
        private static Vector2 s_position;
        private static int s_revision;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetMinimapWaypointStatics()
        {
            s_has = false;
            s_position = Vector2.zero;
            s_revision = 0;
        }

        public static bool HasWaypoint => s_has;
        public static Vector2 Position => s_position;

        /// <summary>Bumped on every set or clear, so a reader can spot a change.</summary>
        public static int Revision => s_revision;

        public static void Set(Vector2 world)
        {
            s_has = true;
            s_position = world;
            s_revision++;
        }

        public static void Clear()
        {
            if (!s_has) return;
            s_has = false;
            s_revision++;
        }

        /// <summary>Clear the pin if <paramref name="player"/> has reached it. True when it did.</summary>
        public static bool ClearIfReached(Vector2 player)
        {
            if (!s_has || (player - s_position).sqrMagnitude > ArrivalRadius * ArrivalRadius) return false;
            Clear();
            return true;
        }
    }
}
