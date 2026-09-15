using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Valkur.Core;
using Valkur.Data;

namespace Valkur.Tests.EditMode.Data.World
{
    /// <summary>
    /// The net under the authored world: if a run damages the shipped JSON, the NEXT run says so.
    ///
    /// <para><b>Why this exists.</b> On 2026-09-10 <c>particles_instances.json</c> went from
    /// <b>337,852 bytes and 188 placed emitters to 162 bytes and one</b>, and that one record was
    /// a fixture's — <c>preset_id "aura_smoke"</c>, spelled in exactly one place in the whole
    /// repository, <c>ParticlesDeleteInstanceTests</c>. Every particle in the world vanished. The
    /// suite stayed green, because nothing asserted anything about the file; the only trace was
    /// one warning at load about a preset the catalogue does not contain. It was noticed by a
    /// person playing the game.</para>
    ///
    /// <para><see cref="WorldDataWriteGuard"/> is the fix that stops the write. This is the
    /// SECOND line: a guard can be bypassed or forgotten, and a floor on the shipped data cannot.
    /// The preset check is the sharper half — it does not need a threshold to catch fixture
    /// pollution, because a record naming a preset nothing ships is a record no author wrote.</para>
    ///
    /// <para>The floors are deliberately well below the authored counts. A designer deleting a
    /// dozen emitters must not go red; a run reducing the file to a handful must.</para>
    /// </summary>
    public class ShippedWorldDataIntegrityTests
    {
        private static string StreamingRoot =>
            Path.Combine(Application.dataPath, "StreamingAssets");

        private static string Read(string relative)
        {
            string path = Path.Combine(StreamingRoot, relative);
            if (!File.Exists(path)) Assert.Ignore($"'{relative}' is not in this checkout.");
            return File.ReadAllText(path);
        }

        // ── Particles ────────────────────────────────────────────────────────

        /// <summary>
        /// Placed emitters, measured at 188 when this test was written. The floor is what
        /// separates "an author removed some" from "a process emptied the file".
        /// </summary>
        private const int PARTICLE_FLOOR = 120;

        [Test]
        public void ParticleInstances_AreStillThere()
        {
            var records = ParseInstances(Read("Particles/particles_instances.json"), "preset_id");
            Assert.GreaterOrEqual(records.Count, PARTICLE_FLOOR,
                $"particles_instances.json holds {records.Count} placed emitters. It held 188 when " +
                "this floor was written, and the incident this test exists for reduced it to ONE. " +
                "If the drop is a deliberate authoring change, lower the floor in the same commit " +
                "that removes the emitters.");
        }

        [Test]
        public void EveryPlacedParticle_NamesAPresetTheGameShips()
        {
            // The sharp half. A record naming a preset no catalogue contains was not authored by a
            // designer — it is fixture debris, and it is exactly what the 2026-09-10 loss left
            // behind ("aura_smoke"). This catches it on the next run with no threshold to tune.
            var catalog = LoadPresetCatalog();
            if (catalog == null) Assert.Ignore("ParticlePresetCatalog is not in this checkout.");

            var known = new HashSet<string>();
            foreach (var preset in catalog.Presets)
                if (preset != null && !string.IsNullOrEmpty(preset.id)) known.Add(preset.id);
            Assert.Greater(known.Count, 0, "The shipped preset catalogue is empty.");

            var unknown = new SortedSet<string>();
            foreach (var id in ParseInstances(Read("Particles/particles_instances.json"), "preset_id"))
                if (!known.Contains(id)) unknown.Add(id);

            Assert.IsEmpty(unknown,
                "particles_instances.json names preset(s) the catalogue does not contain: " +
                string.Join(", ", unknown) + ".\nA record like this is not authored content — it " +
                "is a test fixture that wrote the production file. See WorldDataWriteGuard.");
        }

        // ── The other files that can be wiped the same way ───────────────────

        [Test]
        public void BuildingInstances_AreStillThere()
        {
            var raw = Read("Buildings/buildings_instances.json");
            Assert.GreaterOrEqual(CountTopLevelRecords(raw), 200,
                "buildings_instances.json is far smaller than the authored world. This is the file " +
                "BUILDINGS_SAVE_POSITION_COLLAPSE.md and the particles incident both belong to.");
        }

        [Test]
        public void TheZoneDatabase_IsStillThere()
        {
            var raw = Read("Maps/zones_database.json");
            Assert.Greater(raw.Length, 500,
                "zones_database.json is nearly empty. An orphaned test runner filled the map " +
                "editor's zone sidecar with fixture zones once already.");
        }

        // ── Parsing ──────────────────────────────────────────────────────────

        /// <summary>
        /// Every value of <paramref name="field"/> in the file, one per record.
        ///
        /// <para>Hand-rolled rather than routed through the runtime parser on purpose: this test
        /// has to be able to report that the shipped file is broken, and a test that cannot read a
        /// damaged file cannot tell it apart from a missing one.</para>
        /// </summary>
        private static List<string> ParseInstances(string json, string field)
        {
            var found = new List<string>();
            if (string.IsNullOrEmpty(json)) return found;

            string needle = "\"" + field + "\"";
            int at = 0;
            while (true)
            {
                at = json.IndexOf(needle, at, System.StringComparison.Ordinal);
                if (at < 0) break;
                at += needle.Length;
                int colon = json.IndexOf(':', at);
                if (colon < 0) break;
                int open = json.IndexOf('"', colon);
                if (open < 0) break;
                int close = json.IndexOf('"', open + 1);
                if (close < 0) break;
                found.Add(json.Substring(open + 1, close - open - 1));
                at = close + 1;
            }
            return found;
        }

        /// <summary>Records in a top-level JSON array, counted by their opening braces.</summary>
        private static int CountTopLevelRecords(string json)
        {
            if (string.IsNullOrEmpty(json)) return 0;
            int depth = 0, count = 0;
            bool inString = false, escaped = false;
            foreach (char c in json)
            {
                if (escaped) { escaped = false; continue; }
                if (c == '\\') { escaped = true; continue; }
                if (c == '"') { inString = !inString; continue; }
                if (inString) continue;
                if (c == '{') { if (depth == 0) count++; depth++; }
                else if (c == '}') depth--;
            }
            return count;
        }

        private static ParticlePresetCatalog LoadPresetCatalog()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:ParticlePresetCatalog"))
            {
                var catalog = AssetDatabase.LoadAssetAtPath<ParticlePresetCatalog>(
                    AssetDatabase.GUIDToAssetPath(guid));
                if (catalog != null && catalog.Presets != null && catalog.Presets.Count > 0)
                    return catalog;
            }
            return null;
        }
    }
}
