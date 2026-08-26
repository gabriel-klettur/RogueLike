using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using Valkur.Data;

namespace Valkur.Tests.EditMode.Editors.TileEditor.Catalog
{
    /// <summary>
    /// Guards the rule that makes an auto-tile pack REACHABLE.
    ///
    /// <c>TerrainCatalog.FindPaintRuleset</c> resolves a terrain NAME to exactly one
    /// Corner16 ruleset — highest <c>Priority</c>, ties broken by list order. Two packs
    /// claiming the same primary terrain therefore means one of them can never be selected
    /// from the F8 auto-brush, and nothing anywhere reports it: the pack imports, its 16
    /// slots fill, its sprites sit in Resources, and the brush just keeps painting the
    /// other one.
    ///
    /// This is not hypothetical. <c>rock_lava</c> was authored with primary <c>rock</c>,
    /// which <c>rock_water</c> already claimed, and was silently unreachable until it was
    /// renamed to <c>stone</c> — a fair name either way, since rock_water's rock is smooth
    /// dark #3c3c3c and this one is pale loose rubble #848484.
    ///
    /// <see cref="Corner16ManifestFivePacksResolveTests"/> validates each pack's slot table.
    /// This validates the catalog they all share.
    /// </summary>
    public class TerrainNameUniquenessTests
    {
        private static TerrainCatalog Catalog()
        {
            var catalog = Resources.Load<TerrainCatalog>("TerrainCatalog");
            Assert.IsNotNull(catalog, "TerrainCatalog is not loadable from Resources.");
            return catalog;
        }

        private static IEnumerable<TilesetRuleset> Corner16Rulesets()
            => Catalog().Rulesets.Where(r => r != null && r.Model == AutoTileModel.Corner16);

        /// <summary>
        /// Packs that were ALREADY unreachable before this rule was written down, and are
        /// left that way deliberately: deciding whether 'grass' belongs to grass_dirt or
        /// grass_rock is an art call, not a test's to make. They are named here so the
        /// assertions below still fail on a THIRD clash instead of being switched off, and
        /// so whoever resolves them has the list in front of them.
        ///
        /// To fix one: give the pack a distinct primary terrain (what rock_lava did, taking
        /// 'stone' rather than fighting rock_water for 'rock'), or raise its Priority above
        /// the pack currently winning the name.
        /// </summary>
        private static readonly string[] KnownUnreachable = { "grass_rock", "sand_rock" };

        [Test]
        public void NoTwoCorner16Packs_ClaimTheSamePrimaryTerrain()
        {
            var clashes = Corner16Rulesets()
                .Where(r => !string.IsNullOrEmpty(r.TerrainPrimary))
                .GroupBy(r => r.TerrainPrimary)
                .Where(g => g.Select(r => r.Priority).Distinct().Count() < g.Count())
                // A group is already accounted for once every loser in it is a known one.
                .Where(g => g.Any(r => !KnownUnreachable.Contains(r.FolderName)
                                       && Catalog().FindPaintRuleset(r.TerrainPrimary) != r))
                .Select(g => $"'{g.Key}' claimed by [{string.Join(", ", g.Select(r => r.FolderName))}] " +
                             "at equal Priority")
                .ToList();

            Assert.That(clashes, Is.Empty,
                "Packs sharing a primary terrain at the same Priority — all but one are " +
                "unreachable from the auto-brush: " + string.Join("; ", clashes));
        }

        [Test]
        public void EveryCorner16Pack_IsReachableByItsOwnPrimaryTerrain()
        {
            TerrainCatalog catalog = Catalog();
            var unreachable = new List<string>();

            foreach (TilesetRuleset r in Corner16Rulesets())
            {
                if (string.IsNullOrEmpty(r.TerrainPrimary)) continue;
                if (KnownUnreachable.Contains(r.FolderName)) continue;

                TilesetRuleset resolved = catalog.FindPaintRuleset(r.TerrainPrimary);
                if (resolved != r)
                    unreachable.Add($"{r.FolderName} (primary '{r.TerrainPrimary}') resolves to " +
                                    $"'{resolved?.FolderName ?? "null"}'");
            }

            Assert.That(unreachable, Is.Empty,
                "Packs the auto-brush can never select: " + string.Join("; ", unreachable));
        }

        [Test]
        public void EveryCorner16Pack_NamesBothItsTerrains()
        {
            var unnamed = Corner16Rulesets()
                .Where(r => string.IsNullOrEmpty(r.TerrainPrimary) || string.IsNullOrEmpty(r.TerrainSecondary))
                .Select(r => $"{r.FolderName} (primary '{r.TerrainPrimary}', secondary '{r.TerrainSecondary}')")
                .ToList();

            // TerrainTileResolver.ResolveVariantForCell keys corner slots by TerrainSecondary,
            // so an unnamed ruleset auto-tiles nothing however complete its slot table is.
            Assert.That(unnamed, Is.Empty,
                "Rulesets with an unnamed terrain: " + string.Join("; ", unnamed));
        }

        [Test]
        public void APackNeverTransitionsATerrainIntoItself()
        {
            var degenerate = Corner16Rulesets()
                .Where(r => !string.IsNullOrEmpty(r.TerrainPrimary) &&
                            r.TerrainPrimary == r.TerrainSecondary)
                .Select(r => r.FolderName)
                .ToList();

            Assert.That(degenerate, Is.Empty,
                "Rulesets whose two terrains are the same — every corner mask resolves to " +
                "the same slot: " + string.Join(", ", degenerate));
        }

        [Test]
        public void TheKnownUnreachableListIsStillAccurate()
        {
            // A shrinking list is good news, but it must be recorded here or the packs above
            // stay permanently exempt from the rule they are now keeping.
            TerrainCatalog catalog = Catalog();
            var fixedSince = new List<string>();

            foreach (string name in KnownUnreachable)
            {
                TilesetRuleset pack = Corner16Rulesets().FirstOrDefault(r => r.FolderName == name);
                if (pack == null) continue;      // deleted or renamed; not this test's business
                if (catalog.FindPaintRuleset(pack.TerrainPrimary) == pack)
                    fixedSince.Add(name);
            }

            Assert.That(fixedSince, Is.Empty,
                "These packs are reachable again — remove them from KnownUnreachable so the " +
                "rule starts holding them: " + string.Join(", ", fixedSince));
        }

        [Test]
        public void RockLava_IsRegisteredAndReachableAsStoneOverLava()
        {
            TerrainCatalog catalog = Catalog();

            TilesetRuleset pack = Corner16Rulesets().FirstOrDefault(r => r.FolderName == "rock_lava");
            Assert.IsNotNull(pack, "rock_lava is not in TerrainCatalog as a Corner16 ruleset.");

            Assert.AreEqual("stone", pack.TerrainPrimary,
                "rock_lava paints as 'stone'; 'rock' belongs to rock_water and would make " +
                "this pack unreachable.");
            Assert.AreEqual("lava", pack.TerrainSecondary);

            Assert.AreSame(pack, catalog.FindPaintRuleset("stone"));
            CollectionAssert.Contains(catalog.GetUniqueTerrains().ToList(), "lava");
        }
    }
}
