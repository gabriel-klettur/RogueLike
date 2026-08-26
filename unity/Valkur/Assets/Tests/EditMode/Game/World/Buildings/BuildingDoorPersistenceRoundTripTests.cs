using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Threading;
using NUnit.Framework;
using UnityEngine.TestTools;
using Valkur.Data;
using Valkur.Gameplay.Buildings;
using Valkur.Gameplay.World;

namespace Valkur.Tests.EditMode.Game.World.Buildings
{
    /// <summary>
    /// Asserts the COMPOSITION of the two halves of door persistence: the exact bytes
    /// <c>BuildingsRuntimeEditor.AppendDoorJson</c> writes, fed to the real
    /// <c>BuildingLoader.ParseInstances</c> that has to read them back.
    ///
    /// Testing either half alone proves nothing. That is not a style preference — spawners
    /// shipped for months with a writer that emitted absolute world coordinates into a field
    /// the loader read as zone-relative, and both halves passed their own tests while the
    /// data came back 150 tiles away every restart
    /// (.github/incidents/SPAWNER_COORDINATE_SPACE_DRIFT.md). This fixture is the guard that
    /// incident asked for, applied to the door block before it has a chance to drift.
    /// </summary>
    [TestFixture]
    public class BuildingDoorPersistenceRoundTripTests
    {
        [TearDown]
        public void TearDown() => LogAssert.ignoreFailingMessages = false;

        // ── Reflection seams ────────────────────────────────────────────────────

        private static string Write(BuildingDoorSpec spec)
        {
            var m = typeof(BuildingsRuntimeEditor).GetMethod(
                "AppendDoorJson", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(m, "Reflection: BuildingsRuntimeEditor.AppendDoorJson not found. " +
                                "If it was renamed, update this test — do not delete it.");

            var sb = new StringBuilder();
            m.Invoke(null, new object[] { sb, spec });
            return sb.ToString();
        }

        private static BuildingDoorSpec ReadBack(string doorFragment)
        {
            string json =
                "[{\"id\": 7, \"template_id\": 3, \"zone\": \"lobby\", \"rel_x\": 10, " +
                "\"rel_y\": 20, \"overrides\": {" + doorFragment + "}}]";

            var parse = typeof(BuildingLoader).GetMethod(
                "ParseInstances", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(parse, "Reflection: BuildingLoader.ParseInstances not found.");

            var list = parse.Invoke(null, new object[] { json }) as IList;
            Assert.IsNotNull(list, "ParseInstances returned null — the wrapper JSON is malformed.");
            Assert.AreEqual(1, list.Count, "Expected exactly one parsed instance.");

            var dto = list[0];
            var field = dto.GetType().GetField(
                "DoorSpec", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(field, "Reflection: BuildingInstanceDto.DoorSpec not found.");
            return field.GetValue(dto) as BuildingDoorSpec;
        }

        private static BuildingDoorSpec RoundTrip(BuildingDoorSpec spec) => ReadBack(Write(spec));

        // ── The pair ────────────────────────────────────────────────────────────

        [Test]
        public void FullySpecifiedDoor_SurvivesWriteThenRead()
        {
            var source = new BuildingDoorSpec
            {
                target          = "house_a_int.overlay.json",
                useDefaultSpawn = false,
                spawnX          = 25.5f,
                spawnY          = -3.25f,
                prompt          = "Enter",
            };

            var result = RoundTrip(source);

            Assert.IsNotNull(result, "The door block was written but did not come back.");
            Assert.AreEqual(source.target,          result.target);
            Assert.AreEqual(source.useDefaultSpawn, result.useDefaultSpawn);
            Assert.AreEqual(source.spawnX,          result.spawnX, 1e-3f);
            Assert.AreEqual(source.spawnY,          result.spawnY, 1e-3f);
            Assert.AreEqual(source.prompt,          result.prompt);
        }

        [Test]
        public void DefaultSpawnFlag_SurvivesWriteThenRead()
        {
            var source = new BuildingDoorSpec { target = "cave.overlay.json", useDefaultSpawn = true };

            var result = RoundTrip(source);

            Assert.IsNotNull(result);
            Assert.IsTrue(result.useDefaultSpawn,
                "use_default_spawn is omitted when false and must therefore be written when true, " +
                "or every door authored to land on the destination default silently gains " +
                "coordinates of (0, 0).");
        }

        [Test]
        public void EmptyPrompt_IsOmittedAndComesBackEmptyNotNull()
        {
            var source = new BuildingDoorSpec { target = "cave.overlay.json" };

            string written = Write(source);
            Assert.IsFalse(written.Contains("prompt"),
                "An empty prompt must not add a dead key to hundreds of records.");

            var result = RoundTrip(source);
            Assert.AreEqual(string.Empty, result.prompt,
                "An absent prompt must read as empty, never null — callers concatenate it.");
        }

        [Test]
        public void QuotesAndBackslashesInText_AreEscapedAndSurvive()
        {
            var source = new BuildingDoorSpec
            {
                target = "a\\b.overlay.json",
                prompt = "Enter the \"Old\" inn",
            };

            var result = RoundTrip(source);

            Assert.AreEqual(source.target, result.target, "Backslash escaping broke the target.");
            Assert.AreEqual(source.prompt, result.prompt, "Quote escaping broke the prompt.");
        }

        [Test]
        public void NegativeAndFractionalSpawns_KeepThreeDecimalsOfPrecision()
        {
            var source = new BuildingDoorSpec
            {
                target = "x.overlay.json",
                spawnX = -128.125f,
                spawnY = 0.375f,
            };

            var result = RoundTrip(source);

            Assert.AreEqual(source.spawnX, result.spawnX, 1e-3f);
            Assert.AreEqual(source.spawnY, result.spawnY, 1e-3f);
        }

        // ── Locale ──────────────────────────────────────────────────────────────

        [Test]
        public void CommaDecimalLocale_StillEmitsAParsableFile()
        {
            // A machine set to de-DE would otherwise write  "spawn_x": 25,5  which is two
            // JSON values, not one — the same trap split_ratio already guards against.
            var previous = Thread.CurrentThread.CurrentCulture;
            try
            {
                Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE");

                var source = new BuildingDoorSpec { target = "x.overlay.json", spawnX = 25.5f };
                string written = Write(source);

                Assert.IsTrue(written.Contains("25.500"),
                    $"Expected an invariant decimal point. Written: {written}");
                Assert.IsFalse(written.Contains("25,500"), $"Locale leaked into the file: {written}");

                var result = ReadBack(written);
                Assert.AreEqual(25.5f, result.spawnX, 1e-3f);
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = previous;
            }
        }

        // ── Absence ─────────────────────────────────────────────────────────────

        [Test]
        public void InstanceWithNoDoorBlock_ParsesToNoDoor()
        {
            var result = ReadBack("\"z_top\": 2");

            Assert.IsNull(result, "Only records that carry a door block may produce a spec.");
        }

        [Test]
        public void DoorBlockWithABlankTarget_ParsesToNoDoor()
        {
            // The symmetric guarantee to the writer's IsValid gate: a door entry someone
            // emptied must read as "leads nowhere", not as an inert trigger on the doorway.
            Assert.IsNull(ReadBack("\"door\": {\"target\": \"\"}"));
            Assert.IsNull(ReadBack("\"door\": {\"target\": \"   \"}"));
            Assert.IsNull(ReadBack("\"door\": {\"spawn_x\": 4.0}"));
        }

        [Test]
        public void PartialDoorBlock_FillsTheRestWithDefaults()
        {
            var result = ReadBack("\"door\": {\"target\": \"x.overlay.json\"}");

            Assert.IsNotNull(result);
            Assert.AreEqual("x.overlay.json", result.target);
            Assert.IsFalse(result.useDefaultSpawn);
            Assert.AreEqual(0f, result.spawnX, 1e-4f);
            Assert.AreEqual(0f, result.spawnY, 1e-4f);
            Assert.IsTrue(result.IsValid);
        }

        [Test]
        public void WrittenBlock_UsesTheKeysTheParserLooksFor()
        {
            // Names both sides agree on, spelled out once so a rename on either side fails
            // here instead of silently dropping data on the next save.
            string written = Write(new BuildingDoorSpec
            {
                target = "x.overlay.json", useDefaultSpawn = true, spawnX = 1f, spawnY = 2f, prompt = "P",
            });

            foreach (var key in new[] { "\"door\"", "\"target\"", "\"use_default_spawn\"",
                                        "\"spawn_x\"", "\"spawn_y\"", "\"prompt\"" })
                Assert.IsTrue(written.Contains(key), $"Missing key {key} in: {written}");
        }
    }
}
