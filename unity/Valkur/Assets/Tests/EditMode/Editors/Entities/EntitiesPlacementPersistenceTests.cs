using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Valkur.Core.Coordinates;
using Valkur.Data;
using Valkur.Gameplay.Entities;
using Valkur.Gameplay.World;
using Valkur.Infrastructure.Persistence.Repositories;

namespace Valkur.Tests.EditMode.Editors.Entities
{
    /// <summary>
    /// Covers the F5 placement round trip (audit Dimension 3 — "no repository, no
    /// entities_instances.json, nothing"): the interior-suspension write guard, and the
    /// "records the loader could not resolve survive a re-save" contract
    /// <see cref="EntityInstanceSerializer"/>'s pass-through path exists for.
    ///
    /// Deliberately does not exercise <c>SpawnMonsterAt</c>'s full spawn (it needs a
    /// <c>GameplaySceneSetup.MonsterPrefab</c> wired with a real entity rig — Bootstrap /
    /// Enemies concerns outside this change's scope). Instead a
    /// <see cref="PersistedEntityInstance"/> marker is attached directly, which is exactly the
    /// state a successful spawn leaves behind and exactly what
    /// <c>EntitiesRuntimeEditor.SavePlacedEntities</c> enumerates via
    /// <c>FindObjectsOfType</c> — so the save half is exercised faithfully either way.
    /// </summary>
    [TestFixture]
    public class EntitiesPlacementPersistenceTests
    {
        private const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;

        private readonly List<GameObject> _sceneObjects = new List<GameObject>();
        private readonly List<Object>     _scratchAssets = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _sceneObjects)
                if (go != null) Object.DestroyImmediate(go);
            _sceneObjects.Clear();

            foreach (var o in _scratchAssets)
                if (o != null) Object.DestroyImmediate(o);
            _scratchAssets.Clear();

            LogAssert.ignoreFailingMessages = false;
        }

        // ── Fixture helpers ──────────────────────────────────────────────────────

        private EntitiesRuntimeEditor CreateEditor()
        {
            var go = new GameObject("EntitiesEditorUnderTest");
            _sceneObjects.Add(go);
            return go.AddComponent<EntitiesRuntimeEditor>();
        }

        private void CreateZoneManager(string zoneName, Vector2Int gridOffset)
        {
            var go = new GameObject("ZoneManagerUnderTest");
            _sceneObjects.Add(go);
            var zm = go.AddComponent<ZoneManager>();
            zm.ReplaceZones(new[]
            {
                new ZoneManager.ZoneDefinition { zoneName = zoneName, gridOffset = gridOffset }
            });
        }

        private static void SetPrivateField(object target, string name, object value)
        {
            var f = target.GetType().GetField(name, NP);
            Assert.IsNotNull(f, $"field '{name}' must exist on {target.GetType().Name}");
            f.SetValue(target, value);
        }

        private static object GetPrivateField(object target, string name)
        {
            var f = target.GetType().GetField(name, NP);
            Assert.IsNotNull(f, $"field '{name}' must exist on {target.GetType().Name}");
            return f.GetValue(target);
        }

        private static object Invoke(object target, string method, params object[] args)
        {
            var m = target.GetType().GetMethod(method, NP);
            Assert.IsNotNull(m, $"method '{method}' must exist on {target.GetType().Name}");
            return m.Invoke(target, args);
        }

        /// <summary>Same reflection trick <c>WorldTransitionServiceTests.ForceSuspended</c>
        /// uses: the property has a public getter and a private setter.</summary>
        private static void ForceBaseWorldContentSuspended(bool value)
        {
            typeof(WorldTransitionService)
                .GetProperty("IsBaseWorldContentSuspended", BindingFlags.Public | BindingFlags.Static)
                .SetValue(null, value, null);
        }

        // ── The write guard ──────────────────────────────────────────────────────

        [Test]
        public void SavePlacedEntities_RefusedWhileBaseWorldContentIsSuspended()
        {
            LogAssert.ignoreFailingMessages = true;

            var ed = CreateEditor();
            var repo = new InMemoryEntityInstanceRepository();
            ed.SetEntityInstanceRepository(repo);

            ForceBaseWorldContentSuspended(true);
            try
            {
                bool result = (bool)Invoke(ed, "SavePlacedEntities");
                Assert.IsFalse(result,
                    "the audit explicitly calls out that Spawners lacks this guard; " +
                    "Entities must not repeat that gap.");
            }
            finally
            {
                ForceBaseWorldContentSuspended(false);
            }

            Assert.IsFalse(repo.Exists(WorldId.Base), "a refused save must write nothing at all");
        }

        [Test]
        public void SavePlacedEntities_OutsideATransition_WritesNormally()
        {
            var ed = CreateEditor();
            var repo = new InMemoryEntityInstanceRepository();
            ed.SetEntityInstanceRepository(repo);

            bool result = (bool)Invoke(ed, "SavePlacedEntities");

            Assert.IsTrue(result, "the guard must be inert outside a transition, " +
                                  "or every ordinary save stops working");
            Assert.IsTrue(repo.Exists(WorldId.Base));
        }

        // ── Re-save preserves records the loader could not resolve ─────────────

        [Test]
        public void ReSave_PreservesARecord_ItsLoaderCouldNotResolve()
        {
            var ed = CreateEditor();

            // Empty catalog: the monster key in the seeded record resolves against nothing,
            // so LoadPlacedEntities must carry it through rather than drop it.
            var catalog = ScriptableObject.CreateInstance<MonsterCatalog>();
            _scratchAssets.Add(catalog);
            SetPrivateField(ed, "_monsterCatalog", catalog);

            CreateZoneManager("Lobby", new Vector2Int(150, 50));

            var seed = EntityInstanceSerializer.FromWorldPosition(
                "id-unresolved", "no_such_monster", "Lobby",
                new Vector2(170f, 60f), new Vector2(150f, 50f), zoneHeightTiles: 50);
            string seedJson = EntityInstanceSerializer.Serialize(new[] { seed });

            var repo = new InMemoryEntityInstanceRepository();
            repo.WriteRawJson(WorldId.Base, seedJson);
            ed.SetEntityInstanceRepository(repo);

            Invoke(ed, "LoadPlacedEntities");

            var unresolved = (List<EntityInstanceRecord>)GetPrivateField(ed, "_unresolvedEntityRecords");
            Assert.AreEqual(1, unresolved.Count, "an unknown monster key must be carried through, not dropped");
            Assert.AreEqual("no_such_monster", unresolved[0].MonsterKey);

            Assert.AreEqual(0, Object.FindObjectsOfType<PersistedEntityInstance>().Length,
                "nothing should have been spawned for an unresolvable record");

            bool saved = (bool)Invoke(ed, "SavePlacedEntities");
            Assert.IsTrue(saved);

            string afterJson = repo.ReadRawJson(WorldId.Base);
            var reread = EntityInstanceSerializer.Deserialize(
                afterJson, new Dictionary<string, Vector2>(), zoneHeightTiles: 50);

            Assert.AreEqual(1, reread.Count,
                "the re-save must still hold the one record it could never spawn");
            Assert.AreEqual("no_such_monster", reread[0].MonsterKey);
            Assert.AreEqual(seed.TileCol, reread[0].TileCol,
                "a carried-through record must not have its tile touched");
            Assert.AreEqual(seed.TileRow, reread[0].TileRow);
        }

        [Test]
        public void ReSave_PreservesUnresolvedRecord_AlongsideALivePlacement()
        {
            // Proves the MERGE, not just the pass-through in isolation: a live placement and
            // an unresolved leftover must both survive the same save.
            var ed = CreateEditor();

            var catalog = ScriptableObject.CreateInstance<MonsterCatalog>();
            _scratchAssets.Add(catalog);
            SetPrivateField(ed, "_monsterCatalog", catalog);

            var repo = new InMemoryEntityInstanceRepository();
            ed.SetEntityInstanceRepository(repo);

            var live = new GameObject("LivePlacement");
            _sceneObjects.Add(live);
            live.transform.position = new Vector3(160f, 70f, 0f);
            var marker = live.AddComponent<PersistedEntityInstance>();
            marker.Initialize("id-live", "barbol");

            SetPrivateField(ed, "_unresolvedEntityRecords", new List<EntityInstanceRecord>
            {
                new EntityInstanceRecord
                {
                    Id = "id-orphan", MonsterKey = "ghost_key", Zone = "Lobby",
                    TileCol = 5, TileRow = 5,
                }
            });

            bool saved = (bool)Invoke(ed, "SavePlacedEntities");
            Assert.IsTrue(saved);

            var reread = EntityInstanceSerializer.Deserialize(
                repo.ReadRawJson(WorldId.Base), new Dictionary<string, Vector2>(), zoneHeightTiles: 50);

            Assert.AreEqual(2, reread.Count, "both the live placement and the orphaned record must be written");
            bool hasBarbol = reread.Exists(r => r.MonsterKey == "barbol");
            bool hasGhost  = reread.Exists(r => r.MonsterKey == "ghost_key" && r.TileCol == 5 && r.TileRow == 5);
            Assert.IsTrue(hasBarbol, "the live marker must be re-derived from its current position");
            Assert.IsTrue(hasGhost, "the orphaned record must be carried through with its original tile");
        }

        // ── Autosave debounce ────────────────────────────────────────────────────

        [Test]
        public void FlushEntityPlacementAutosave_WritesAPendingPlacement_WithoutWaitingForTheDebounce()
        {
            // Deactivate() and OnDestroy() both call FlushEntityPlacementAutosave() as their
            // first action (see EntitiesRuntimeEditor.cs / .Persistence.cs) — this is the
            // primitive that makes "place a monster, then Stop" keep it rather than losing the
            // last few seconds of edits to the 0.75s debounce window. Exercised directly here
            // rather than through Deactivate() itself, which also drives UI/camera state that
            // needs a full BuildUI() this fixture has no reason to construct.
            var ed = CreateEditor();
            var repo = new InMemoryEntityInstanceRepository();
            ed.SetEntityInstanceRepository(repo);

            Invoke(ed, "MarkEntityPlacementsDirty");
            Assert.IsFalse(repo.Exists(WorldId.Base), "must not have saved yet — the debounce has not elapsed");

            Invoke(ed, "FlushEntityPlacementAutosave");

            Assert.IsTrue(repo.Exists(WorldId.Base),
                "a pending placement must be written immediately on flush");
        }

        [Test]
        public void FlushEntityPlacementAutosave_IsANoOp_WhenNothingIsPending()
        {
            var ed = CreateEditor();
            var repo = new InMemoryEntityInstanceRepository();
            ed.SetEntityInstanceRepository(repo);

            Invoke(ed, "FlushEntityPlacementAutosave");

            Assert.IsFalse(repo.Exists(WorldId.Base),
                "flushing with nothing dirty must not write an empty file over nothing");
        }
    }
}
