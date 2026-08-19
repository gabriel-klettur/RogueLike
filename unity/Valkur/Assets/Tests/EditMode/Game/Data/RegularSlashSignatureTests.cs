using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Valkur.Data;
using Valkur.Gameplay.Spells;

namespace Valkur.Tests.EditMode.Game.Data
{
    /// <summary>
    /// Protects slash_regular as a distinct, player-only precision melee attack.
    /// Its authored sector, anticipation and code-native feedback are intentional;
    /// falling back to the legacy straight prefab would break that identity.
    /// </summary>
    [TestFixture]
    public class RegularSlashSignatureTests
    {
        private const string SpellCatalogPath =
            "Assets/_Project/Data/Catalogs/SpellCatalog.asset";

        private SpellDefinition _spell;

        [SetUp]
        public void SetUp()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<SpellCatalog>(SpellCatalogPath);
            Assert.IsNotNull(catalog, "SpellCatalog is missing.");

            var matches = catalog.AllSpells
                .Where(s => s != null && s.spellKey == RegularSlashAttack.SpellKey)
                .ToArray();
            Assert.AreEqual(1, matches.Length,
                "slash_regular must be registered exactly once in SpellCatalog.");
            _spell = matches[0];
        }

        [Test]
        public void IsAPlayerOnlySlash()
        {
            Assert.AreEqual(SpellType.Slash, _spell.type);
            Assert.AreEqual(SpellAudience.Player, _spell.audience,
                "The spell must appear only in the PLAYERS tab.");
            Assert.AreEqual(0f, _spell.manaCost);
        }

        [Test]
        public void HasReadableAnticipationActiveAndRecoveryBeats()
        {
            Assert.GreaterOrEqual(_spell.prepareDuration, 0.06f,
                "A high-quality melee attack needs a readable wind-up.");
            Assert.GreaterOrEqual(_spell.lifetime, RegularSlashAttack.MinimumTotalDuration);
            Assert.Greater(_spell.cooldownDuration, _spell.lifetime,
                "The cooldown should leave a short recovery beat after the visual finishes.");
            Assert.IsTrue(_spell.lockCastDirection,
                "The authored sweep and its damage sector must keep one facing direction.");
        }

        [Test]
        public void VisualSectorAndDamageSectorUseTheSameAuthoredShape()
        {
            Assert.AreEqual(_spell.radius, _spell.hitRadius, 0.0001f);
            Assert.AreEqual(_spell.arcRangeDegrees, _spell.hitArcDegrees, 0.0001f);
            Assert.AreEqual(100f, _spell.arcRangeDegrees, 0.0001f);
            Assert.GreaterOrEqual(_spell.hitRadius, 2.5f);
        }

        [Test]
        public void UsesItsCodeNativeCrescent_NotTheLegacyStraightPrefab()
        {
            Assert.IsEmpty(_spell.CollectVfxPresets());
            Assert.IsEmpty(_spell.CollectImpactPresets());
            Assert.IsEmpty(_spell.CollectCastPresets());
            Assert.IsTrue(RegularSlashAttack.Matches(_spell));
        }

        [Test]
        public void PaletteReadsAsAWhiteHotSteelEdgeWithCoolAtmosphere()
        {
            Assert.GreaterOrEqual(_spell.particleColor.b, 0.95f);
            Assert.GreaterOrEqual(_spell.particleColor.g, 0.75f);
            Assert.Greater(_spell.particleColor.b, _spell.particleColor.r);
        }

        [Test]
        public void SectorPredicateMatchesTheVisibleHundredDegreeCrescent()
        {
            Vector2 origin = new Vector2(3f, -2f);
            Vector2 forward = Vector2.right;
            float radius = _spell.hitRadius;
            float halfArc = _spell.arcRangeDegrees * 0.5f;

            Assert.IsTrue(RegularSlashAttack.IsInsideSector(
                origin, forward, origin + Vector2.right * radius, radius, _spell.arcRangeDegrees));

            Vector2 boundary = Quaternion.Euler(0f, 0f, halfArc) * Vector2.right;
            Assert.IsTrue(RegularSlashAttack.IsInsideSector(
                origin, forward, origin + boundary * (radius * 0.8f), radius, _spell.arcRangeDegrees));

            Vector2 outsideArc = Quaternion.Euler(0f, 0f, halfArc + 3f) * Vector2.right;
            Assert.IsFalse(RegularSlashAttack.IsInsideSector(
                origin, forward, origin + outsideArc, radius, _spell.arcRangeDegrees));

            Assert.IsFalse(RegularSlashAttack.IsInsideSector(
                origin, forward, origin + Vector2.right * (radius + 0.05f), radius,
                _spell.arcRangeDegrees));
            Assert.IsFalse(RegularSlashAttack.IsInsideSector(
                origin, forward, origin + Vector2.left, radius, _spell.arcRangeDegrees));
        }
    }
}
