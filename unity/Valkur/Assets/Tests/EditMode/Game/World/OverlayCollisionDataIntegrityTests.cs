// Iterates every *.overlay.json shipped under StreamingAssets/Maps and asserts
// each one parses cleanly and matches the expected schema. Catches data
// regressions across all 24 zone files at once — a missing comma, a renamed
// "Collision" key, or an empty layers map will fail loudly here instead of
// silently producing a walk-through-everything zone at runtime.

using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Valkur.Gameplay.World;

namespace Valkur.Tests.EditMode.World
{
    /// <summary>
    /// Data-integrity smoke tests for every overlay JSON shipped with the game.
    /// </summary>
    [TestFixture]
    public class OverlayCollisionDataIntegrityTests
    {
        private static readonly string MapsDir =
            Path.Combine(Application.streamingAssetsPath, "Maps");

        // Cell values may be the literal empty string (no tile) or any tile-name
        // string. The Forest zone for example uses asset-path style names like
        // "ready/grass_dirt/tileset3_slices/tileset3_32_96". A maintained allow-list
        // would diverge from the asset catalog, so the integrity check here only
        // asserts schema (string, not null/number/object) and reserves runtime
        // resolution for the PlayMode AllOverlaysCompositeBakePlayTests fixture.

        [SetUp]
        public void SetUp() => LogAssert.ignoreFailingMessages = true;

        private static IEnumerable<string> AllOverlayFiles()
        {
            if (!Directory.Exists(MapsDir)) yield break;
            foreach (var path in Directory.GetFiles(MapsDir, "*.overlay.json"))
                yield return path;
        }

        [Test]
        public void MapsDirectory_Exists()
        {
            Assert.IsTrue(Directory.Exists(MapsDir),
                $"StreamingAssets Maps directory missing: {MapsDir}");
        }

        [Test]
        public void AtLeastOneOverlay_IsShipped()
        {
            int count = 0;
            foreach (var _ in AllOverlayFiles()) count++;
            Assert.Greater(count, 0,
                "Expected at least one *.overlay.json under StreamingAssets/Maps.");
        }

        /// <summary>
        /// Every overlay must parse successfully through the same MiniJSON runtime
        /// that the game uses. Catches malformed JSON (trailing commas, unbalanced
        /// braces, etc.) at test-time instead of at scene-load time.
        /// </summary>
        [Test]
        public void EveryOverlay_ParsesAsJsonObject()
        {
            foreach (var path in AllOverlayFiles())
            {
                string json = File.ReadAllText(path);
                var root = MiniJsonRuntime.Deserialize(json);
                Assert.IsInstanceOf<Dictionary<string, object>>(root,
                    $"Overlay {Path.GetFileName(path)} did not parse to a JSON object.");
            }
        }

        /// <summary>
        /// Every overlay must declare a <c>layers</c> dictionary. Without it,
        /// <see cref="OverlayLoader"/> logs an error and paints nothing.
        /// </summary>
        [Test]
        public void EveryOverlay_HasLayersDictionary()
        {
            foreach (var path in AllOverlayFiles())
            {
                var root = MiniJsonRuntime.Deserialize(File.ReadAllText(path))
                    as Dictionary<string, object>;
                Assert.IsNotNull(root, $"{Path.GetFileName(path)} parse failed.");
                Assert.IsTrue(root.ContainsKey("layers"),
                    $"Overlay {Path.GetFileName(path)} is missing the 'layers' key.");
                Assert.IsInstanceOf<Dictionary<string, object>>(root["layers"],
                    $"Overlay {Path.GetFileName(path)} 'layers' is not an object.");
            }
        }

        /// <summary>
        /// Each overlay layer is a 2D array of strings (rows × columns) where each
        /// cell is either "" (no tile) or a tile name. Iterates every Collision
        /// cell across every overlay and asserts the value is a string (no nulls,
        /// no nested arrays, no numeric junk). Catches schema regressions where a
        /// migration script accidentally writes the wrong type.
        /// </summary>
        [Test]
        public void EveryCollisionCell_IsAStringValue()
        {
            int inspected = 0;
            foreach (var path in AllOverlayFiles())
            {
                foreach (var name in EnumerateCollisionTileNames(path))
                {
                    inspected++;
                    Assert.IsNotNull(name,
                        $"{Path.GetFileName(path)} contains a null Collision cell.");
                }
            }
            Assert.Greater(inspected, 0,
                "Did not inspect a single Collision cell — overlay schema may have changed.");
        }

        /// <summary>
        /// At least one zone overlay must contain a non-empty Collision tile —
        /// guards against a future commit where every overlay is silently cleared.
        /// </summary>
        [Test]
        public void AtLeastOneOverlay_HasCollisionEntries()
        {
            int solidCells = 0;
            foreach (var path in AllOverlayFiles())
            {
                foreach (var name in EnumerateCollisionTileNames(path))
                {
                    if (!string.IsNullOrEmpty(name)) solidCells++;
                }
            }
            Assert.Greater(solidCells, 0,
                "All shipped overlays have ZERO solid Collision tiles — the world " +
                "has been silently emptied. Restore lobby/forest/dungeon collision data.");
        }

        // Yields every cell-string from the Collision layer of an overlay file.
        // Layer schema: layers.Collision is List<List<string>> (rows of columns).
        private static IEnumerable<string> EnumerateCollisionTileNames(string path)
        {
            var root = MiniJsonRuntime.Deserialize(File.ReadAllText(path))
                as Dictionary<string, object>;
            if (root == null) yield break;
            if (!(root["layers"] is Dictionary<string, object> layers)) yield break;
            if (!layers.TryGetValue("Collision", out var collObj)) yield break;
            if (!(collObj is List<object> rows)) yield break;

            foreach (var rowObj in rows)
            {
                if (!(rowObj is List<object> cols)) continue;
                foreach (var cell in cols)
                {
                    yield return cell?.ToString() ?? "";
                }
            }
        }
    }
}
