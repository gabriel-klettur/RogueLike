using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Tilemaps;
using Valkur.Data;
using Valkur.Gameplay.TileEditor;

namespace Valkur.Tests.EditMode.Editors.TileEditor.Brush
{
    /// <summary>
    /// Drives the REAL <c>TileEditorManager.ResolveAutoBrushTerrain</c> against synthetic
    /// <see cref="TerrainCatalog"/> / <see cref="TilesetRuleset"/> objects.
    ///
    /// <para><b>It used to reproduce that method's body instead, and that is how a live
    /// defect shipped.</b> A copy of production logic inside a test compares one half of
    /// the project against itself: when the real lookup changed from matching a folder
    /// NAME to reading the pack off the selected SPRITE, the copy kept answering the old
    /// question and every test here stayed green while the AUTO brush was dead on seven
    /// picker tabs. The helper's own comment had already recorded the same trap happening
    /// once before, with FindBaseRuleset. Calling the shipped method is the only version
    /// of this fixture that can fail for the right reason.</para>
    ///
    /// <para>Two seams make that possible in EditMode. A component added from script never
    /// receives <c>Awake</c>, so the manager can be hosted for a pure lookup without a Grid
    /// or a WorldGridBuilder; and <see cref="TerrainCatalogLoader"/>'s cache is written
    /// directly so the production path reads THIS catalog instead of the shipped asset. The
    /// cache is a global, so <c>TearDown</c> already invalidates it — a probe that changes
    /// global editor state and does not restore it has changed it for every test that
    /// follows.</para>
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

        private static readonly FieldInfo CatalogCache = typeof(TerrainCatalogLoader)
            .GetField("_cached", BindingFlags.NonPublic | BindingFlags.Static);

        /// <summary>
        /// Calls the shipped resolver with <paramref name="catalog"/> in place of the
        /// project asset and <paramref name="selected"/> as the picked tile.
        /// </summary>
        private (TilesetRuleset Ruleset, string Terrain, string Reason) Resolve(
            TerrainCatalog catalog, Sprite selected)
        {
            Assert.IsNotNull(CatalogCache, "TerrainCatalogLoader._cached is the injection seam.");
            CatalogCache.SetValue(null, catalog);

            var host = new GameObject(nameof(AutoBrushTerrainResolutionTests));
            _created.Add(host);
            var manager = host.AddComponent<TileEditorManager>();

            if (selected != null)
            {
                var tile = ScriptableObject.CreateInstance<Tile>();
                _scriptableObjects.Add(tile);
                tile.sprite = selected;
                manager.State.SelectedTile = tile;
            }

            var method = typeof(TileEditorManager).GetMethod(
                "ResolveAutoBrushTerrain", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(method,
                "ResolveAutoBrushTerrain is what the AUTO toggle and every AUTO stroke ask. " +
                "If it moved, point this fixture at the new owner — never re-implement it here.");

            object boxed = method.Invoke(manager, null);
            var t = boxed.GetType();
            return ((TilesetRuleset)t.GetField("Item1").GetValue(boxed),
                    (string)t.GetField("Item2").GetValue(boxed),
                    (string)t.GetField("Item3").GetValue(boxed));
        }

        /// <summary>A sprite the pack itself holds — the solid-primary tile for a Corner16
        /// pack, or any populated slot for a Blob16 one.</summary>
        private static Sprite PureTileOf(TilesetRuleset ruleset)
        {
            var fromCorners = ruleset.CornerSlots
                .Where(c => (byte)c.slot == 0 && c.variants != null)
                .SelectMany(c => c.variants).FirstOrDefault(v => v != null);
            if (fromCorners != null) return fromCorners;

            return ruleset.Slots
                .Where(b => b.variants != null)
                .SelectMany(b => b.variants).FirstOrDefault(v => v != null);
        }

        [Test]
        public void EmptyCategory_ReturnsNoTileSelectedHint()
        {
            var catalog = NewCatalog();
            var (ruleset, terrain, reason) = Resolve(catalog, null);
            Assert.IsNull(ruleset);
            Assert.IsNull(terrain);
            Assert.AreEqual(TileEditorConstants.NoTileSelectedHint, reason);
        }

        [Test]
        public void CategoryMatchesNoRuleset_ReturnsNoRulesetHint()
        {
            // A tile no registered pack holds. The lookup must refuse it rather than
            // fall back on anything — a wrong pack paints the wrong terrain silently.
            var orphan = NewRuleset("unregistered_pack", "grass", "dirt", AutoTileModel.Corner16);
            var catalog = NewCatalog();
            var (ruleset, terrain, reason) = Resolve(catalog, PureTileOf(orphan));
            Assert.IsNull(ruleset);
            Assert.IsNull(terrain);
            Assert.AreEqual(TileEditorConstants.NoRulesetForCategoryHint, reason);
        }

        [Test]
        public void CategoryMatchesRulesetWithEmptyPrimary_ReturnsNoRulesetHint()
        {
            var rs = NewRuleset("broken_pack", "", null, AutoTileModel.Blob16);
            var catalog = NewCatalog(rs);
            var (ruleset, terrain, reason) = Resolve(catalog, PureTileOf(rs));
            Assert.IsNull(terrain, "A pack that names no terrain can paint nothing.");
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
            var (resolved, terrain, reason) = Resolve(catalog, PureTileOf(rs));
            Assert.AreSame(rs, resolved);
            Assert.AreEqual("grass", terrain, "A Corner16 pack must be paintable — it is the whole point of the model.");
            Assert.IsNull(reason);

            // And its OTHER terrain must be reachable from its own solid tile, or an author
            // can only ever lay down one side of the boundary the pack exists to draw.
            var secondaryTile = rs.CornerSlots
                .Where(c => (byte)c.slot == 15 && c.variants != null)
                .SelectMany(c => c.variants).FirstOrDefault(v => v != null);
            Assert.AreEqual("dirt", Resolve(catalog, secondaryTile).Terrain);
        }

        [Test]
        public void CategoryMatchesABaseRuleset_ReturnsPrimaryTerrainSuccessfully()
        {
            var rs = NewRuleset("solid_grass", "grass", null, AutoTileModel.Blob16);
            var catalog = NewCatalog(rs);
            var (resolved, terrain, reason) = Resolve(catalog, PureTileOf(rs));
            Assert.AreSame(rs, resolved);
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

            var (resolved, terrain, reason) = Resolve(catalog, PureTileOf(transition));
            Assert.AreSame(transition, resolved,
                "The pack the author clicked must win, not whichever ruleset happens to own " +
                "the terrain NAME — that name lookup is what made shadowed packs unreachable.");
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
            var (resolved, terrain, reason) = Resolve(catalog, PureTileOf(rs));
            Assert.AreSame(rs, resolved);
            Assert.AreEqual("grass", terrain);
            Assert.IsNull(reason);

            var terrainMap = new TerrainMap();
            var tilemap = NewTilemap();
            var rect = new BoundsInt(0, 0, 0, 2, 2, 1);
            var (edits, metadataEdits) = TerrainPainter.PaintRegion(tilemap, rect, "grass", catalog, terrainMap);

            // Corner16 terrain is keyed by VERTEX, and a 2x2 rect of cells is bounded by
            // 3x3 of them. The far corners belong to the stroke as much as the near ones —
            // leaving them out is what would make the region's own outer row read as a
            // boundary against itself.
            Assert.AreEqual(9, metadataEdits.Count,
                "Every vertex the rect spans records its terrain for undo.");
            Assert.IsNotEmpty(edits,
                "A ruleset with populated slots must place sprites. Empty here means the resolver " +
                "found no tile for the computed corner signature — a silent no-op wearing a success badge.");
        }
    }
}
