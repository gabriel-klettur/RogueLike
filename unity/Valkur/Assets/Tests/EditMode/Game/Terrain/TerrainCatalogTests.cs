using NUnit.Framework;
using UnityEngine;
using Valkur.Data;

namespace Valkur.Tests.EditMode.Game.Terrain
{
    /// <summary>
    /// Unit tests for <see cref="TerrainCatalog"/>.
    ///
    /// Exercises base-vs-transition lookup, priority resolution, bidirectional
    /// transition matching, and the unique-terrain enumeration that powers the
    /// Auto-tile Region picker chips.
    /// </summary>
    [TestFixture]
    public class TerrainCatalogTests
    {
        private static TilesetRuleset NewRuleset(string folder, string primary, string secondary, int priority)
        {
            var rs = ScriptableObject.CreateInstance<TilesetRuleset>();
            rs.EditorSetMetadata(folder, primary, secondary, priority, AutoTileModel.Blob16);
            return rs;
        }

        [Test]
        public void FindBaseRuleset_NullOrEmpty_ReturnsNull()
        {
            var catalog = ScriptableObject.CreateInstance<TerrainCatalog>();
            try
            {
                Assert.IsNull(catalog.FindBaseRuleset(null));
                Assert.IsNull(catalog.FindBaseRuleset(""));
            }
            finally { Object.DestroyImmediate(catalog); }
        }

        [Test]
        public void FindBaseRuleset_IgnoresTransitionRulesets()
        {
            var catalog = ScriptableObject.CreateInstance<TerrainCatalog>();
            var transition = NewRuleset("grass_dirt", "grass", "dirt", 0);
            try
            {
                catalog.EditorAdd(transition);
                Assert.IsNull(catalog.FindBaseRuleset("grass"),
                    "FindBaseRuleset must skip rulesets that have a secondary terrain.");
            }
            finally
            {
                Object.DestroyImmediate(transition);
                Object.DestroyImmediate(catalog);
            }
        }

        [Test]
        public void FindBaseRuleset_ReturnsHighestPriority()
        {
            var catalog = ScriptableObject.CreateInstance<TerrainCatalog>();
            var lo = NewRuleset("grass_v1", "grass", null, 0);
            var hi = NewRuleset("grass_v2", "grass", null, 10);
            try
            {
                catalog.EditorAdd(lo);
                catalog.EditorAdd(hi);
                Assert.AreSame(hi, catalog.FindBaseRuleset("grass"));
            }
            finally
            {
                Object.DestroyImmediate(lo);
                Object.DestroyImmediate(hi);
                Object.DestroyImmediate(catalog);
            }
        }

        [Test]
        public void FindTransitionRuleset_MatchesBothOrders()
        {
            var catalog = ScriptableObject.CreateInstance<TerrainCatalog>();
            var rs = NewRuleset("grass_dirt", "grass", "dirt", 0);
            try
            {
                catalog.EditorAdd(rs);
                Assert.AreSame(rs, catalog.FindTransitionRuleset("grass", "dirt"));
                Assert.AreSame(rs, catalog.FindTransitionRuleset("dirt", "grass"));
            }
            finally
            {
                Object.DestroyImmediate(rs);
                Object.DestroyImmediate(catalog);
            }
        }

        [Test]
        public void FindTransitionRuleset_NoMatch_ReturnsNull()
        {
            var catalog = ScriptableObject.CreateInstance<TerrainCatalog>();
            var rs = NewRuleset("grass_dirt", "grass", "dirt", 0);
            try
            {
                catalog.EditorAdd(rs);
                Assert.IsNull(catalog.FindTransitionRuleset("grass", "rock"));
            }
            finally
            {
                Object.DestroyImmediate(rs);
                Object.DestroyImmediate(catalog);
            }
        }

        [Test]
        public void FindTransitionRuleset_IgnoresBaseRulesets()
        {
            var catalog = ScriptableObject.CreateInstance<TerrainCatalog>();
            var baseRs = NewRuleset("grass", "grass", null, 0);
            try
            {
                catalog.EditorAdd(baseRs);
                Assert.IsNull(catalog.FindTransitionRuleset("grass", "dirt"),
                    "FindTransitionRuleset must skip rulesets with no secondary terrain.");
            }
            finally
            {
                Object.DestroyImmediate(baseRs);
                Object.DestroyImmediate(catalog);
            }
        }

        [Test]
        public void GetUniqueTerrains_DeDupesAcrossRulesets()
        {
            var catalog = ScriptableObject.CreateInstance<TerrainCatalog>();
            var grass = NewRuleset("grass", "grass", null, 0);
            var grassDirt = NewRuleset("grass_dirt", "grass", "dirt", 0);
            var sandOcean = NewRuleset("sand_ocean", "sand", "ocean", 0);
            try
            {
                catalog.EditorAdd(grass);
                catalog.EditorAdd(grassDirt);
                catalog.EditorAdd(sandOcean);

                var terrains = new System.Collections.Generic.HashSet<string>(catalog.GetUniqueTerrains());
                Assert.That(terrains, Is.EquivalentTo(new[] { "grass", "dirt", "sand", "ocean" }));
            }
            finally
            {
                Object.DestroyImmediate(grass);
                Object.DestroyImmediate(grassDirt);
                Object.DestroyImmediate(sandOcean);
                Object.DestroyImmediate(catalog);
            }
        }

        [Test]
        public void EditorRemove_DropsRuleset()
        {
            var catalog = ScriptableObject.CreateInstance<TerrainCatalog>();
            var rs = NewRuleset("grass", "grass", null, 0);
            try
            {
                catalog.EditorAdd(rs);
                catalog.EditorRemove(rs);
                Assert.IsNull(catalog.FindBaseRuleset("grass"));
            }
            finally
            {
                Object.DestroyImmediate(rs);
                Object.DestroyImmediate(catalog);
            }
        }
    }
}
