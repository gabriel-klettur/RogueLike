using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Valkur.Data;
using Valkur.Gameplay.World;
using Valkur.Core.Coordinates;
using Valkur.Infrastructure.Persistence.Repositories;

namespace Valkur.Tests.EditMode.Game.World.Lighting
{
    /// <summary>
    /// Drives <see cref="WorldLightLoader"/> through whole load → edit → save cycles against an
    /// in-memory repository and a synthetic catalog, so the assertions are about what actually
    /// lands on "disk" rather than about any one method in isolation.
    ///
    /// The audit's own conclusion was that half-tested persistence proves nothing: a save and a
    /// load can each look correct and still disagree with each other. Every test here therefore
    /// asserts the COMPOSITION.
    ///
    /// Each case corresponds to a defect measured on the shipped data:
    ///
    ///   * a <c>falloff</c> override was parsed off disk into a field nothing read, and the
    ///     live-light serialiser had no branch for it — so authoring one changed nothing and the
    ///     next save deleted it;
    ///   * a record with no <c>zone</c> key threw <c>ArgumentNullException</c> out of the load
    ///     loop (<c>Dictionary.TryGetValue</c> rejects a null key), abandoning every record after
    ///     it — those records reached neither the scene nor the preserved set, and the next save
    ///     removed them;
    ///   * a record naming a zone the world does not have was placed at a zero offset instead,
    ///     i.e. somewhere else entirely, and the first drag rebased it onto whatever zone that
    ///     wrong position fell in.
    /// </summary>
    [TestFixture]
    public class WorldLightLoaderLoadSaveCycleTests
    {
        private const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic
                                       | BindingFlags.Instance | BindingFlags.Static;

        private readonly List<UnityEngine.Object> _trash = new List<UnityEngine.Object>();
        private WorldLightLoader   _loader;
        private MemoryLightRepo    _repo;
        private LightPresetCatalog _catalog;

        /// <summary>An <see cref="ILightInstanceRepository"/> that never touches the project.</summary>
        private sealed class MemoryLightRepo : ILightInstanceRepository
        {
            public string Json;
            public int    Writes;
            public bool   Exists(WorldId worldId) => Json != null;
            public string ReadRawJson(WorldId worldId) => Json;
            public void   WriteRawJson(WorldId worldId, string json) { Json = json; Writes++; }
        }

        [SetUp]
        public void SetUp()
        {
            _catalog = ScriptableObject.CreateInstance<LightPresetCatalog>();
            _trash.Add(_catalog);
            _catalog.presets.Add(MakePreset("Torch"));
            _catalog.RebuildLookup();

            var go = new GameObject("CycleLoader");
            _trash.Add(go);
            _loader = go.AddComponent<WorldLightLoader>();
            _loader.SetCatalog(_catalog);

            _repo = new MemoryLightRepo();
            _loader.SetRepository(_repo);
        }

        [TearDown]
        public void TearDown()
        {
            // The loader spawns child GameObjects for its lights; destroying its own object
            // takes them with it.
            foreach (var o in _trash)
                if (o != null) UnityEngine.Object.DestroyImmediate(o);
            _trash.Clear();
        }

        private LightPresetDefinition MakePreset(string key)
        {
            var p = ScriptableObject.CreateInstance<LightPresetDefinition>();
            _trash.Add(p);
            p.presetKey = key;
            p.radius    = 160f;
            p.intensity = 1f;
            p.falloff   = 0.8f;
            p.color     = Color.white;
            return p;
        }

        private void Load() => LoaderMethod("LoadInstances").Invoke(_loader, null);
        private static MethodInfo LoaderMethod(string name)
            => typeof(WorldLightLoader).GetMethod(name, Any);

        // ─────────────────────────────────────────────────────────────────────────
        //  falloff
        // ─────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// An authored falloff must reach the live light AND come back out of the save.
        ///
        /// This is the quietest shape of data loss there is: the file stays well-formed, the light
        /// still lights, and the only evidence is that the value the author typed is not there any
        /// more. Nothing about the result looks wrong afterwards.
        /// </summary>
        [Test]
        public void FalloffOverride_IsAppliedOnLoadAndSurvivesTheSave()
        {
            _repo.Json = Json(@"{ ""id"": 1, ""preset_id"": ""Torch"", ""zone"": """",
                                  ""rel_x"": 0, ""rel_y"": 0,
                                  ""overrides"": { ""falloff"": 0.25 } }");
            Load();

            Assert.AreEqual(1, _loader.PersistentLightCount, "The record did not load.");
            var light = _loader.PersistentLightObjects[0]
                              .GetComponent<UnityEngine.Rendering.Universal.Light2D>();
            Assert.IsNotNull(light, "The spawned light has no Light2D.");
            Assert.AreEqual(0.25f, light.falloffIntensity, 0.001f,
                "The authored falloff never reached the light — it was parsed into a field " +
                $"nothing read, so the preset's {0.8f} won.");

            _loader.SaveAll();
            StringAssert.Contains("\"falloff\"", _repo.Json,
                "The save dropped the falloff override. Round-tripping the file therefore erases " +
                "it, which is how an authored value disappears without anything failing.");
            StringAssert.Contains("0.25", _repo.Json);
        }

        /// <summary>An absent falloff must not be invented on save.</summary>
        [Test]
        public void NoFalloffOverride_IsNotWrittenBack()
        {
            _repo.Json = Json(@"{ ""id"": 1, ""preset_id"": ""Torch"", ""zone"": """",
                                  ""rel_x"": 0, ""rel_y"": 0 }");
            Load();
            _loader.SaveAll();

            StringAssert.DoesNotContain("\"falloff\"", _repo.Json,
                "A falloff override appeared out of nowhere. Every later load then treats the " +
                "preset's value as overridden, so editing the preset stops reaching this light.");
        }

        // ─────────────────────────────────────────────────────────────────────────
        //  One bad record must cost only itself
        // ─────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// A record that throws while spawning must not take the rest of the file with it.
        ///
        /// The load loop had no per-record guard, so the first throw abandoned every remaining
        /// record — they reached neither <c>_activeLights</c> nor the preserved set, and the next
        /// save wrote the file without them. A missing <c>zone</c> key was enough to trigger it:
        /// JsonUtility leaves the string null, and ZoneManager hands it straight to
        /// <c>Dictionary.TryGetValue</c>, which throws on a null key.
        /// </summary>
        [Test]
        public void ARecordThatThrows_DoesNotDiscardTheRecordsAfterIt()
        {
            var zones = new GameObject("ZonesForThrow").AddComponent<ZoneManager>();
            _trash.Add(zones.gameObject);

            _repo.Json = Json(
                @"{ ""id"": 1, ""preset_id"": ""Torch"", ""zone"": """", ""rel_x"": 0, ""rel_y"": 0 }",
                // no "zone" key at all -> null -> the throw
                @"{ ""id"": 2, ""preset_id"": ""Torch"", ""rel_x"": 10, ""rel_y"": 10 }",
                @"{ ""id"": 3, ""preset_id"": ""Torch"", ""zone"": """", ""rel_x"": 20, ""rel_y"": 20 }");

            LogAssert.ignoreFailingMessages = true;   // the loader reports what it could not place
            Load();
            LogAssert.ignoreFailingMessages = false;

            Assert.AreEqual(3, _loader.PersistentLightCount,
                "Records were lost. Every record must end up either spawned or preserved — never " +
                "dropped, because a save writes exactly those two sets.");

            _loader.SaveAll();
            foreach (string id in new[] { "\"id\": 1", "\"id\": 2", "\"id\": 3" })
                StringAssert.Contains(id, _repo.Json, $"{id} vanished from the file.");
        }

        // ─────────────────────────────────────────────────────────────────────────
        //  An unresolvable zone is not a position
        // ─────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// A record naming a zone this world does not have must be preserved, not placed.
        ///
        /// Placing it at a zero offset does not give "roughly the right spot" — it gives a
        /// completely different one, which an author then drags to look right, and the save
        /// rebases the record onto whichever zone that new position falls in. The record is
        /// corrupted by an edit that looked like a correction.
        /// </summary>
        [Test]
        public void ARecordNamingAnUnknownZone_IsPreservedRatherThanPlacedAtTheOrigin()
        {
            var zones = new GameObject("ZonesForUnknown").AddComponent<ZoneManager>();
            _trash.Add(zones.gameObject);

            _repo.Json = Json(
                @"{ ""id"": 1, ""preset_id"": ""Torch"", ""zone"": ""zone_does_not_exist"",
                    ""rel_x"": 1323, ""rel_y"": 457 }");

            LogAssert.ignoreFailingMessages = true;
            Load();
            LogAssert.ignoreFailingMessages = false;

            Assert.AreEqual(0, _loader.PersistentLightObjects.Count,
                "The light was placed anyway, at a position its record does not describe.");
            Assert.AreEqual(1, _loader.UnspawnedRecordCount, "The record was not preserved.");

            _loader.SaveAll();
            StringAssert.Contains("zone_does_not_exist", _repo.Json,
                "The unresolvable record was deleted rather than kept. Preserving it is the whole " +
                "point: the zone may simply belong to a map slot that is not loaded.");
            StringAssert.Contains("1323", _repo.Json, "Its coordinates were not preserved verbatim.");
        }

        /// <summary>
        /// An EMPTY zone stays legal — it means the coordinates are already absolute, which is
        /// what the lobby and the pre-zone records use. Refusing those would delete real data.
        /// </summary>
        [Test]
        public void AnEmptyZone_StillPlacesTheLight()
        {
            var zones = new GameObject("ZonesForEmpty").AddComponent<ZoneManager>();
            _trash.Add(zones.gameObject);

            _repo.Json = Json(@"{ ""id"": 1, ""preset_id"": ""Torch"", ""zone"": """",
                                  ""rel_x"": 320, ""rel_y"": 0 }");
            Load();

            Assert.AreEqual(1, _loader.PersistentLightObjects.Count,
                "An empty zone was treated as unresolvable. It is not — it means absolute coords.");
        }

        // ─────────────────────────────────────────────────────────────────────────
        //  Teardown must not destroy what the loader does not own
        // ─────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Derived lights belong to their lamp-post building and are attached once, when that
        /// building spawns. ReloadAllWorldContent reloads the buildings first and this loader
        /// second, so a teardown that destroyed derived lights destroyed every fixture light in
        /// the world immediately after it was created — on every map-slot switch and every
        /// <c>reloadworld</c>, for the rest of the session.
        /// </summary>
        [Test]
        public void ClearSpawnedLights_LeavesDerivedLightsAlone()
        {
            _repo.Json = Json(@"{ ""id"": 1, ""preset_id"": ""Torch"", ""zone"": """",
                                  ""rel_x"": 0, ""rel_y"": 0 }");
            Load();

            var owner = new GameObject("LampPost");
            _trash.Add(owner);
            var derived = _loader.RegisterDerivedLight("Torch", Vector3.zero, owner.transform);
            Assert.IsNotNull(derived, "Precondition: the derived light was created.");
            Assert.AreEqual(1, _loader.DerivedLightCount);
            Assert.AreEqual(1, _loader.PersistentLightCount);

            _loader.ClearSpawnedLights();

            Assert.AreEqual(0, _loader.PersistentLightCount, "The authored light survived the teardown.");
            Assert.IsTrue(derived != null,
                "The derived light was destroyed by a loader that cannot re-create it — nothing " +
                "re-registers it until its building respawns.");
            Assert.AreEqual(1, _loader.DerivedLightCount,
                "The derived light lost its record, so the day/night gate and culling stop " +
                "reaching it even though the object is still there.");
        }

        // ─────────────────────────────────────────────────────────────────────────
        //  Colour reaches the screen
        // ─────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// An authored colour is picked in sRGB and must be handed to the light as linear
        /// radiance. Nothing in URP's 2D path does this conversion — Light2D.color is a plain C#
        /// field passed to the shader verbatim — so without it the encode back to display pulls
        /// every channel ratio toward 1 and a torch renders as warm grey.
        ///
        /// Measured on the shipped Torch, same frame, midnight: rendered saturation 0.167 before
        /// the conversion and 0.351 after.
        /// </summary>
        [Test]
        public void LightColour_IsHandedToTheLightAsLinearRadiance()
        {
            if (QualitySettings.activeColorSpace != ColorSpace.Linear)
                Assert.Ignore("Project is not in Linear colour space; the conversion is a no-op.");

            var authored = new Color(1f, 0.784313738f, 0.549019635f, 1f);   // the shipped Torch
            _catalog.presets[0].color = authored;
            _catalog.RebuildLookup();

            _repo.Json = Json(@"{ ""id"": 1, ""preset_id"": ""Torch"", ""zone"": """",
                                  ""rel_x"": 0, ""rel_y"": 0 }");
            Load();

            var light = _loader.PersistentLightObjects[0]
                              .GetComponent<UnityEngine.Rendering.Universal.Light2D>();
            var expected = authored.linear;

            Assert.AreEqual(expected.r, light.color.r, 0.002f);
            Assert.AreEqual(expected.g, light.color.g, 0.002f,
                "The green channel was not converted. Handing sRGB numbers to a linear renderer " +
                "costs more than half the chroma of every warm light in the game.");
            Assert.AreEqual(expected.b, light.color.b, 0.002f);

            // The peak channel must survive, or every light loses its brightness ceiling too.
            Assert.AreEqual(1f, light.color.r, 0.002f,
                "linear(1.0) is 1.0 — the conversion must cost chroma-carrying channels, never the peak.");
        }

        /// <summary>
        /// A per-instance colour override that equals its own preset is a copy the editor left
        /// behind, not an authoring decision. While such overrides are honoured, retuning a preset
        /// changes nothing and nothing says why — which is exactly what every one of the ten
        /// shipped records did.
        /// </summary>
        [Test]
        public void AColourOverrideIdenticalToItsPreset_DoesNotShadowThePreset()
        {
            _catalog.presets[0].color = new Color(1f, 0.784313738f, 0.549019635f, 1f);
            _catalog.RebuildLookup();

            // [255, 200, 140] is the preset colour, quantised the way the schema stores it.
            _repo.Json = Json(@"{ ""id"": 1, ""preset_id"": ""Torch"", ""zone"": """",
                                  ""rel_x"": 0, ""rel_y"": 0,
                                  ""overrides"": { ""color"": [255, 200, 140] } }");
            Load();

            var snap = _loader.CaptureLight(_loader.PersistentLightObjects[0]);
            var overrideColour = (Color?)snap.GetType().GetField("OverrideColor").GetValue(snap);
            Assert.IsFalse(overrideColour.HasValue,
                "A redundant colour override was kept. It pins the light to a value the preset " +
                "already carries, so editing the preset is silently inert.");

            _loader.SaveAll();
            StringAssert.DoesNotContain("\"color\"", _repo.Json,
                "The redundant override was written back, so the file re-acquires it on every save.");
        }

        /// <summary>A colour that genuinely differs must still win.</summary>
        [Test]
        public void AColourOverrideThatDiffers_StillWins()
        {
            _catalog.presets[0].color = new Color(1f, 0.784313738f, 0.549019635f, 1f);
            _catalog.RebuildLookup();

            _repo.Json = Json(@"{ ""id"": 1, ""preset_id"": ""Torch"", ""zone"": """",
                                  ""rel_x"": 0, ""rel_y"": 0,
                                  ""overrides"": { ""color"": [120, 200, 255] } }");
            Load();

            var snap = _loader.CaptureLight(_loader.PersistentLightObjects[0]);
            var overrideColour = (Color?)snap.GetType().GetField("OverrideColor").GetValue(snap);
            Assert.IsTrue(overrideColour.HasValue, "A real override was discarded.");
            Assert.AreEqual(120f / 255f, overrideColour.Value.r, 0.005f);
        }

        // ─────────────────────────────────────────────────────────────────────────
        //  Ids are the contract the whole editing layer rests on
        // ─────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Every undo command resolves its light through <c>FindLightById</c>, so an id that is
        /// zero or shared is not untidy — it is a command that silently addresses the wrong light,
        /// or none. Neither shape used to be checked anywhere on the read path.
        ///
        /// An absent "id" key deserializes to 0, which is the sentinel a derived light uses for
        /// "not addressable"; a duplicate resolves to whichever record loaded first, so deleting
        /// the second of the pair and pressing redo destroys the first.
        /// </summary>
        [Test]
        public void RecordIds_AreMadePositiveAndUniqueBeforeAnythingSpawns()
        {
            _repo.Json = Json(
                @"{ ""id"": 5, ""preset_id"": ""Torch"", ""zone"": """", ""rel_x"": 0,  ""rel_y"": 0 }",
                @"{ ""preset_id"": ""Torch"", ""zone"": """", ""rel_x"": 10, ""rel_y"": 0 }",   // no id at all
                @"{ ""id"": 5, ""preset_id"": ""Torch"", ""zone"": """", ""rel_x"": 20, ""rel_y"": 0 }",   // duplicate
                @"{ ""id"": 0, ""preset_id"": ""Torch"", ""zone"": """", ""rel_x"": 30, ""rel_y"": 0 }");  // explicit 0

            LogAssert.ignoreFailingMessages = true;   // each repair is reported
            Load();
            LogAssert.ignoreFailingMessages = false;

            Assert.AreEqual(4, _loader.PersistentLightObjects.Count, "All four records must load.");

            var ids = new HashSet<int>();
            foreach (var go in _loader.PersistentLightObjects)
            {
                var snap = _loader.CaptureLight(go);
                Assert.IsNotNull(snap,
                    "A light could not be captured — with a zero id it cannot take part in undo, " +
                    "so every command aimed at it is a silent no-op that still consumes a step.");
                int id = (int)snap.GetType().GetField("Id").GetValue(snap);
                Assert.Greater(id, 0, "A non-positive id survived the load.");
                Assert.IsTrue(ids.Add(id),
                    $"Two lights share id {id}. FindLightById returns the first, so a redo aimed " +
                    "at the second destroys the first instead.");
            }
        }

        /// <summary>
        /// An id must never be handed out twice within one loaded world, even after the light
        /// holding it is deleted.
        ///
        /// Recomputing "max + 1" from whatever is live looks equivalent and is not: delete the
        /// highest-numbered light and the next spawn is given its number back, so the delete
        /// command still sitting in the undo history now names the NEW light — and pressing redo
        /// destroys it. The bug needs no stale reference at all; recycling the id is enough.
        /// </summary>
        [Test]
        public void Ids_AreNeverRecycledWithinOneWorld()
        {
            _repo.Json = Json(
                @"{ ""id"": 1, ""preset_id"": ""Torch"", ""zone"": """", ""rel_x"": 0,  ""rel_y"": 0 }",
                @"{ ""id"": 2, ""preset_id"": ""Torch"", ""zone"": """", ""rel_x"": 10, ""rel_y"": 0 }");
            Load();

            var highest = _loader.FindLightById(2);
            Assert.IsNotNull(highest, "Precondition: id 2 loaded.");
            _loader.RemoveLight(highest);

            var replacement = _loader.RegisterRuntimeLight("Torch", new Vector3(20f, 0f, 0f));
            Assert.IsNotNull(replacement, "The replacement light did not spawn.");
            var snap = _loader.CaptureLight(replacement);
            int newId = (int)snap.GetType().GetField("Id").GetValue(snap);

            Assert.AreNotEqual(2, newId,
                "The deleted light's id was reissued. Any undo command still holding id 2 now " +
                "names this new light, so its redo deletes the wrong one.");
            Assert.Greater(newId, 2, "Ids must advance, not fill gaps.");
        }

        /// <summary>
        /// Reloading the world DOES reseed the allocator. Ids only have to be unique within one
        /// loaded world — carrying a counter across a slot switch would inflate every id in the
        /// file for no reason.
        /// </summary>
        [Test]
        public void Ids_RestartFromTheFileAfterTheWorldIsReplaced()
        {
            _repo.Json = Json(@"{ ""id"": 1, ""preset_id"": ""Torch"", ""zone"": """", ""rel_x"": 0, ""rel_y"": 0 }");
            Load();
            _loader.RegisterRuntimeLight("Torch", new Vector3(5f, 0f, 0f));   // takes id 2

            _loader.ClearSpawnedLights();
            Load();

            var spawned = _loader.RegisterRuntimeLight("Torch", new Vector3(9f, 0f, 0f));
            var snap = _loader.CaptureLight(spawned);
            int id = (int)snap.GetType().GetField("Id").GetValue(snap);
            Assert.AreEqual(2, id,
                "The allocator did not reseed from the reloaded file, so ids drift upward on every " +
                "reload and the file's numbering grows without bound.");
        }

        // ─────────────────────────────────────────────────────────────────────────
        //  Derived lights are not editable
        // ─────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// A derived light is a child of its building and is never written by a save. Moving or
        /// overriding one looks like it worked, does nothing to the file, and reverts on the next
        /// load — with the flame left displaced from its lamp-post in the meantime.
        /// </summary>
        [Test]
        public void MoveAndOverride_RefuseADerivedLight()
        {
            var owner = new GameObject("LampPost");
            _trash.Add(owner);
            owner.transform.position = new Vector3(5f, 5f, 0f);
            var derived = _loader.RegisterDerivedLight("Torch", new Vector3(5f, 5f, 0f), owner.transform);
            Assert.IsNotNull(derived, "Precondition.");
            var before = derived.transform.position;

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("Refusing to move"));
            _loader.MoveLight(derived, new Vector3(99f, 99f, 0f));
            Assert.AreEqual(before, derived.transform.position,
                "A building's light was dragged off its building.");

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("Refusing to override"));
            _loader.OverrideLight(derived, color: Color.red);
            var light = derived.GetComponent<UnityEngine.Rendering.Universal.Light2D>();
            Assert.AreNotEqual(Color.red, light.color,
                "An override was applied to a light no save will ever record.");
        }

        // ─────────────────────────────────────────────────────────────────────────
        //  Helpers
        // ─────────────────────────────────────────────────────────────────────────

        private static string Json(params string[] records) => "[" + string.Join(",", records) + "]";
    }
}
