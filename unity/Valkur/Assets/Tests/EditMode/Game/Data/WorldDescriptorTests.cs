using NUnit.Framework;
using UnityEngine;
using Valkur.Core.Coordinates;
using Valkur.Data;

namespace Valkur.Tests.EditMode.Game.Data
{
    /// <summary>
    /// Pins the contract of <see cref="WorldDescriptor"/> — the asset Phase 1
    /// keys every world load off. Gettable defaults and deterministic Id
    /// derivation matter because WorldId is the database / save key for
    /// every persistence layer.
    /// </summary>
    [TestFixture]
    public class WorldDescriptorTests
    {
        [Test]
        public void LegacyBase_HasBaseSlugAndConfig()
        {
            var d = WorldDescriptor.CreateLegacyBase();
            try
            {
                Assert.AreEqual("base", d.Slug);
                Assert.AreEqual("Overworld", d.DisplayName);
                Assert.IsNotNull(d.Config, "Legacy base must wire a config so WorldManager can load it.");
                Assert.AreEqual(WorldConfig.LegacyChunkSize, d.Config.ChunkSize);
            }
            finally
            {
                if (d.Config != null) Object.DestroyImmediate(d.Config);
                Object.DestroyImmediate(d);
            }
        }

        [Test]
        public void Id_DerivedFromConfigId_Deterministic()
        {
            var a = WorldDescriptor.CreateLegacyBase();
            var b = WorldDescriptor.CreateLegacyBase();
            try
            {
                // Same slug -> same Guid via WorldConfig.DeterministicGuid;
                // descriptor surfaces that as Id.Value.
                Assert.AreEqual(a.Id.Value, b.Id.Value,
                    "Same slug must yield the same Id across descriptor instances.");
                Assert.AreEqual(a.Id.Slug, b.Id.Slug);
            }
            finally
            {
                if (a.Config != null) Object.DestroyImmediate(a.Config);
                if (b.Config != null) Object.DestroyImmediate(b.Config);
                Object.DestroyImmediate(a);
                Object.DestroyImmediate(b);
            }
        }

        [Test]
        public void Id_FallbackToSlug_WhenConfigNull()
        {
            // A freshly-created descriptor (config not yet wired in inspector)
            // must still surface a non-empty Id so logs and partial flows work.
            var d = ScriptableObject.CreateInstance<WorldDescriptor>();
            try
            {
                Assert.AreEqual("base", d.Id.Slug,
                    "Default slug must be 'base' so an unwired descriptor matches WorldId.Base layout.");
            }
            finally { Object.DestroyImmediate(d); }
        }

        [Test]
        public void DefaultSpawnTile_DefaultsToCenterIsh()
        {
            var d = ScriptableObject.CreateInstance<WorldDescriptor>();
            try
            {
                // 75,75 matches the historical Lobby spawn — keeps existing
                // single-world behaviour byte-equivalent under the descriptor.
                Assert.AreEqual(new Vector2Int(75, 75), d.DefaultSpawnTile);
            }
            finally { Object.DestroyImmediate(d); }
        }
    }
}
