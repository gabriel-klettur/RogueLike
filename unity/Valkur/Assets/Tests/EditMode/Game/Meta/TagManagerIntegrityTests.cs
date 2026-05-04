using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace Valkur.Tests.EditMode.Game.Meta
{
    /// <summary>
    /// Regression guard for the `ProjectSettings/TagManager.asset` corruption that
    /// silently broke every layer-aware system in the project.
    ///
    /// What happened (May 2026):
    ///   A test-cleanup commit added a new entry (`SpellPreview`) to the
    ///   <c>layers:</c> list inside TagManager.asset by replacing one of the
    ///   trailing empty slots with the new name — but did NOT add a fresh
    ///   empty slot at the end. The list went from 32 entries (Unity's hard
    ///   requirement) to 31. Unity then refused to register ANY layer past
    ///   index 4, which made every <c>LayerMask.NameToLayer("World")</c>,
    ///   <c>"NPC"</c>, <c>"Player"</c>, etc. return <c>-1</c>.
    ///
    ///   Symptoms in-game:
    ///     • Player / NPCs / Buildings invisible (sortinglayer fallback to Default
    ///       made every entity render below the ground tilemap).
    ///     • SpawnerOutlineRenderer + BuildingOutlineRenderer tests asserted
    ///       <c>"VFX"</c> but got <c>"Default"</c>.
    ///     • OverlayLoaderTests + TileOverlayPersistence tests crashed in SetUp
    ///       with <c>'A game object can only be in one layer. The layer needs
    ///       to be in the range [0...31]'</c> (caused by <c>gameObject.layer = -1</c>).
    ///
    /// What this test pins:
    ///   • Every Valkur layer name resolves to a valid id (0..31).
    ///   • Every Valkur sorting-layer name is registered with the runtime.
    ///   • A sample sorting-layer assignment round-trips (catches the case where
    ///     the asset has the entry but Unity's runtime cache hasn't loaded it).
    ///
    /// If this test fails, open `ProjectSettings/TagManager.asset`:
    ///   1. The `layers:` list MUST contain exactly 32 dash entries between
    ///      `layers:` and `m_SortingLayers:`. Count them with:
    ///        awk '/^  layers:/,/m_SortingLayers/' TagManager.asset | grep -c '^  -'
    ///   2. The `m_SortingLayers:` list must contain every Valkur sorting layer.
    /// </summary>
    [TestFixture]
    public class TagManagerIntegrityTests
    {
        // Canonical Valkur physics layers (per CLAUDE.md). Their indices are
        // historical — code paths assume these values and serialised data
        // (prefabs, scenes) reference them numerically. Renumbering breaks
        // every existing reference, so this test pins both name AND id.
        private static readonly (string name, int expectedIndex)[] PhysicsLayers =
        {
            ("Default",         0),
            ("TransparentFX",   1),
            ("Ignore Raycast",  2),
            ("Water",           4),
            ("UI",              5),
            ("Player",          8),
            ("NPC",             9),
            ("Projectile",     10),
            ("World",          11),
            ("Pickup",         12),
            ("UIBlocker",      13),
            ("Building",       14),
            ("Spawner",        15),
            ("ParticlePreview",16),
            ("SpellPreview",   17),
        };

        // Canonical Valkur sorting layers. Order matters for depth — these are
        // listed back-to-front (Background renders first, Overlay renders last).
        private static readonly string[] SortingLayers =
        {
            "Default",
            "Background",
            "Ground",
            "FloorDecals",
            "ObjectsLow",
            "WallsBottom",
            "Entities",
            "Decorations",
            "WallsTop",
            "ObjectsHigh",
            "Projectiles",
            "VFX",
            "Overhead",
            "UI_World",
            "Overlay",
        };

        // ── Physics layers ────────────────────────────────────────────────────

        [Test]
        public void EveryValkurPhysicsLayer_ResolvesToValidIndex()
        {
            var failed = new List<string>();
            foreach (var (name, expectedIndex) in PhysicsLayers)
            {
                int actual = LayerMask.NameToLayer(name);
                if (actual != expectedIndex)
                    failed.Add($"  '{name}': expected index {expectedIndex}, got {actual}");
            }

            Assert.IsEmpty(failed,
                "TagManager corruption detected. The most likely cause is that " +
                "ProjectSettings/TagManager.asset has fewer than 32 dash entries " +
                "in its `layers:` block (Unity hard-requires exactly 32). " +
                "Failed layers:\n" + string.Join("\n", failed));
        }

        [Test]
        public void NoValkurPhysicsLayer_FallsThroughToDefault()
        {
            // Catches the specific failure mode where TagManager has the right
            // strings on disk but Unity's runtime cache wasn't repopulated —
            // every NameToLayer call would silently return -1 (Unity API)
            // and code that does `go.layer = -1` then emits the "[0..31]"
            // editor error.
            foreach (var (name, _) in PhysicsLayers)
                Assert.GreaterOrEqual(LayerMask.NameToLayer(name), 0,
                    $"LayerMask.NameToLayer(\"{name}\") returned -1. " +
                    "Either the layer is missing from TagManager.asset OR " +
                    "Unity's runtime layer cache is stale (close + reopen Unity, " +
                    "or use SerializedObject API on the TagManager singleton).");
        }

        [Test]
        public void TagManager_LayerArray_HasExactly32Slots()
        {
            // Reads the on-disk asset directly (no Unity API) so a stale
            // runtime cache cannot mask the corruption. The layers: list
            // sits between `layers:` and `m_SortingLayers:` in the YAML.
            string path = System.IO.Path.Combine(
                Application.dataPath, "..", "ProjectSettings", "TagManager.asset");
            Assert.IsTrue(System.IO.File.Exists(path),
                "TagManager.asset must exist at " + path);

            var lines = System.IO.File.ReadAllLines(path);
            int dashCount = 0;
            bool inLayers = false;
            foreach (var raw in lines)
            {
                string line = raw.TrimEnd();
                if (line == "  layers:") { inLayers = true; continue; }
                if (inLayers && line.StartsWith("  m_SortingLayers")) break;
                if (inLayers && line.TrimStart().StartsWith("-")) dashCount++;
            }

            Assert.AreEqual(32, dashCount,
                $"TagManager.asset `layers:` block must contain exactly 32 " +
                $"entries (one per Unity layer slot 0..31). Found {dashCount}. " +
                $"Off-by-one corruption — likely a hand-edit that added a " +
                $"named layer without re-padding to 32. ");
        }

        // ── Sorting layers ────────────────────────────────────────────────────

        [Test]
        public void EveryValkurSortingLayer_IsRegisteredWithRuntime()
        {
            var registered = new HashSet<string>();
            foreach (var l in SortingLayer.layers)
                registered.Add(l.name);

            var missing = new List<string>();
            foreach (var expected in SortingLayers)
                if (!registered.Contains(expected))
                    missing.Add(expected);

            Assert.IsEmpty(missing,
                "Sorting layers missing from runtime cache: " +
                string.Join(", ", missing) +
                ". TagManager.asset on disk has them but Unity's in-memory " +
                "cache does not — close + reopen Unity, or repopulate via " +
                "SerializedObject API on the TagManager singleton.");
        }

        [Test]
        public void SortingLayerAssignment_RoundTripsForKnownLayers()
        {
            // The most user-visible symptom of the bug: assigning a real
            // sorting-layer name to a renderer silently falls back to "Default"
            // when Unity's runtime cache lost the entry. A direct round-trip
            // here would have caught the regression in CI.
            var go = new GameObject("SLRoundTripTest");
            try
            {
                var lr = go.AddComponent<LineRenderer>();
                foreach (var name in SortingLayers)
                {
                    if (name == "Default") continue; // baseline
                    lr.sortingLayerName = name;
                    Assert.AreEqual(name, lr.sortingLayerName,
                        $"Assigning sortingLayerName=\"{name}\" silently fell " +
                        $"back to \"{lr.sortingLayerName}\". This is the " +
                        $"\"world-space HP bar visible but no sprite\" symptom " +
                        $"reported by the user — every entity renders below " +
                        $"the ground tilemap.");
                }
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
    }
}
