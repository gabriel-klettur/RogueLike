using System.Collections.Generic;

namespace Valkur.Gameplay.World.Dungeon.Udemy.Builder
{
    /// <summary>
    /// Outputs from <see cref="DungeonBuilder.GenerateDungeon"/>. The room
    /// dictionary is keyed by RoomNodeSO id (= RoomData.Id at runtime).
    /// </summary>
    public sealed class DungeonBuildResult
    {
        public bool Success { get; set; }
        public Dictionary<string, Room> RoomsByNodeId { get; set; } = new Dictionary<string, Room>();
        public string FailureReason { get; set; } = string.Empty;
        public int OuterAttempts { get; set; }
        public int InnerAttempts { get; set; }

        public static DungeonBuildResult Failed(string reason)
            => new DungeonBuildResult { Success = false, FailureReason = reason ?? string.Empty };
    }
}
