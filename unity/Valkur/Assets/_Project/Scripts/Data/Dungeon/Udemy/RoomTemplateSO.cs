using System.Collections.Generic;
using UnityEngine;

namespace Valkur.Data.Dungeon.Udemy
{
    /// <summary>
    /// Authored room template â€” the prefab whose tilemaps will be stamped into
    /// the world when the dungeon builder picks this template for a graph node
    /// of <see cref="roomNodeType"/>. Doorways, spawn points, and enemy budgets
    /// all live on the template so designers can tune each room independently.
    /// </summary>
    [CreateAssetMenu(
        fileName = "RoomTemplate_",
        menuName = "Valkur/Dungeon/Udemy/Room Template")]
    public class RoomTemplateSO : ScriptableObject
    {
        [HideInInspector] public string guid;

        [Tooltip("Room prefab containing all tilemaps and per-room game objects.")]
        public GameObject prefab;

        // Tracks the prefab the GUID was generated against; used to detect SO
        // duplication so the copy gets a fresh GUID instead of colliding.
        [HideInInspector] public GameObject previousPrefab;

        [Tooltip("Optional id of the music track played while the room is active and has enemies.")]
        public string battleMusicId;

        [Tooltip("Optional id of the music track played once the room is cleared.")]
        public string ambientMusicId;

        [Tooltip("Node type this template can fulfil in a graph (Entrance, Corridor NS, Chamber, Bossâ€¦).")]
        public RoomNodeTypeSO roomNodeType;

        [Tooltip("Lower-left corner of the room's tilemap AABB, in template-local tile coords.")]
        public Vector2Int lowerBounds;

        [Tooltip("Upper-right corner of the room's tilemap AABB, in template-local tile coords.")]
        public Vector2Int upperBounds;

        [Tooltip("Doorway openings in this room. The builder pairs them by opposite orientation.")]
        public List<Doorway> doorwayList = new List<Doorway>();

        [Tooltip("Possible spawn positions (enemies, chests). In template-local tile coords.")]
        public Vector2Int[] spawnPositionArray = System.Array.Empty<Vector2Int>();

        [Tooltip("Per-level enemy pools available for this room.")]
        public List<SpawnableEnemyByLevel> enemiesByLevelList = new List<SpawnableEnemyByLevel>();

        [Tooltip("Per-level spawn budget (counts, intervals, concurrency).")]
        public List<RoomEnemySpawnParameters> roomEnemySpawnParametersList = new List<RoomEnemySpawnParameters>();

        public List<Doorway> GetDoorwayList() => doorwayList;

#if UNITY_EDITOR
        private void OnValidate()
        {
            // Regenerate GUID when copy detected (different prefab or empty GUID).
            if (string.IsNullOrEmpty(guid) || previousPrefab != prefab)
            {
                guid = System.Guid.NewGuid().ToString();
                previousPrefab = prefab;
                UnityEditor.EditorUtility.SetDirty(this);
            }
        }
#endif

        /// <summary>Test hook: regenerate GUID outside the editor pipeline.</summary>
        public void TestRegenerateGuid()
        {
            guid = System.Guid.NewGuid().ToString();
            previousPrefab = prefab;
        }
    }
}
