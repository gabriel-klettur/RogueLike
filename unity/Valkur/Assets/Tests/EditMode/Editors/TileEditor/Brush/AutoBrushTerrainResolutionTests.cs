using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Tilemaps;
using Valkur.Data;
using Valkur.Gameplay.TileEditor;

namespace Valkur.Tests.EditMode.Editors.TileEditor.Brush
{
    /// <summary>
    /// Reproduces <c>TileEditorManager.ResolveAutoBrushTerrain</c>'s exact
    /// algorithm against real <see cref="TerrainCatalog"/> / <see cref="TilesetRuleset"/>
    /// objects. The manager itself is a MonoBehaviour that needs a full Grid +
    /// WorldGridBuilder scene to spin up in EditMode — see
    /// <c>AutoTileRegionUndoTests</c>'s class doc (Editors/TileEditor/Undo) for
    /// the project's standing rationale on why it isn't instantiated for this
    /// kind of test.
    ///
    /// This is the guard that exists specifically because a Corner16 pack is BY
    /// DEFINITION a two-material transition ruleset, so
    /// <see cref="TerrainCatalog.FindBaseRuleset"/> (which explicitly excludes
    /// every transition) never finds it. Checking only "does a ruleset asset
    /// exist for this folder" would report success at toggle-time and then
    /// silently paint zero cells on every single stroke — exactly the failure
    /// mode this suite proves the real gate avoids.
    /// </summary>
    [TestFixture]
    public class AutoBrushTerrainResolutionTests
    {
        private readonly List<Object> _scriptableObjects = new List<Object>();
        private readonly List<Sprite> _sprites = new List<Sprite>();
        private readonly List<GameObject> _created = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _created)
                if (go != null) Object.DestroyImmediate(go);
            _created.Clear();

            foreach (var s in _sprites)
            {
                if (s == null) continue;
                if (s.texture != null) Object.DestroyImmediate(s.texture);
                Object.DestroyImmediate(s);
            }
            _sprites.Clear();

            foreach (var so in _scriptableObjects)
                if (so != null) Object.DestroyImmediate(so);
            _scriptableObjects.Clear();

            TileRegistry.Instance.Clear();
            TerrainCatalogLoader.InvalidateCache();
        }

        private Sprite NewSprite(string name)
        {
            var tex = new Texture2D(1, 1);
            var sprite = Sprite.Create(tex, new Rect(0, 0, 1, 1), Vector2.zero);
            sprite.name = name;
            _sprites.Add(sprite);
            return sprite;
        }

        private TilesetRuleset NewRuleset(string folder, string primary, string secondary, AutoTileModel model)
        {
            var rs = ScriptableObject.CreateInstance<TilesetRuleset>();
            _scriptableObjects.Add(rs);
            rs.EditorSetMetadata(folder, primary, secondary, 0, model);
            if (model == AutoTileModel.Corner16)
            {
                for (int i = 0; i < 16; i++)
                    rs.EditorSetSlot((Corner16Slot)i, new[] { NewSprite($"{folder}_c{i}") });
            }
            else
            {
                for (int i = 0; i < 16; i++)
                    rs.EditorSetSlot((Blob16Slot)i, new[] { NewSprite($"{folder}_b{i}") });
            }
            return rs;
        }

        private TerrainCatalog NewCatalog(params TilesetRuleset[] rulesets)
        {
            var catalog = ScriptableObject.CreateInstance<TerrainCatalog>();
            _scriptableObjects.Add(catalog);
            foreach (var rs in rulesets) catalog.EditorAdd(rs);
            return catalog;
        }

        private Tilemap NewTilemap()
        {
            var gridGo = new GameObject("Grid");
            _created.Add(gridGo);
            gridGo.AddComponent<Grid>();
            var tilemapGo = new GameObject("Tilemap");
            tilemapGo.transform.SetParent(gridGo.transform);
            _created.Add(tilemapGo);
            return tilemapGo.AddComponent<Tilemap>();
        }

        /// <summary>Reproduces TileEditorManager.ResolveAutoBrushTerrain's body
        /// verbatim, minus the TerrainCatalogLoader.Load() call — the caller
        /// supplies the catalog directly here for testability, same pattern as
        /// AutoTileRegionUndoTests.CommitAutoTileRegionLike.</summary>
        private static (string Terrain, string Reason) ResolveAutoBrushTerrainLike(TerrainCatalog catalog, string category)
        {
            if (string.IsNullOrEmpty(category))
                return (null, TileEditorConstants.NoTileSelectedHint);

            string primary = null;
            var rulesets = catalog.Rulesets;
            for (int i = 0; i < rulesets.Count; i++)
            {
                var r = rulesets[i];
                if (r != null && r.FolderName == category)
                {
                    primary = r.TerrainPrimary;
                    break;
                }
            }

            // Mirrors the production gate. It must call the SAME selector the paint
            // path uses (FindPaintRuleset), not a copy of an older rule: this helper
            // duplicated FindBaseRuleset and therefore kept passing after the paint
            // path moved on, which is exactly how a test starts guarding the wrong
            // contract without anyone noticing.
            if (string.IsNullOrEmpty(primary) || catalog.FindPaintRuleset(primary) == null)
                return (null, TileEditorConstants.NoRulesetForCategoryHint);

            return (primary, null);
        }

        [Test]
        public void EmptyCategory_ReturnsNoTileSelectedHint()
        {
            var catalog = NewCatalog();
            var (terrain, reason) = ResolveAutoBrushTerrainLike(catalog, "");
            Assert.IsNull(terrain);
            Assert.AreEqual(TileEditorConstants.NoTileSelectedHint, reason);
        }

        [Test]
        public void CategoryMatchesNoRuleset_ReturnsNoRulesetHint()
        {
            var catalog = NewCatalog();
            var (terrain, reason) = ResolveAutoBrushTerrainLike(catalog, "nonexistent_pack");
            Assert.IsNull(terrain);
            Assert.AreEqual(TileEditorConstants.NoRulesetForCategoryHint, reason);
        }

        [Test]
        public void CategoryMatchesRulesetWithEmptyPrimary_ReturnsNoRulesetHint()
        {
            var rs = NewRuleset("broken_pack", "", null, AutoTileModel.Blob16);
            var catalog = NewCatalog(rs);
            var (terrain, reason) = ResolveAutoBrushTerrainLike(catalog, "broken_pack");
            Assert.IsNull(terrain);
            Assert.AreEqual(TileEditorConstants.NoRulesetForCategoryHint, reason);
        }

        [Test]
        public void CategoryMatchesOnlyACorner16Ruleset_IsAccepted()
        {
            // The state of all 5 imported corner packs. A Corner16 sheet ALWAYS
            // declares a secondary terrain -- its corners are what separate A from
            // B -- so it is a "transition" by the cardinal model's definition while
            // being the only sheet that can paint its terrain. FindBaseRuleset
            // excludes it for that reason, which left every generated pack
            // unreachable; FindPaintRuleset is the selector that accepts it.
            var rs = NewRuleset("grass_dirt", "grass", "dirt", AutoTileModel.Corner16);
            var catalog = NewCatalog(rs);
            var (terrain, reason) = ResolveAutoBrushTerrainLike(catalog, "grass_dirt");
            Assert.AreEqual("grass", terrain, "A Corner16 pack must be paintable — it is the whole point of the model.");
            Assert.IsNull(reason);
        }

        [Test]
        public void CategoryMatchesABaseRuleset_ReturnsPrimaryTerrainSuccessfully()
        {
            var rs = NewRuleset("solid_grass", "grass", null, AutoTileModel.Blob16);
            var catalog = NewCatalog(rs);
            var (terrain, reason) = ResolveAutoBrushTerrainLike(catalog, "solid_grass");
            Assert.AreEqual("grass", terrain);
            Assert.IsNull(reason);
        }

        [Test]
        public void CategoryMatchesTransition_ButABaseRulesetForItsPrimaryIsAlsoRegistered_GateSucceeds()
        {
            // Proves the escape hatch: once an author registers a plain base
            // ruleset for the transition's primary terrain, AUTO starts working
            // for that pack without any change to the transition ruleset itself.
            var transition = NewRuleset("grass_dirt2", "grass", "dirt", AutoTileModel.Corner16);
            var baseGrass = NewRuleset("solid_grass2", "grass", null, AutoTileModel.Blob16);
            var catalog = NewCatalog(transition, baseGrass);

            var (terrain, reason) = ResolveAutoBrushTerrainLike(catalog, "grass_dirt2");
            Assert.AreEqual("grass", terrain);
            Assert.IsNull(reason);
        }

        [Test]
        public void AcceptedCorner16Terrain_Paints_TilesAndTerrainTogether()
        {
            // The counterpart of the gate test: once the terrain is accepted, the
            // stroke must place sprites AND stamp terrain. A run that stamped
            // terrain but placed zero sprites is the silent no-op this whole
            // feature exists to avoid -- it looks like a broken editor, not like an
            // unconfigured pack.
            var rs = NewRuleset("grass_rock", "grass", "rock", AutoTileModel.Corner16);
            var catalog = NewCatalog(rs);
            var (terrain, reason) = ResolveAutoBrushTerrainLike(catalog, "grass_rock");
            Assert.AreEqual("grass", terrain);
            Assert.IsNull(reason);

            var terrainMap = new TerrainMap();
            var tilemap = NewTilemap();
            var rect = new BoundsInt(0, 0, 0, 2, 2, 1);
            var (edits, metadataEdits) = TerrainPainter.PaintRegion(tilemap, rect, "grass", catalog, terrainMap);

            Assert.AreEqual(4, metadataEdits.Count, "Every cell in the rect records its terrain for undo.");
            Assert.IsNotEmpty(edits,
                "A ruleset with populated slots must place sprites. Empty here means the resolver " +
                "found no tile for the computed corner signature — a silent no-op wearing a success badge.");
        }
    }
}
