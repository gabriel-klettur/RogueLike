using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Valkur.Gameplay;
using Valkur.Gameplay.Entities;
using Valkur.Gameplay.Spawners;

namespace Valkur.Tests.EditMode.Gameplay.Enemies
{
    /// <summary>
    /// <see cref="MonsterSpawner.DespawnAllForWorldSwap"/>: a map swap must take the old world's
    /// monsters with it, persistent vendors included. Before it, the first Seed World build ran
    /// with twelve vendors — the lobby's six had followed the player into the new map.
    /// </summary>
    public class MonsterSpawnerWorldSwapTests
    {
        private GameObject _spawnerGo;
        private MonsterSpawner _spawner;
        private readonly List<GameObject> _made = new List<GameObject>();

        [SetUp]
        public void SetUp()
        {
            _spawnerGo = new GameObject("MonsterSpawnerTest");
            _spawner = _spawnerGo.AddComponent<MonsterSpawner>();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _made) if (go != null) Object.DestroyImmediate(go);
            _made.Clear();
            if (_spawnerGo != null) Object.DestroyImmediate(_spawnerGo);
        }

        private GameObject Track(string name)
        {
            var go = new GameObject(name);
            _made.Add(go);
            var field = typeof(MonsterSpawner).GetField("_activeMonsters", BindingFlags.Instance | BindingFlags.NonPublic);
            ((List<GameObject>)field.GetValue(_spawner)).Add(go);
            return go;
        }

        [Test]
        public void PersistentVendors_AreRemovedBySwap_ThoughDistanceNeverRemovesThem()
        {
            var vendor = Track("Vendor");
            vendor.AddComponent<PersistentSpawnMarker>();
            var hostile = Track("Hostile");

            Assert.IsTrue(MonsterSpawner.IsExemptFromDespawn(vendor), "precondition: distance never removes a vendor");

            int removed = _spawner.DespawnAllForWorldSwap();

            Assert.AreEqual(2, removed);
            Assert.IsTrue(vendor == null, "the old world's vendor followed the player into the new map");
            Assert.IsTrue(hostile == null);
            Assert.AreEqual(0, _spawner.ActiveMonsterCount);
        }

        [Test]
        public void Allies_AndEditorPlacedEntities_SurviveTheSwap()
        {
            var ally = Track("Ally");
            ally.AddComponent<AlliedUnit>();
            var placed = Track("Placed");
            placed.AddComponent<PersistedEntityInstance>().Initialize(null, "barbol");

            int removed = _spawner.DespawnAllForWorldSwap();

            Assert.AreEqual(0, removed);
            Assert.IsTrue(ally != null, "an ally follows the player, not the map");
            Assert.IsTrue(placed != null, "placed entities belong to PlacedEntityService.ClearSpawned");
        }
    }
}
