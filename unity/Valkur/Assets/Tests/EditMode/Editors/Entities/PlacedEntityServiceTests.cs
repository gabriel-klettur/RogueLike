using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Valkur.Core.Coordinates;
using Valkur.Data;
using Valkur.Gameplay;
using Valkur.Gameplay.Entities;
using Valkur.Gameplay.World;
using Valkur.Infrastructure.Persistence.Repositories;

namespace Valkur.Tests.EditMode.Editors.Entities
{
    /// <summary>
    /// Hand-placed entities: the authored file, the standing instances and the run's kills.
    ///
    /// <para>The defect these exist for: killing a monster placed with the Entities editor did not
    /// reach the placement file, so it stood again on the next Play — unless some later editor
    /// action happened to trigger a save, which then deleted the placement for good, because the
    /// save enumerated whatever was alive in the scene. Every test below asserts one half of the
    /// split that fixes it: nothing a player does changes the authored file, and the run's kills
    /// travel with the save.</para>
    ///
    /// <para>The production spawn needs a full entity rig, so <c>SpawnOverride</c> builds a bare
    /// GameObject with a <see cref="Health"/> — which is everything the service reads.</para>
    /// </summary>
    [TestFixture]
    public class PlacedEntityServiceTests
    {
        private const float Tolerance = 0.001f;

        private readonly List<Object> _cleanup = new List<Object>();
        private InMemoryEntityInstanceRepository _repo;
        private MonsterCatalog _catalog;

        [SetUp]
        public void SetUp()
        {
            _repo = new InMemoryEntityInstanceRepository();

            _catalog = ScriptableObject.CreateInstance<MonsterCatalog>();
            _cleanup.Add(_catalog);
            AddDefinition("barbol");
            AddDefinition("knight_red");

            var zmGo = new GameObject("ZoneManagerUnderTest");
            _cleanup.Add(zmGo);
            zmGo.AddComponent<ZoneManager>().ReplaceZones(new[]
            {
                new ZoneManager.ZoneDefinition { zoneName = "Lobby", gridOffset = new Vector2Int(150, 50) }
            });
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _cleanup)
                if (o != null) Object.DestroyImmediate(o);
            _cleanup.Clear();

            foreach (var marker in Object.FindObjectsOfType<PersistedEntityInstance>())
                if (marker != null) Object.DestroyImmediate(marker.gameObject);

            ForceBaseWorldContentSuspended(false);
            LogAssert.ignoreFailingMessages = false;
        }

        // ── Fixture ──────────────────────────────────────────────────────────────

        private void AddDefinition(string key)
        {
            var def = ScriptableObject.CreateInstance<MonsterDefinition>();
            def.monsterKey = key;
            _cleanup.Add(def);
            _catalog.UpsertDefinition(def);
        }

        private PlacedEntityService CreateService()
        {
            var service = PlacedEntityService.GetOrCreate();
            _cleanup.Add(service.gameObject);
            service.SetRepository(_repo);
            service.SetMonsterCatalog(_catalog);
            service.SpawnOverride = (def, pos) =>
            {
                var go = new GameObject("Placed_" + def.monsterKey);
                go.transform.position = pos;
                go.AddComponent<Health>().Initialize(10);
                return go;
            };
            return service;
        }

        /// <summary>Seed the file as an author would have left it, and load it.</summary>
        private PlacedEntityService CreateLoadedService(params EntityInstanceRecord[] records)
        {
            _repo.WriteRawJson(WorldId.Base, EntityInstanceSerializer.Serialize(records));
            var service = CreateService();
            service.Load();
            return service;
        }

        private static EntityInstanceRecord Authored(string id, string key, Vector2 worldPos, float respawn = 0f)
        {
            var r = EntityInstanceSerializer.FromWorldPosition(id, key, "Lobby", worldPos,
                                                               new Vector2(150f, 50f), zoneHeightTiles: 50);
            r.RespawnSeconds = respawn;
            return r;
        }

        private List<EntityInstanceRecord> FileRecords()
            => EntityInstanceSerializer.Deserialize(_repo.ReadRawJson(WorldId.Base),
                   new Dictionary<string, Vector2> { { "Lobby", new Vector2(150f, 50f) } }, 50);

        /// <summary>
        /// Through the DoT entry, which skips the post-hit grace window: in Edit Mode Time.time does
        /// not advance, so a second TakeDamage on the same body would be refused by the grace and a
        /// test about a second death would pass without any death happening.
        /// </summary>
        private static void Kill(PersistedEntityInstance marker)
            => marker.GetComponent<Health>().TakeDotDamage(9999);

        private static void ForceBaseWorldContentSuspended(bool value)
        {
            typeof(WorldTransitionService)
                .GetProperty("IsBaseWorldContentSuspended", BindingFlags.Public | BindingFlags.Static)
                .SetValue(null, value, null);
        }

        // ── The bug ──────────────────────────────────────────────────────────────

        [Test]
        public void KillingAPlacement_NeverTouchesTheAuthoredFile()
        {
            var service = CreateLoadedService(Authored("k1", "knight_red", new Vector2(160f, 70f)));
            string before = _repo.ReadRawJson(WorldId.Base);

            Kill(service.LiveInstance("k1") ?? FindMarker("k1"));

            Assert.IsNull(service.LiveInstance("k1"), "a killed placement is no longer standing");
            Assert.AreEqual(PlacedEntityStatus.Defeated, service.StatusOf("k1", out _));
            Assert.IsFalse(service.IsDirty, "a kill is not an authoring edit and must schedule no write");
            Assert.AreEqual(before, _repo.ReadRawJson(WorldId.Base));
        }

        [Test]
        public void AnUnrelatedEditAfterAKill_KeepsTheKilledPlacementInTheFile()
        {
            // The half that used to DELETE placements: the save enumerated the living, so the
            // next edit anywhere in the editor wrote the killed one out of the world for good.
            var service = CreateLoadedService(
                Authored("k1", "knight_red", new Vector2(160f, 70f)),
                Authored("b1", "barbol", new Vector2(162f, 70f)));

            Kill(FindMarker("k1"));
            service.Place("barbol", new Vector3(165f, 72f, 0f));
            Assert.IsTrue(service.Save());

            var ids = FileRecords().ConvertAll(r => r.Id);
            Assert.Contains("k1", ids, "the killed placement is authored data and must survive the save");
            Assert.Contains("b1", ids);
            Assert.AreEqual(3, ids.Count);
        }

        [Test]
        public void AKilledPlacement_StaysDeadAcrossAReload_WhenTheRunSaysSo()
        {
            var service = CreateLoadedService(Authored("k1", "knight_red", new Vector2(160f, 70f)));
            Kill(FindMarker("k1"));

            service.Load();   // same run, world re-read (map swap back, interior exit)

            Assert.IsNull(service.LiveInstance("k1"));
            Assert.AreEqual(0, CountStandingMarkers("k1"), "a reload must not stand a killed placement up");
        }

        [Test]
        public void TheSaveCarriesTheKill_AndANewRunStartsWithEverythingStanding()
        {
            var service = CreateLoadedService(Authored("k1", "knight_red", new Vector2(160f, 70f)));
            Kill(FindMarker("k1"));

            var save = new GameSaveData();
            service.CollectInto(save);
            Assert.IsNotEmpty(save.GetMeta(PlacedEntityRunState.MetaKey));

            // Next session: the boot spawns the world, THEN restores the save.
            Object.DestroyImmediate(service.gameObject);
            var next = CreateLoadedService(Authored("k1", "knight_red", new Vector2(160f, 70f)));
            Assert.IsNotNull(next.LiveInstance("k1"), "before the restore the world is pristine");

            next.RestoreFrom(save);
            Assert.IsNull(next.LiveInstance("k1"), "restoring the save takes the dead placement down");
            Assert.AreEqual(PlacedEntityStatus.Defeated, next.StatusOf("k1", out _));

            // A save from before the kill (or before this layer) stands it back up.
            next.RestoreFrom(new GameSaveData());
            Assert.IsNotNull(next.LiveInstance("k1"));
        }

        [Test]
        public void RestoringASave_IsNotAKill()
        {
            var service = CreateLoadedService(Authored("k1", "knight_red", new Vector2(160f, 70f)));
            int defeats = 0;
            service.PlacementDefeated += _ => defeats++;

            var save = new GameSaveData();
            save.SetMeta(PlacedEntityRunState.MetaKey, "k1=0");
            service.RestoreFrom(save);

            Assert.AreEqual(0, defeats, "a despawn from a loaded save must not raise a kill");
        }

        // ── Respawn ──────────────────────────────────────────────────────────────

        [Test]
        public void ARespawningPlacement_ComesBackAtItsDeadline_AtItsAuthoredPosition()
        {
            var service = CreateLoadedService(Authored("k1", "knight_red", new Vector2(160f, 70f), respawn: 30f));
            var marker = FindMarker("k1");
            marker.transform.position = new Vector3(190f, 90f, 0f);   // it wandered before dying
            Kill(marker);

            Assert.AreEqual(PlacedEntityStatus.Respawning, service.StatusOf("k1", out double at));
            Assert.AreEqual(0, service.SpawnDue(at - 1d), "not before the deadline");
            Assert.AreEqual(1, service.SpawnDue(at + 1d));

            var back = service.LiveInstance("k1");
            Assert.IsNotNull(back);
            Assert.AreEqual(160f, back.transform.position.x, Tolerance, "it returns where the author put it");
            Assert.AreEqual(70f, back.transform.position.y, Tolerance);
        }

        [Test]
        public void ADeadlineThatPassedWhileTheWorldWasUnloaded_IsOver()
        {
            var service = CreateLoadedService(Authored("k1", "knight_red", new Vector2(160f, 70f), respawn: 30f));
            var save = new GameSaveData();
            save.SetMeta(PlacedEntityRunState.MetaKey, "k1=1");   // 1970: long past
            service.RestoreFrom(save);

            Assert.IsNotNull(service.LiveInstance("k1"));
            Assert.IsFalse(service.RunState.Contains("k1"));
        }

        [Test]
        public void TheCorpseOfAKilledPlacement_IsNoLongerThePlacement()
        {
            var service = CreateLoadedService(Authored("k1", "knight_red", new Vector2(160f, 70f), respawn: 30f));
            var corpse = FindMarker("k1");
            Kill(corpse);

            Assert.IsTrue(corpse.IsDefeated);
            service.SpawnDue(double.MaxValue);
            var fresh = service.LiveInstance("k1");
            Assert.AreNotSame(corpse, fresh);

            int defeats = 0;
            service.PlacementDefeated += _ => defeats++;
            corpse.GetComponent<Health>().Initialize(10);
            Kill(corpse);
            Assert.AreEqual(0, defeats, "an earlier life dying again is not a kill of the standing one");
            Assert.IsNotNull(service.LiveInstance("k1"));
        }

        // ── Authoring ────────────────────────────────────────────────────────────

        [Test]
        public void TheSaveWritesWhereTheAuthorPutIt_NotWhereTheMonsterWalked()
        {
            var service = CreateLoadedService(Authored("k1", "knight_red", new Vector2(160f, 70f)));
            var marker = FindMarker("k1");
            marker.transform.position = new Vector3(175f, 60f, 0f);

            service.SetRespawnSeconds("k1", 5f);   // any authoring edit
            Assert.IsTrue(service.Save());

            var saved = FileRecords().Find(r => r.Id == "k1");
            var expected = Authored("k1", "knight_red", new Vector2(160f, 70f));
            Assert.AreEqual(expected.TileCol, saved.TileCol);
            Assert.AreEqual(expected.TileRow, saved.TileRow);
            Assert.AreEqual(5f, saved.RespawnSeconds, Tolerance);
        }

        [Test]
        public void MovingAPlacement_MovesTheAuthoredRecord()
        {
            var service = CreateLoadedService(Authored("k1", "knight_red", new Vector2(160f, 70f)));
            Assert.IsTrue(service.SetAuthoredPosition("k1", new Vector3(170f, 75f, 0f)));
            Assert.IsTrue(service.IsDirty);
            service.Save();

            var expected = Authored("k1", "knight_red", new Vector2(170f, 75f));
            var saved = FileRecords().Find(r => r.Id == "k1");
            Assert.AreEqual(expected.TileCol, saved.TileCol);
            Assert.AreEqual(expected.TileRow, saved.TileRow);
        }

        [Test]
        public void DeletingADeadPlacement_RemovesItFromTheFileAndTheRun()
        {
            var service = CreateLoadedService(Authored("k1", "knight_red", new Vector2(160f, 70f)));
            Kill(FindMarker("k1"));

            Assert.IsTrue(service.Remove("k1"), "a dead placement can still be deleted by its author");
            Assert.IsFalse(service.RunState.Contains("k1"));
            Assert.AreEqual(PlacedEntityStatus.Unknown, service.StatusOf("k1", out _));

            // Deleting the LAST placement is an explicit removal, so the anti-wipe guard must let
            // it through — it exists for a table that is empty for no reason, not for this.
            Assert.IsTrue(service.Save());
            Assert.AreEqual(0, FileRecords().Count);
        }

        [Test]
        public void AnEmptyTableThatNobodyEmptied_IsNeverWrittenOverAPopulatedFile()
        {
            // A parse failure, or a load before the catalogue existed, leaves nothing to write.
            // Only removals the author made may shrink the file.
            _repo.WriteRawJson(WorldId.Base, "{ this is not json");
            var service = CreateService();
            LogAssert.ignoreFailingMessages = true;
            service.Load();
            int writes = CountWrites();

            service.Place("barbol", new Vector3(160f, 70f, 0f));
            Assert.IsTrue(service.Save(), "a real placement over an unreadable file is still saved");

            var guard = typeof(PlacedEntityService).GetMethod("AbortReason", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(guard.Invoke(null, new object[] { 0, 5, 0 }), "0 over 5 with no removals is a wipe");
            Assert.IsNull(guard.Invoke(null, new object[] { 0, 5, 5 }), "0 over 5 after deleting all five is an edit");
            Assert.IsNotNull(guard.Invoke(null, new object[] { 3, 20, 2 }), "20 -> 3 explained by 2 removals is still a collapse");
            Assert.IsNotNull(guard.Invoke(null, new object[] { 0, -1, 0 }), "never wipe a file that could not be parsed");
            Assert.Greater(CountWrites(), writes);
        }

        [Test]
        public void RestoringAPlacementById_ClearsAnyKillAgainstIt()
        {
            var service = CreateLoadedService(Authored("k1", "knight_red", new Vector2(160f, 70f)));
            Kill(FindMarker("k1"));

            var restored = service.Place("knight_red", new Vector3(160f, 70f, 0f), "k1");

            Assert.IsNotNull(restored);
            Assert.AreEqual("k1", restored.PlacementId);
            Assert.AreEqual(PlacedEntityStatus.Alive, service.StatusOf("k1", out _));
        }

        [Test]
        public void ReviveAll_StandsEveryDefeatedPlacementBackUp()
        {
            var service = CreateLoadedService(
                Authored("k1", "knight_red", new Vector2(160f, 70f)),
                Authored("b1", "barbol", new Vector2(162f, 70f)));
            Kill(FindMarker("k1"));
            Kill(FindMarker("b1"));

            Assert.AreEqual(2, service.ReviveAll());
            Assert.AreEqual(0, service.RunState.Count);
        }

        [Test]
        public void LoadingTwice_DoesNotDoubleTheWorld()
        {
            var service = CreateLoadedService(Authored("k1", "knight_red", new Vector2(160f, 70f)));
            service.Load();
            service.Load();
            Assert.AreEqual(1, CountStandingMarkers("k1"));
        }

        // ── Save guards (migrated from the editor's persistence fixture) ───────────

        [Test]
        public void Save_IsRefusedWhileBaseWorldContentIsSuspended()
        {
            var service = CreateLoadedService(Authored("k1", "knight_red", new Vector2(160f, 70f)));
            service.SetRespawnSeconds("k1", 3f);
            int writes = CountWrites();

            ForceBaseWorldContentSuspended(true);
            Assert.IsFalse(service.Save());
            Assert.AreEqual(writes, CountWrites(), "a refused save writes nothing at all");
        }

        [Test]
        public void Save_IsRefusedBeforeLoad_AndAfterClearSpawned()
        {
            _repo.WriteRawJson(WorldId.Base, EntityInstanceSerializer.Serialize(new[]
                { Authored("k1", "knight_red", new Vector2(160f, 70f)) }));
            var service = CreateService();
            Assert.IsFalse(service.Save(), "an empty, never-loaded table must not be written over the file");

            service.Load();
            service.ClearSpawned();
            Assert.IsFalse(service.Save(), "a torn-down world must not persist its emptiness");
            Assert.AreEqual(0, Object.FindObjectsOfType<PersistedEntityInstance>().Length);
            Assert.AreEqual(1, FileRecords().Count);
        }

        [Test]
        public void ClearSpawned_IsNotAKill()
        {
            var service = CreateLoadedService(Authored("k1", "knight_red", new Vector2(160f, 70f)));
            service.ClearSpawned();
            service.Load();
            Assert.IsNotNull(service.LiveInstance("k1"), "a world swap must not register a defeat");
        }

        [Test]
        public void ARecordTheLoaderCannotResolve_SurvivesAReSave()
        {
            var service = CreateLoadedService(
                Authored("ghost", "no_such_monster", new Vector2(160f, 70f)),
                Authored("k1", "knight_red", new Vector2(161f, 70f)));

            Assert.AreEqual(1, service.UnresolvedRecords.Count);
            service.SetRespawnSeconds("k1", 1f);
            Assert.IsTrue(service.Save());

            var saved = FileRecords();
            Assert.AreEqual(2, saved.Count);
            Assert.IsTrue(saved.Exists(r => r.Id == "ghost" && r.MonsterKey == "no_such_monster"));
        }

        [Test]
        public void AFixtureThatInjectsNoRepository_NeverReadsTheShippedFile()
        {
            var service = PlacedEntityService.GetOrCreate();
            _cleanup.Add(service.gameObject);
            service.SetMonsterCatalog(_catalog);
            service.SpawnOverride = (def, pos) => new GameObject("never");

            service.Load();
            Assert.AreEqual(0, service.Records.Count,
                "outside Play Mode the default store is in-memory; the shipped world must not spawn into a test");
        }

        // ── The wiring that makes it run at all ───────────────────────────────────

        [Test]
        public void TheBootSequence_LoadsPlacements_OutsideTheEditorBlock()
        {
            // The placements used to be loaded by the Entities editor, which a release build does
            // not create. Built with editors switched off, the sequence must still load them.
            var go = new GameObject("BootProbe");
            _cleanup.Add(go);
            var setup = go.AddComponent<GameplaySceneSetup>();
            var policy = System.Type.GetType("Valkur.Core.Boot.RuntimeEditorPolicy, Valkur.Core");
            Assert.IsNotNull(policy);

            var build = typeof(GameplaySceneSetup).GetMethod("BuildBootSequence", BindingFlags.NonPublic | BindingFlags.Instance);
            var steps = (List<Valkur.Core.Boot.BootStep>)build.Invoke(setup, null);

            int place = steps.FindIndex(s => s.Label == "Colocando las entidades del mapa");
            int restore = steps.FindIndex(s => s.Label == "Restaurando la partida");
            int spawner = steps.FindIndex(s => s.Label == "Inicializando los generadores de monstruos");
            Assert.GreaterOrEqual(place, 0, "the placement step is missing");
            Assert.Less(spawner, place, "placements spawn through the MonsterSpawner, so it must exist first");
            Assert.Less(place, restore, "the save restore takes down placements the run already killed");

            string sequence = File.ReadAllText(Path.Combine(Application.dataPath,
                "_Project/Scripts/Gameplay/Bootstrap/GameplaySceneSetup.Sequence.cs"));
            int call = sequence.IndexOf("EnsurePlacedEntityService", System.StringComparison.Ordinal);
            int editorBlock = sequence.IndexOf("EnsureEntitiesRuntimeEditor", System.StringComparison.Ordinal);
            Assert.Greater(call, 0);
            Assert.IsFalse(IsInsideIfEditorsBlock(sequence, call),
                "the placement step must not be gated with the authoring editors");
            Assert.IsTrue(IsInsideIfEditorsBlock(sequence, editorBlock), "sanity: the probe recognises the editor block");
        }

        [Test]
        public void TheNpcRespawnSystem_LeavesPlacementsToTheirOwner()
        {
            string src = File.ReadAllText(Path.Combine(Application.dataPath,
                "_Project/Scripts/Gameplay/Combat/Mechanics/NPCRespawnSystem.cs"));
            Assert.IsTrue(src.Contains("GetComponent<Entities.PersistedEntityInstance>() != null) return;"),
                "a placed neutral would otherwise come back twice: once on its own terms and once as an unmarked copy");
        }

        [Test]
        public void TheEditor_NoLongerSavesByScanningTheScene()
        {
            string editor = File.ReadAllText(Path.Combine(Application.dataPath,
                "_Project/Scripts/Gameplay/Editors/Entities/EntitiesRuntimeEditor.Persistence.cs"));
            Assert.IsFalse(editor.Contains("WriteRawJson"),
                "the editor must not write the placement file itself; PlacedEntityService owns it");
            Assert.IsFalse(editor.Contains("FindObjectsOfType<PersistedEntityInstance>"),
                "saving the living placements is the defect: a kill reads as a deletion");
        }

        // ── Helpers ──────────────────────────────────────────────────────────────

        private static PersistedEntityInstance FindMarker(string id)
        {
            foreach (var m in Object.FindObjectsOfType<PersistedEntityInstance>())
                if (m != null && m.PlacementId == id && !m.IsDefeated && m.gameObject.activeSelf) return m;
            Assert.Fail($"no standing placement '{id}'");
            return null;
        }

        private static int CountStandingMarkers(string id)
        {
            int n = 0;
            foreach (var m in Object.FindObjectsOfType<PersistedEntityInstance>())
                if (m != null && m.PlacementId == id && !m.IsDefeated && m.gameObject.activeSelf) n++;
            return n;
        }

        private int CountWrites() => _repo.WriteCount;

        /// <summary>True when <paramref name="index"/> falls inside an <c>if (editors) { ... }</c> block.</summary>
        private static bool IsInsideIfEditorsBlock(string src, int index)
        {
            int search = 0;
            while (true)
            {
                int open = src.IndexOf("if (editors)", search, System.StringComparison.Ordinal);
                if (open < 0 || open > index) return false;
                int brace = src.IndexOf('{', open);
                int depth = 0, end = brace;
                for (int i = brace; i < src.Length; i++)
                {
                    if (src[i] == '{') depth++;
                    else if (src[i] == '}' && --depth == 0) { end = i; break; }
                }
                if (index > brace && index < end) return true;
                search = end;
            }
        }
    }
}
