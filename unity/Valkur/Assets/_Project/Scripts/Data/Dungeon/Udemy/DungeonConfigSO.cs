using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace Valkur.Data.Dungeon.Udemy
{
    /// <summary>
    /// Designer-tunable knobs for the Udemy dungeon strategy. Replaces the
    /// hard-coded constants in Udemy's <c>Settings.cs</c>. Lookup tiles
    /// (unwalkable / preferred) live here instead of a separate
    /// <c>GameResources</c> singleton â€” Valkur reads them via ServiceLocator.
    /// </summary>
    [CreateAssetMenu(
        fileName = "DungeonConfig",
        menuName = "Valkur/Dungeon/Udemy/Dungeon Config")]
    public class DungeonConfigSO : ScriptableObject
    {
        [Header("Generation Retries")]
        [Tooltip("Outer loop: how many distinct node graphs to try before giving up.")]
        [Min(1)] public int maxDungeonBuildAttempts = 10;

        [Tooltip("Inner loop: how many overlap-retry layouts to try per graph.")]
        [Min(1)] public int maxDungeonRebuildAttemptsForRoomGraph = 1000;

        [Header("A* Penalty")]
        [Tooltip("Default per-cell movement penalty (Udemy default = 40).")]
        [Min(0)] public int defaultMovementPenalty = 40;

        [Tooltip("Tiles in this list make a cell unwalkable (penalty 0).")]
        public List<TileBase> enemyUnwalkableCollisionTiles = new List<TileBase>();

        [Tooltip("Optional preferred-path tile. When matched, penalty drops to 1.")]
        public TileBase preferredEnemyPathTile;

        [Header("Doors")]
        [Tooltip("Delay (seconds) before automatically unlocking doors after enemies cleared.")]
        [Min(0f)] public float doorUnlockDelay = 1f;

        [Tooltip("Layer index used by Door triggers to detect the player (Valkur Player(8) by default).")]
        [Min(0)] public int playerLayer = 8;

        [Tooltip("Layer index used by Door triggers to detect player projectiles (Valkur Projectile(10) by default).")]
        [Min(0)] public int projectileLayer = 10;

        [Header("Room Lighting")]
        [Tooltip("Fade-in duration when the player first enters a room (seconds). Phase 2 â€” wired later.")]
        [Min(0f)] public float fadeInTime = 0.5f;
        [Header("Visual Fallback")]
        [Tooltip("Tile painted across the room interior when the prefab has no Ground tilemap. " +
                 "Lets you exercise the Udemy strategy with empty templates while authored prefabs are still in flight.")]
        public TileBase defaultFloorTile;

        [Tooltip("Tile painted around the room perimeter (outline) in fallback mode. Doorway openings stay floor.")]
        public TileBase defaultWallTile;
    }
}
