using System.Collections.Generic;
using UnityEngine;

namespace Valkur.Core
{
    /// <summary>
    /// Central registry for runtime entities. Eliminates per-frame FindObjectOfType
    /// and FindGameObjectWithTag calls by providing O(1) lookups.
    /// 
    /// Entities register/unregister themselves in OnEnable/OnDisable via the
    /// EntityRegistration component, or manually via static API.
    /// </summary>
    public static class EntityRegistry
    {
        private static GameObject _player;
        private static Transform _playerTransform;
        private static readonly List<GameObject> _monsters = new List<GameObject>();
        private static readonly List<GameObject> _npcs = new List<GameObject>();

        // --- Player ---

        public static GameObject Player => _player;
        public static Transform PlayerTransform => _playerTransform;
        public static bool HasPlayer => _player != null;

        public static void RegisterPlayer(GameObject player)
        {
            _player = player;
            _playerTransform = player != null ? player.transform : null;
        }

        public static void UnregisterPlayer(GameObject player)
        {
            if (_player == player)
            {
                _player = null;
                _playerTransform = null;
            }
        }

        // --- Monsters ---

        public static IReadOnlyList<GameObject> Monsters => _monsters;
        public static int MonsterCount => _monsters.Count;

        public static void RegisterMonster(GameObject monster)
        {
            if (!_monsters.Contains(monster))
                _monsters.Add(monster);
        }

        public static void UnregisterMonster(GameObject monster)
        {
            _monsters.Remove(monster);
        }

        // --- NPCs ---

        public static IReadOnlyList<GameObject> NPCs => _npcs;

        public static void RegisterNPC(GameObject npc)
        {
            if (!_npcs.Contains(npc))
                _npcs.Add(npc);
        }

        public static void UnregisterNPC(GameObject npc)
        {
            _npcs.Remove(npc);
        }

        /// <summary>
        /// Clear all registrations. Call on scene unload or domain reload.
        /// </summary>
        public static void Clear()
        {
            _player = null;
            _playerTransform = null;
            _monsters.Clear();
            _npcs.Clear();
        }
    }
}
