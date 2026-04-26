using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Tilemaps;
using Valkur.Gameplay.TileEditor;

namespace Valkur.Tests.EditMode.Editors.TileEditor.Catalog
{
    /// <summary>
    /// Tests for the runtime tile catalog + registry pair.
    /// Validates: ScriptableObject API contracts (categories, lookups by category),
    /// TileRegistry name↔tile bidirectional mapping, fallbacks for tiles created
    /// outside the catalog (e.g. dynamic OverlayLoader tiles), and resilience to
    /// null/empty inputs.
    /// </summary>
    [TestFixture]
    public class TileCatalogAndRegistryTests
    {
        private readonly List<Object> _trash = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _trash) if (o != null) Object.DestroyImmediate(o);
            _trash.Clear();
            // Reset the registry so cross-test pollution can't happen.
            TileRegistry.Instance.Load(null);
        }

        private Sprite MakeSprite(string name)
        {
            var tex = new Texture2D(1, 1);
            tex.SetPixel(0, 0, Color.white);
            tex.Apply();
            var s = Sprite.Create(tex, new Rect(0, 0, 1, 1), Vector2.one * 0.5f, 1f);
            s.name = name;
            _trash.Add(tex);
            _trash.Add(s);
            return s;
        }

        private Tile MakeTile(string name)
        {
            var t = ScriptableObject.CreateInstance<Tile>();
            t.name = name;
            t.sprite = MakeSprite(name);
            _trash.Add(t);
            return t;
        }

        private TileCatalog BuildCatalog(params (string cat, string tileName)[] entries)
        {
            var cat = ScriptableObject.CreateInstance<TileCatalog>();
            _trash.Add(cat);

            var list = new List<TileCatalog.TileEntry>();
            foreach (var (category, tn) in entries)
            {
                var tile = MakeTile(tn);
                list.Add(new TileCatalog.TileEntry
                {
                    category = category,
                    tileName = tn,
                    tile = tile,
                    preview = tile.sprite,
                });
            }

#if UNITY_EDITOR
            cat.PopulateFromAssets(list);
#else
            // Reflection fallback for builds without UNITY_EDITOR define
            var field = typeof(TileCatalog).GetField("entries",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            field.SetValue(cat, list);
#endif
            return cat;
        }

        // ── TileCatalog ─────────────────────────────────────────────────

        [Test]
        public void GetCategories_ReturnsUniqueOrderedCategories()
        {
            var cat = BuildCatalog(
                ("grass", "g1"),
                ("grass", "g2"),
                ("sand",  "s1"),
                ("rock",  "r1"));

            var categories = cat.GetCategories();

            Assert.AreEqual(3, categories.Count, "Duplicate categories must be deduped.");
            CollectionAssert.AreEqual(new[] { "grass", "sand", "rock" }, categories,
                "Categories must preserve first-seen order.");
        }

        [Test]
        public void GetTilesForCategory_ReturnsOnlyMatchingEntries()
        {
            var cat = BuildCatalog(
                ("grass", "g1"),
                ("grass", "g2"),
                ("sand",  "s1"));

            var grass = cat.GetTilesForCategory("grass");
            var sand  = cat.GetTilesForCategory("sand");
            var none  = cat.GetTilesForCategory("missing");

            Assert.AreEqual(2, grass.Count);
            Assert.AreEqual(1, sand.Count);
            Assert.AreEqual(0, none.Count, "Unknown category must return empty list, not null.");
        }

        // ── TileRegistry ────────────────────────────────────────────────

        [Test]
        public void Registry_Load_PopulatesNameLookup()
        {
            var cat = BuildCatalog(("grass", "grass_a"), ("sand", "sand_a"));
            TileRegistry.Instance.Load(cat);

            Assert.IsTrue(TileRegistry.Instance.IsLoaded);
            Assert.IsNotNull(TileRegistry.Instance.GetTile("grass_a"));
            Assert.IsNotNull(TileRegistry.Instance.GetTile("sand_a"));
            Assert.IsNull(TileRegistry.Instance.GetTile("missing"));
        }

        [Test]
        public void Registry_GetName_ReturnsCanonicalName()
        {
            var cat = BuildCatalog(("grass", "grass_a"));
            TileRegistry.Instance.Load(cat);

            var tile = TileRegistry.Instance.GetTile("grass_a");
            Assert.AreEqual("grass_a", TileRegistry.Instance.GetName(tile));
        }

        [Test]
        public void Registry_GetName_FallsBackToTileObjectName_ForUnregisteredTile()
        {
            // Tile that was NEVER registered (mirrors OverlayLoader creating ad-hoc tiles).
            var loose = MakeTile("dynamic_floor");
            TileRegistry.Instance.Load(null);

            Assert.AreEqual("dynamic_floor", TileRegistry.Instance.GetName(loose),
                "GetName must fall back to the Tile.name when not registered.");
        }

        [Test]
        public void Registry_GetName_FallsBackToSpriteName_WhenTileNameEmpty()
        {
            var t = ScriptableObject.CreateInstance<Tile>();
            _trash.Add(t);
            t.name = string.Empty;
            t.sprite = MakeSprite("sprite_label");

            Assert.AreEqual("sprite_label", TileRegistry.Instance.GetName(t),
                "GetName must fall back to sprite.name when tile.name is empty.");
        }

        [Test]
        public void Registry_GetName_NullTile_ReturnsNull()
        {
            Assert.IsNull(TileRegistry.Instance.GetName(null));
        }

        [Test]
        public void Registry_Register_AddsBidirectionalMapping()
        {
            TileRegistry.Instance.Load(null);
            var t = MakeTile("manual");
            TileRegistry.Instance.Register("manual", t);

            Assert.AreSame(t, TileRegistry.Instance.GetTile("manual"));
            Assert.AreEqual("manual", TileRegistry.Instance.GetName(t));
        }

        [Test]
        public void Registry_Register_NullOrEmptyName_IsIgnored()
        {
            TileRegistry.Instance.Load(null);
            var t = MakeTile("x");

            Assert.DoesNotThrow(() => TileRegistry.Instance.Register(null, t));
            Assert.DoesNotThrow(() => TileRegistry.Instance.Register("",   t));
            Assert.DoesNotThrow(() => TileRegistry.Instance.Register("k",  null));

            Assert.IsNull(TileRegistry.Instance.GetTile(""));
            Assert.IsNull(TileRegistry.Instance.GetTile("k"));
        }

        [Test]
        public void Registry_Load_WithDuplicateNames_KeepsFirstOccurrence()
        {
            var cat = BuildCatalog(("a", "dup"), ("b", "dup"));
            TileRegistry.Instance.Load(cat);

            // Both entries share name "dup" — first one wins.
            var first = cat.Entries[0].tile;
            Assert.AreSame(first, TileRegistry.Instance.GetTile("dup"));
        }
    }
}
