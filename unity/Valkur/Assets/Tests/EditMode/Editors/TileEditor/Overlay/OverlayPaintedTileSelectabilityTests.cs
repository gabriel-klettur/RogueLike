// Reproduces the exact symptom the user reported: floor, floor_2, floor_4 and wall
// (among 69 distinct tile names in total) are painted across the shipped zone
// overlays in StreamingAssets/Maps/*.overlay.json, yet were completely unreachable
// from the Tile Editor picker — TileCatalog.BuildFromResources() had no category
// for them at all before the Resources/Tiles/_categories.json manifest fix.
//
// Two independent checks per painted tile name:
//   a) OverlayLoader can actually resolve it to a sprite (it always could, via the
//      direct Resources.Load("Tiles/" + name) mirror — this is the sanity leg).
//   b) TileCatalog exposes it as a selectable picker entry (this is the leg that
//      was BROKEN — a tile could be resolvable and paintable yet still invisible
//      to any author trying to select it in the editor).
//
// A regression that narrows either TileCatalog's or OverlayLoader's category
// coverage — reintroducing a hand-maintained array instead of reading the shared
// manifest — makes (b) fail here, because it fails against the REAL shipped map
// data, not a synthetic fixture that could go stale right along with the bug.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Tilemaps;
using Valkur.Gameplay.TileEditor;
using Valkur.Gameplay.World;

namespace Valkur.Tests.EditMode.Editors.TileEditor.Overlay
{
    [TestFixture]
    public class OverlayPaintedTileSelectabilityTests
    {
        private static readonly string MapsDir = Path.Combine(Application.streamingAssetsPath, "Maps");

        private TileCatalog _catalog;
        private HashSet<string> _catalogTileNames;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            _catalog = TileCatalog.BuildFromResources();
            _catalogTileNames = new HashSet<string>();
            foreach (var entry in _catalog.Entries) _catalogTileNames.Add(entry.tileName);
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            if (_catalog == null) return;
            foreach (var entry in _catalog.Entries)
                if (entry.tile != null) UnityEngine.Object.DestroyImmediate(entry.tile);
            UnityEngine.Object.DestroyImmediate(_catalog);
            _catalog = null;
        }

        [TearDown]
        public void TearDown() => TileRegistry.Instance.Load(null);

        // ── Real shipped-data scan ──────────────────────────────────────────

        private static IEnumerable<string> AllOverlayFiles()
        {
            if (!Directory.Exists(MapsDir)) yield break;
            foreach (var path in Directory.GetFiles(MapsDir, "*.overlay.json"))
                yield return path;
        }

        /// <summary>
        /// Every distinct non-empty tile name painted on ANY layer of ANY shipped
        /// overlay — not just Collision. Mirrors the cell-walk in
        /// OverlayCollisionDataIntegrityTests but across the whole layers dictionary.
        /// </summary>
        private static HashSet<string> CollectAllPaintedTileNames()
        {
            var names = new HashSet<string>();
            foreach (var path in AllOverlayFiles())
            {
                var root = MiniJsonRuntime.Deserialize(File.ReadAllText(path)) as Dictionary<string, object>;
                if (root == null) continue;
                if (!root.ContainsKey("layers")) continue;
                if (!(root["layers"] is Dictionary<string, object> layers)) continue;

                foreach (var layerEntry in layers)
                {
                    if (!(layerEntry.Value is List<object> rows)) continue;
                    foreach (var rowObj in rows)
                    {
                        if (!(rowObj is List<object> cols)) continue;
                        foreach (var cell in cols)
                        {
                            string name = cell?.ToString();
                            if (!string.IsNullOrEmpty(name)) names.Add(name);
                        }
                    }
                }
            }
            return names;
        }

        /// <summary>
        /// TileCatalog entries only ever store the bare sprite file name (no folder
        /// prefix). Some overlay-stored names carry a legacy path prefix (e.g.
        /// "ready/grass_dirt/tileset3_slices/tileset3_32_32") that OverlayLoader
        /// strips before falling back to its category-folder probe — mirror that
        /// here so the "selectable" check compares like with like.
        /// </summary>
        private static string BasenameOf(string tileName)
        {
            int slash = tileName.LastIndexOf('/');
            return slash < 0 ? tileName : tileName.Substring(slash + 1);
        }

        // ── (a) resolvability sanity leg ────────────────────────────────────

        [Test]
        public void EveryPaintedOverlayTileName_ResolvesViaOverlayLoader()
        {
            var names = new List<string>(CollectAllPaintedTileNames());
            Assert.Greater(names.Count, 0,
                "Sanity: no tile names found across shipped overlays — fixture broken?");

            var gridGo = new GameObject("WorldGridBuilder_OrphanTileCheck");
            try
            {
                var grid = gridGo.AddComponent<WorldGridBuilder>();
                grid.BuildGrid();

                var sb = new StringBuilder();
                sb.Append("{\"layers\":{\"Ground\":[[");
                for (int i = 0; i < names.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    sb.Append('"').Append(names[i]).Append('"');
                }
                sb.Append("]]}}");

                string tempPath = Path.Combine(Application.temporaryCachePath,
                    "ValkurOrphanTileCheck_" + Guid.NewGuid().ToString("N") + ".json");
                File.WriteAllText(tempPath, sb.ToString());
                try
                {
                    OverlayLoader.LoadOverlayFromPath(tempPath, grid, 0, 0, false, 0, 0);

                    var ground = grid.GetTilemap(TilemapLayerSetup.TilemapLayer.Ground);
                    var unresolved = new List<string>();
                    for (int i = 0; i < names.Count; i++)
                        if (ground.GetTile(new Vector3Int(i, 0, 0)) == null)
                            unresolved.Add(names[i]);

                    Assert.IsEmpty(unresolved,
                        "OverlayLoader could not resolve a sprite for tile name(s) that are " +
                        "actually painted in a shipped map: " + string.Join(", ", unresolved));
                }
                finally
                {
                    if (File.Exists(tempPath)) File.Delete(tempPath);
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(gridGo);
            }
        }

        // ── (b) the critical leg — this is the one the original bug broke ──

        [Test]
        public void EveryPaintedOverlayTileName_IsSelectableFromTheTilePicker()
        {
            var names = CollectAllPaintedTileNames();
            Assert.Greater(names.Count, 0,
                "Sanity: no tile names found across shipped overlays — fixture broken?");

            var unselectable = names
                .Where(name => !_catalogTileNames.Contains(BasenameOf(name)))
                .ToList();

            Assert.IsEmpty(unselectable,
                "Tile name(s) are painted in a shipped map but are NOT selectable from the Tile " +
                "Editor picker (TileCatalog.BuildFromResources): " + string.Join(", ", unselectable) +
                ". Every Resources/Tiles/ folder — and the loose root files via the synthetic " +
                "root category — must be reachable from the picker, or painted maps silently " +
                "regress into content nobody can re-author.");
        }

        // ── Explicit pin for the reported symptom ──────────────────────────

        [TestCase("floor")]
        [TestCase("floor_2")]
        [TestCase("floor_4")]
        [TestCase("wall")]
        public void KnownRegressionTileName_IsSelectable(string tileName)
        {
            Assert.IsTrue(_catalogTileNames.Contains(tileName),
                $"'{tileName}' must be selectable from the Tile Editor picker. This is the exact " +
                "orphaned-tile bug reported by the user: painted across shipped maps, unreachable " +
                "from the picker because Resources/Tiles/ root loose files had no category.");
        }
    }
}
