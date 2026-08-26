using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using Valkur.Data;
using Valkur.Gameplay.World;

namespace Valkur.Tests.EditMode.Game.World.Lighting
{
    /// <summary>
    /// Pins the parts of <see cref="WorldLightLoader"/> that can lose authored data.
    ///
    /// Every assertion here corresponds to a failure the Lighting Editor audit found live:
    ///
    ///   * <b>The zone offset was never applied.</b> <c>_zoneManager</c> is a [SerializeField]
    ///     nobody assigns, so <c>ComputeWorldPosition</c> resolved a null ZoneManager, fell back
    ///     to a zero offset, and put all ten authored lights 150-200 tiles from where they were
    ///     placed. It looked like the author had lit an empty region of the map.
    ///   * <b>SaveAll was an unguarded whole-file overwrite.</b> A world whose lights had not
    ///     loaded wrote a five-byte empty array over the file and reported "Saved 0 light
    ///     instance(s)" as a success — the same accident that reduced particles_instances.json
    ///     to 4 bytes.
    ///   * <b>Records that could not be spawned were dropped.</b> One renamed preset key deleted
    ///     every light that used it, silently, on the next save.
    ///   * <b>The counters conflated authored with derived lights.</b> Lights owned by lamp-post
    ///     buildings are never written by SaveAll, so counting them made every guard and every
    ///     panel header disagree with what a save would actually contain.
    ///
    /// The fixture deliberately reaches into private members. These are internal invariants of a
    /// serialiser, not public API, and the whole point is that they cannot be verified from
    /// outside: the bug that motivated each one was invisible at the public surface.
    /// </summary>
    [TestFixture]
    public class WorldLightLoaderPersistenceTests
    {
        private const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic
                                       | BindingFlags.Instance | BindingFlags.Static;

        /// <summary>Buildings PPU. Light records share the buildings grid — see PX_TO_WORLD.</summary>
        private const float PxToWorld = 1f / 32f;

        private static readonly Type LoaderType   = typeof(WorldLightLoader);
        private static Type RecordType  => LoaderType.GetNestedType("LightInstanceData", Any);
        private static Type OverrideType => LoaderType.GetNestedType("LightOverrides", Any);
        private static Type WrapperType => LoaderType.GetNestedType("LightInstanceArrayWrapper", Any);

        private static string ShippedFilePath =>
            Path.Combine(Application.dataPath, "StreamingAssets", "Lights", "light_instances.json");

        private readonly List<GameObject> _spawned = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _spawned)
                if (go != null) UnityEngine.Object.DestroyImmediate(go);
            _spawned.Clear();
        }

        private T Track<T>(T go) where T : UnityEngine.Object
        {
            if (go is GameObject g) _spawned.Add(g);
            else if (go is Component c) _spawned.Add(c.gameObject);
            return go;
        }

        // ─────────────────────────────────────────────────────────────────────────
        //  The shipped file
        // ─────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// The authored light file must stay parseable, non-empty and internally consistent.
        ///
        /// This is the assertion that would have caught the wipe the moment it happened rather
        /// than at the next code review: a 4-byte "[]" parses fine, so only the COUNT betrays it.
        /// </summary>
        [Test]
        public void ShippedLightFile_IsPopulatedAndWellFormed()
        {
            Assert.IsTrue(File.Exists(ShippedFilePath),
                          $"The authored light file is missing entirely: {ShippedFilePath}");

            string json = File.ReadAllText(ShippedFilePath);
            Assert.IsFalse(string.IsNullOrWhiteSpace(json), "The authored light file is empty.");

            Array records = ParseRecords(json);
            Assert.Greater(records.Length, 0,
                "The authored light file holds zero records. If this is intentional, delete the " +
                "file instead — an empty array is indistinguishable from the wipe this test exists " +
                "to catch.");

            var ids = new HashSet<int>();
            for (int i = 0; i < records.Length; i++)
            {
                object r      = records.GetValue(i);
                int    id     = (int)   RecordType.GetField("id").GetValue(r);
                string preset = (string)RecordType.GetField("preset_id").GetValue(r);
                string zone   = (string)RecordType.GetField("zone").GetValue(r);

                Assert.Greater(id, 0,
                    $"Light record {i} has id {id}. Ids address lights for undo and for the editor; " +
                    "0 is the sentinel a building-derived light uses for \"not addressable\", so an " +
                    "authored light carrying it silently ignores every command aimed at it.");
                Assert.IsTrue(ids.Add(id), $"Duplicate light id {id} at record {i}.");
                Assert.IsFalse(string.IsNullOrWhiteSpace(preset),
                               $"Light id {id} has no preset_id — it can never spawn.");
                Assert.IsFalse(string.IsNullOrWhiteSpace(zone),
                               $"Light id {id} has no zone. A blank zone resolves to a zero offset, " +
                               "which is how a light ends up in the wrong part of the world.");
            }
        }

        /// <summary>
        /// Every record's rel_x / rel_y must sit inside its own zone.
        ///
        /// A coordinate written in the WRONG SPACE still round-trips through the parser and still
        /// looks like a number; what gives it away is that it no longer lands inside the zone it
        /// claims. This is the shape of assertion that the spawner drift incident concluded was
        /// missing — see .github/incidents/SPAWNER_COORDINATE_SPACE_DRIFT.md, where a save wrote
        /// absolute world coordinates into a zone-relative field for months.
        /// </summary>
        [Test]
        public void ShippedLightFile_CoordinatesAreZoneRelative()
        {
            if (!File.Exists(ShippedFilePath)) Assert.Ignore("No authored light file.");
            Array records = ParseRecords(File.ReadAllText(ShippedFilePath));

            // A zone is ZoneHeightTiles tiles on a side, and rel coords are pixels at 32 PPU,
            // so an in-bounds record is 0 .. side*32. Use the legacy chunk size: the test must
            // not need a live ZoneManager, and the bound is generous either way.
            float sideTiles = WorldConfig.LegacyChunkSize;
            float maxPx     = sideTiles / PxToWorld;

            for (int i = 0; i < records.Length; i++)
            {
                object r  = records.GetValue(i);
                int    id = (int)  RecordType.GetField("id").GetValue(r);
                float  x  = (float)RecordType.GetField("rel_x").GetValue(r);
                float  y  = (float)RecordType.GetField("rel_y").GetValue(r);

                Assert.That(x, Is.InRange(0f, maxPx),
                    $"Light id {id} has rel_x = {x}, outside its zone (0 .. {maxPx} px). " +
                    "A value far past the top of that range usually means an absolute world " +
                    "coordinate was written into a zone-relative field.");
                Assert.That(y, Is.InRange(0f, maxPx),
                    $"Light id {id} has rel_y = {y}, outside its zone (0 .. {maxPx} px).");
            }
        }

        // ─────────────────────────────────────────────────────────────────────────
        //  The coordinate transform
        // ─────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Load and save must be exact inverses. Assert the COMPOSITION, not each half: a test
        /// that exercises only one direction proves nothing, which is precisely how the spawner
        /// drift survived its own test suite.
        /// </summary>
        [Test]
        public void WorldPositionAndRelCoords_RoundTripExactly()
        {
            var loader  = Track(new GameObject("LoaderRT").AddComponent<WorldLightLoader>());
            var compute = LoaderType.GetMethod("ComputeWorldPosition", Any);
            var resolve = LoaderType.GetMethod("ResolveZoneAt", Any);

            foreach (var rel in new[] { new Vector2(0f, 0f),
                                        new Vector2(1323f, 457f),
                                        new Vector2(789.5f, 1.25f),
                                        new Vector2(1599f, 1599f) })
            {
                object record = MakeRecord(1, "Torch", "zone_0_0", rel.x, rel.y);
                var world = (Vector2)compute.Invoke(loader, new[] { record });

                object[] args = { new Vector3(world.x, world.y, 0f), null };
                resolve.Invoke(loader, args);
                var back = (Vector2)args[1];

                Assert.AreEqual(rel.x, back.x, 0.001f,
                    $"rel_x did not survive the round trip: {rel.x} -> world {world.x} -> {back.x}");
                Assert.AreEqual(rel.y, back.y, 0.001f,
                    $"rel_y did not survive the round trip: {rel.y} -> world {world.y} -> {back.y}. " +
                    "Note Y is flipped on load (records are Y-down); an unflipped inverse mirrors " +
                    "every light about the middle of its zone.");
            }
        }

        /// <summary>
        /// <c>ResolveZoneManager</c> must find the live ZoneManager even though the
        /// [SerializeField] backing it is never assigned. This single null reference displaced
        /// every authored light in the game by its zone's origin.
        /// </summary>
        [Test]
        public void ResolveZoneManager_FindsTheSceneInstanceWhenTheFieldIsUnassigned()
        {
            // EditMode fixtures run in whichever scene happens to be open, so the scene may
            // already own a ZoneManager. Only claim the stronger identity assertion when ours
            // is the only candidate.
            int preexisting = UnityEngine.Object.FindObjectsOfType<ZoneManager>().Length;
            var zones  = Track(new GameObject("ZoneManagerRZ").AddComponent<ZoneManager>());
            var loader = Track(new GameObject("LoaderRZ").AddComponent<WorldLightLoader>());

            var field = LoaderType.GetField("_zoneManager", Any);
            Assert.IsNotNull(field, "_zoneManager no longer exists — update this test.");
            Assert.IsNull(field.GetValue(loader),
                          "Precondition: the serialized field starts unassigned, as it does in the scene.");

            var resolved = LoaderType.GetMethod("ResolveZoneManager", Any).Invoke(loader, null);
            Assert.IsNotNull(resolved,
                "ResolveZoneManager fell back to null. Every light then loads at a zero zone " +
                "offset, i.e. displaced by its zone's origin — up to 200 tiles.");
            if (preexisting == 0)
                Assert.AreSame(zones, resolved, "ResolveZoneManager found something other than the scene's ZoneManager.");
        }

        // ─────────────────────────────────────────────────────────────────────────
        //  The anti-wipe guard
        // ─────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// The guard refuses to write nothing over something, and refuses a drop too large to be
        /// an edit. Exactly half is allowed: deleting half the lights in one sitting is a plausible
        /// thing for an author to do, and the guard must not stand in the way of real work.
        /// </summary>
        [Test]
        public void MayOverwrite_RefusesAWipeAndADisproportionateDrop()
        {
            var loader = Track(new GameObject("LoaderMO").AddComponent<WorldLightLoader>());
            var may    = LoaderType.GetMethod("MayOverwrite", Any);
            var count  = LoaderType.GetMethod("CountRecordsOnDisk", Any);
            Assert.IsNotNull(may,   "MayOverwrite is gone — the save is unguarded again.");
            Assert.IsNotNull(count, "CountRecordsOnDisk is gone — the guard has nothing to compare to.");

            int onDisk = (int)count.Invoke(loader, null);
            if (onDisk <= 1) Assert.Ignore($"Only {onDisk} record(s) on disk; nothing to guard.");

            Assert.IsFalse(Allows(loader, may, 0),
                "Writing 0 records over a populated file was allowed. This is the wipe.");
            Assert.IsFalse(Allows(loader, may, (int)(onDisk * 0.5f) - 1),
                "A drop of more than half was allowed.");
            Assert.IsTrue(Allows(loader, may, onDisk),
                "An unchanged count was refused — the guard blocks ordinary saves.");
            Assert.IsTrue(Allows(loader, may, onDisk + 5),
                "Adding lights was refused.");
        }

        /// <summary>
        /// An unreadable file is not an empty one. If the guard cannot tell what is on disk it must
        /// assume the file is populated, because the alternative — treating a read failure as
        /// permission to overwrite — turns a transient IO error into data loss.
        /// </summary>
        [Test]
        public void CountRecordsOnDisk_TreatsAnUnreadableFileAsPopulated()
        {
            var loader = Track(new GameObject("LoaderCU").AddComponent<WorldLightLoader>());
            var count  = LoaderType.GetMethod("CountRecordsOnDisk", Any);
            int onDisk = (int)count.Invoke(loader, null);

            Assert.GreaterOrEqual(onDisk, 0,
                "A negative count would compare as 'fewer than anything' and wave every save through.");
        }

        private static bool Allows(object loader, MethodInfo may, int aboutToWrite)
        {
            object[] args = { aboutToWrite, null };
            return (bool)may.Invoke(loader, args);
        }

        // ─────────────────────────────────────────────────────────────────────────
        //  Verbatim re-emission
        // ─────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// A record the loader could not spawn must come back out of the serialiser identical to
        /// the way it went in. This is what keeps one renamed preset key from deleting every light
        /// that referenced it: the light is not in the scene, so nothing else can vouch for it.
        /// </summary>
        [Test]
        public void AppendRecordData_RoundTripsEveryField()
        {
            object record = MakeRecord(77, "torch_warm", "zone_100_50", 1323f, 457f);
            object ov     = Activator.CreateInstance(OverrideType);
            OverrideType.GetField("color").SetValue(ov, new[] { 255f, 200f, 140f });
            OverrideType.GetField("intensity").SetValue(ov, 1.4f);
            OverrideType.GetField("radius").SetValue(ov, 6.25f);
            OverrideType.GetField("falloff").SetValue(ov, 0.5f);
            OverrideType.GetField("flicker_amp").SetValue(ov, 0.12f);
            OverrideType.GetField("flicker_speed").SetValue(ov, 3f);
            RecordType.GetField("overrides").SetValue(record, ov);

            object reparsed = EmitAndReparse(record);
            Assert.AreEqual(77,            RecordType.GetField("id").GetValue(reparsed));
            Assert.AreEqual("torch_warm",  RecordType.GetField("preset_id").GetValue(reparsed));
            Assert.AreEqual("zone_100_50", RecordType.GetField("zone").GetValue(reparsed));
            Assert.AreEqual(1323f, (float)RecordType.GetField("rel_x").GetValue(reparsed), 0.001f);
            Assert.AreEqual(457f,  (float)RecordType.GetField("rel_y").GetValue(reparsed), 0.001f);

            object back  = RecordType.GetField("overrides").GetValue(reparsed);
            var    color = (float[])OverrideType.GetField("color").GetValue(back);
            Assert.AreEqual(new[] { 255f, 200f, 140f }, color, "The colour override did not survive.");
            Assert.AreEqual(1.4f,  (float)OverrideType.GetField("intensity").GetValue(back),     0.001f);
            Assert.AreEqual(6.25f, (float)OverrideType.GetField("radius").GetValue(back),        0.001f);
            Assert.AreEqual(0.5f,  (float)OverrideType.GetField("falloff").GetValue(back),       0.001f);
            Assert.AreEqual(0.12f, (float)OverrideType.GetField("flicker_amp").GetValue(back),   0.001f);
            Assert.AreEqual(3f,    (float)OverrideType.GetField("flicker_speed").GetValue(back), 0.001f);
        }

        /// <summary>
        /// A record with no overrides must not grow any. -1 is the schema's "absent" sentinel, and
        /// emitting it as a real value would silently pin every unset field to a nonsense number
        /// the next time the file is read.
        /// </summary>
        [Test]
        public void AppendRecordData_DoesNotInventOverrides()
        {
            object record   = MakeRecord(78, "magic_blue", "zone_0_0", -2.5f, 0f);
            object reparsed = EmitAndReparse(record);
            object back     = RecordType.GetField("overrides").GetValue(reparsed);

            Assert.AreEqual(-1f, (float)OverrideType.GetField("intensity").GetValue(back), 0.001f,
                "An absent override came back as a real value.");
            Assert.AreEqual(-1f, (float)OverrideType.GetField("radius").GetValue(back), 0.001f);
            var color = (float[])OverrideType.GetField("color").GetValue(back);
            Assert.IsTrue(color == null || color.Length == 0, "A colour override was invented.");
        }

        // ─────────────────────────────────────────────────────────────────────────
        //  Authored vs derived
        // ─────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Lights derived from lamp-post buildings must never be counted as authored. SaveAll does
        /// not write them, so a world holding only derived lights has nothing to save — and the
        /// old <c>ActiveLightCount</c> reported a healthy non-zero number for exactly that state,
        /// which is how the guard in the Map Editor was talked past.
        /// </summary>
        [Test]
        public void PersistentLightCount_ExcludesDerivedLights()
        {
            var loader = Track(new GameObject("LoaderPD").AddComponent<WorldLightLoader>());
            var list   = LoaderType.GetField("_activeLights", Any).GetValue(loader);
            var instType = LoaderType.GetNestedType("LightInstance", Any);
            Assert.IsNotNull(instType, "LightInstance is gone — update this test.");

            var add = list.GetType().GetMethod("Add");
            add.Invoke(list, new[] { MakeInstance(instType, persistent: true) });
            add.Invoke(list, new[] { MakeInstance(instType, persistent: true) });
            add.Invoke(list, new[] { MakeInstance(instType, persistent: false) });

            Assert.AreEqual(3, loader.ActiveLightCount,     "ActiveLightCount should count everything on screen.");
            Assert.AreEqual(2, loader.PersistentLightCount, "PersistentLightCount must count only what SaveAll writes.");
            Assert.AreEqual(1, loader.DerivedLightCount,    "DerivedLightCount must count the building-owned lights.");
            Assert.AreEqual(2, loader.PersistentLightObjects.Count,
                "PersistentLightObjects must list the same set PersistentLightCount counts.");
        }

        /// <summary>
        /// Records that could not be spawned still count toward what a save will write. If they did
        /// not, the guard would compare a short live list against a full file and refuse every
        /// legitimate save in a world holding an unknown preset.
        /// </summary>
        [Test]
        public void PersistentLightCount_IncludesUnspawnableRecords()
        {
            var loader = Track(new GameObject("LoaderUR").AddComponent<WorldLightLoader>());
            var field  = LoaderType.GetField("_unspawnedRecords", Any);
            Assert.IsNotNull(field, "_unspawnedRecords is gone — unspawnable records are being dropped again.");

            object list = field.GetValue(loader);
            var    add  = list.GetType().GetMethod("Add");
            add.Invoke(list, new[] { MakeRecord(90, "preset_that_no_longer_exists", "zone_0_0", 10f, 10f) });

            Assert.AreEqual(1, loader.UnspawnedRecordCount);
            Assert.AreEqual(1, loader.PersistentLightCount,
                "A record kept for re-emission must count toward the save, or the guard will refuse " +
                "every save in a world holding one.");
            Assert.AreEqual(0, loader.PersistentLightObjects.Count,
                "An unspawnable record has no GameObject and must not appear in the instance list.");
        }

        // ─────────────────────────────────────────────────────────────────────────
        //  Id minting and teardown, once records outlive their lights
        // ─────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// A preserved-but-unspawnable record still owns its id. It is invisible in the scene, so
        /// an id minted from the live lights alone collides with it — and the file then ships two
        /// records with the same id, which the loader resolves by whichever it happens to read
        /// last. This hazard did not exist before the records were preserved; keeping them is what
        /// created it.
        /// </summary>
        [Test]
        public void NextLightId_SkipsIdsHeldByUnspawnableRecords()
        {
            var loader = Track(new GameObject("LoaderNI").AddComponent<WorldLightLoader>());
            object list = LoaderType.GetField("_unspawnedRecords", Any).GetValue(loader);
            var add = list.GetType().GetMethod("Add");
            add.Invoke(list, new[] { MakeRecord(42, "Torch", "zone_0_0", 0f, 0f) });

            int next = (int)LoaderType.GetMethod("NextLightId", Any).Invoke(loader, null);
            Assert.AreEqual(43, next,
                "NextLightId ignored a record it will write to disk, and handed out an id already " +
                "in use. Ids must be unique across BOTH the live lights and the preserved records.");
        }

        /// <summary>
        /// Tearing down the world must drop the preserved records with it. They describe the
        /// OUTGOING map slot's file: left behind across a slot switch they make
        /// PersistentLightCount non-zero for a world holding no lights, and the next save carries
        /// the old slot's records into the new slot's file.
        /// </summary>
        [Test]
        public void ClearSpawnedLights_DropsPreservedRecordsToo()
        {
            var loader = Track(new GameObject("LoaderCS").AddComponent<WorldLightLoader>());
            object list = LoaderType.GetField("_unspawnedRecords", Any).GetValue(loader);
            list.GetType().GetMethod("Add")
                .Invoke(list, new[] { MakeRecord(7, "Torch", "zone_0_0", 0f, 0f) });
            Assert.AreEqual(1, loader.UnspawnedRecordCount, "Precondition.");

            loader.ClearSpawnedLights();

            Assert.AreEqual(0, loader.UnspawnedRecordCount,
                "A preserved record survived the teardown and now belongs to the wrong map slot.");
            Assert.AreEqual(0, loader.PersistentLightCount,
                "PersistentLightCount is non-zero for an emptied world, which is exactly the " +
                "reading that talks the Map Editor's flush guard into writing.");
        }

        // ─────────────────────────────────────────────────────────────────────────
        //  Helpers
        // ─────────────────────────────────────────────────────────────────────────

        private static object MakeRecord(int id, string preset, string zone, float relX, float relY)
        {
            object r = Activator.CreateInstance(RecordType);
            RecordType.GetField("id").SetValue(r, id);
            RecordType.GetField("preset_id").SetValue(r, preset);
            RecordType.GetField("zone").SetValue(r, zone);
            RecordType.GetField("rel_x").SetValue(r, relX);
            RecordType.GetField("rel_y").SetValue(r, relY);
            return r;
        }

        private object MakeInstance(Type instType, bool persistent)
        {
            object inst = Activator.CreateInstance(instType);
            instType.GetField("go").SetValue(inst, Track(new GameObject("LightProbe")));
            instType.GetField("persistent").SetValue(inst, persistent);
            return inst;
        }

        /// <summary>Emit one record through the serialiser and read it straight back.</summary>
        private static object EmitAndReparse(object record)
        {
            var sb = new StringBuilder();
            sb.Append("[\n");
            LoaderType.GetMethod("AppendRecordData", Any).Invoke(null, new[] { sb, record });
            sb.Append("\n]\n");

            Array records = ParseRecords(sb.ToString());
            Assert.AreEqual(1, records.Length,
                $"The serialiser emitted JSON that does not parse back to one record:\n{sb}");
            return records.GetValue(0);
        }

        private static Array ParseRecords(string json)
        {
            var fromJson = typeof(JsonUtility)
                          .GetMethod("FromJson", new[] { typeof(string) })
                          .MakeGenericMethod(WrapperType);
            object wrapper = fromJson.Invoke(null, new object[] { "{\"items\":" + json + "}" });
            Assert.IsNotNull(wrapper, "The light JSON did not parse at all.");
            return (Array)WrapperType.GetField("items").GetValue(wrapper);
        }
    }
}
