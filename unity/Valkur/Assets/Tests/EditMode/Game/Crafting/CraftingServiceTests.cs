using NUnit.Framework;
using UnityEngine;
using Valkur.Data;
using Valkur.Gameplay.Crafting;

namespace Valkur.Tests.EditMode.Game.Crafting
{
    /// <summary>
    /// The crafting rules, and specifically the ones that would destroy a player's materials
    /// if they were wrong.
    ///
    /// <para>Built entirely from synthetic assets rather than the shipped catalog. A rule test
    /// that reads real data fails when a designer retunes a recipe, which trains everyone to
    /// ignore it — the shipped data has its own fixture
    /// (<see cref="ShippedCraftingDataTests"/>) that asks a different question.</para>
    /// </summary>
    [TestFixture]
    public class CraftingServiceTests
    {
        private GameObject _go;
        private Valkur.Gameplay.Inventory.Inventory _bag;
        private PlayerProfessions _professions;
        private ProfessionDefinition _trade;
        private ItemDefinition _plank;
        private ItemDefinition _nail;
        private ItemDefinition _chair;
        private RecipeDefinition _recipe;

        [SetUp]
        public void SetUp()
        {
            _go = new GameObject("CraftFixture");
            _bag = _go.AddComponent<Valkur.Gameplay.Inventory.Inventory>();
            _bag.Initialize(10);
            _professions = _go.AddComponent<PlayerProfessions>();

            _trade = ScriptableObject.CreateInstance<ProfessionDefinition>();
            _trade.professionKey = "testing";
            _trade.displayName = "Testing";
            _trade.maxLevel = 5;
            _trade.baseXpPerLevel = 10;
            _trade.xpGrowth = 1f;

            _plank = MakeItem("plank", 20);
            _nail = MakeItem("nail", 20);
            _chair = MakeItem("chair", 5);

            _recipe = ScriptableObject.CreateInstance<RecipeDefinition>();
            _recipe.recipeId = "chair";
            _recipe.displayName = "Chair";
            _recipe.profession = _trade;
            _recipe.output = _chair;
            _recipe.outputQuantity = 1;
            _recipe.xpReward = 5;
            _recipe.ingredients = new[]
            {
                new RecipeIngredient(_plank, 2),
                new RecipeIngredient(_nail, 3),
            };
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_go);
            Object.DestroyImmediate(_trade);
            Object.DestroyImmediate(_plank);
            Object.DestroyImmediate(_nail);
            Object.DestroyImmediate(_chair);
            Object.DestroyImmediate(_recipe);
        }

        private static ItemDefinition MakeItem(string id, int stack)
        {
            var item = ScriptableObject.CreateInstance<ItemDefinition>();
            item.itemId = id;
            item.displayName = id;
            item.stackable = true;
            item.maxStack = stack;
            return item;
        }

        // ── Availability ────────────────────────────────────────────────────

        [Test]
        public void EmptyBag_ReportsEveryShortfall_NotJustTheFirst()
        {
            var a = CraftingService.Evaluate(_bag, _recipe, _professions, false);

            Assert.AreEqual(CraftBlockReason.MissingIngredients, a.Reason);
            // Both, not one. A panel that names a single missing ingredient makes the player
            // walk back after every trip to learn what else they need.
            Assert.AreEqual(2, a.Shortfalls.Count);
            Assert.AreEqual(2, a.Shortfalls[0].Missing);
            Assert.AreEqual(3, a.Shortfalls[1].Missing);
        }

        [Test]
        public void PartialStock_ReportsOnlyWhatIsShort()
        {
            _bag.AddItem(_plank, 2);
            var a = CraftingService.Evaluate(_bag, _recipe, _professions, false);

            Assert.AreEqual(1, a.Shortfalls.Count);
            Assert.AreEqual(_nail, a.Shortfalls[0].Item);
            Assert.AreEqual(3, a.Shortfalls[0].Missing);
        }

        [Test]
        public void LevelIsCheckedBeforeIngredients()
        {
            // Both are wrong at once. Level must win: it is the only refusal the player cannot
            // fix by walking somewhere or picking something up, so reporting the ingredients
            // first sends them to gather materials they still could not use.
            _recipe.requiredLevel = 3;
            var a = CraftingService.Evaluate(_bag, _recipe, _professions, false);

            Assert.AreEqual(CraftBlockReason.LevelTooLow, a.Reason);
            Assert.AreEqual(3, a.RequiredLevel);
        }

        [Test]
        public void IngredientsAreCheckedBeforeTheStation()
        {
            // "You are short two planks" is actionable anywhere; "find a workbench" sends the
            // player to a workbench they still cannot use.
            _recipe.requiresStation = true;
            var a = CraftingService.Evaluate(_bag, _recipe, _professions, false);

            Assert.AreEqual(CraftBlockReason.MissingIngredients, a.Reason);
        }

        [Test]
        public void StationRecipe_IsRefusedWithoutOne_AndAllowedWithOne()
        {
            _recipe.requiresStation = true;
            _bag.AddItem(_plank, 2);
            _bag.AddItem(_nail, 3);

            Assert.AreEqual(CraftBlockReason.NeedsStation,
                CraftingService.Evaluate(_bag, _recipe, _professions, false).Reason);
            Assert.IsTrue(CraftingService.Evaluate(_bag, _recipe, _professions, true).CanCraft);
        }

        [Test]
        public void NullProfessions_ReadsAsStartingLevel_NeverAsARefusal()
        {
            // A missing component must not silently lock the whole system. Level 1 recipes stay
            // craftable; only a genuine level requirement refuses.
            _bag.AddItem(_plank, 2);
            _bag.AddItem(_nail, 3);
            Assert.IsTrue(CraftingService.Evaluate(_bag, _recipe, null, false).CanCraft);

            _recipe.requiredLevel = 2;
            Assert.AreEqual(CraftBlockReason.LevelTooLow,
                CraftingService.Evaluate(_bag, _recipe, null, false).Reason);
        }

        [Test]
        public void MalformedRecipe_IsRefusedRatherThanCrafted()
        {
            _recipe.output = null;
            _bag.AddItem(_plank, 2);
            _bag.AddItem(_nail, 3);

            Assert.AreEqual(CraftBlockReason.Malformed,
                CraftingService.Evaluate(_bag, _recipe, _professions, false).Reason);
            Assert.IsFalse(CraftingService.TryCraft(_bag, _recipe, _professions, false));
            Assert.AreEqual(2, _bag.GetItemCount(_plank), "a refused craft must consume nothing");
        }

        // ── Crafting and atomicity ──────────────────────────────────────────

        [Test]
        public void Craft_ConsumesExactly_AndYieldsTheOutput()
        {
            _bag.AddItem(_plank, 5);
            _bag.AddItem(_nail, 7);

            Assert.IsTrue(CraftingService.TryCraft(_bag, _recipe, _professions, false));
            Assert.AreEqual(3, _bag.GetItemCount(_plank));
            Assert.AreEqual(4, _bag.GetItemCount(_nail));
            Assert.AreEqual(1, _bag.GetItemCount(_chair));
        }

        [Test]
        public void RefusedCraft_ConsumesNothing()
        {
            _bag.AddItem(_plank, 2);
            Assert.IsFalse(CraftingService.TryCraft(_bag, _recipe, _professions, false));
            Assert.AreEqual(2, _bag.GetItemCount(_plank),
                "the ingredients the player did have must survive a refusal");
        }

        /// <summary>
        /// THE ONE THAT MATTERS. A craft that takes the ingredients and then cannot place the
        /// result has destroyed the player's materials.
        ///
        /// <para>Reaching it needs a bag that is genuinely full AND a removal that frees no
        /// slot, which is why the ingredients sit on deep stacks: taking 2 off a stack of 20
        /// leaves the slot occupied, so the chair has nowhere to go.</para>
        /// </summary>
        [Test]
        public void FullBag_RollsBackEveryIngredient_AndYieldsNothing()
        {
            _bag.Initialize(2);
            _bag.AddItem(_plank, 20);
            _bag.AddItem(_nail, 20);
            Assert.IsTrue(_bag.IsFull, "fixture must actually fill the bag");

            Assert.IsFalse(CraftingService.TryCraft(_bag, _recipe, _professions, false));

            Assert.AreEqual(20, _bag.GetItemCount(_plank));
            Assert.AreEqual(20, _bag.GetItemCount(_nail));
            Assert.AreEqual(0, _bag.GetItemCount(_chair));
        }

        [Test]
        public void RolledBackCraft_GrantsNoExperience()
        {
            // Otherwise a player with a full bag farms levels off a button that produces
            // nothing.
            _bag.Initialize(2);
            _bag.AddItem(_plank, 20);
            _bag.AddItem(_nail, 20);

            CraftingService.TryCraft(_bag, _recipe, _professions, false);

            Assert.AreEqual(0, _professions.GetProgress(_trade).Xp);
            Assert.AreEqual(PlayerProfessions.STARTING_LEVEL, _professions.GetLevel("testing"));
        }

        [Test]
        public void SuccessfulCraft_GrantsExperience()
        {
            _bag.AddItem(_plank, 2);
            _bag.AddItem(_nail, 3);

            Assert.IsTrue(CraftingService.TryCraft(_bag, _recipe, _professions, false));
            Assert.AreEqual(5, _professions.GetProgress(_trade).Xp);
        }

        [Test]
        public void MaxBatches_IsBoundedByTheScarcestIngredient()
        {
            _bag.AddItem(_plank, 10);   // enough for 5
            _bag.AddItem(_nail, 7);     // enough for 2
            Assert.AreEqual(2, CraftingService.MaxBatches(_bag, _recipe));
        }

        // ── Progression ─────────────────────────────────────────────────────

        [Test]
        public void UnknownTrade_StartsAtOne_NeverZero()
        {
            // Zero would refuse every recipe carrying the default requiredLevel of 1, making
            // the whole system inert on a fresh character, silently.
            Assert.AreEqual(1, _professions.GetLevel("a-trade-never-practised"));
        }

        [Test]
        public void OneGrant_CanCrossSeveralLevels()
        {
            // Flat curve at 10 xp a level: 35 xp is three levels and a remainder.
            _professions.AddXp(_trade, 35);
            var p = _professions.GetProgress(_trade);

            Assert.AreEqual(4, p.Level);
            Assert.AreEqual(5, p.Xp);
        }

        [Test]
        public void CappedTrade_BanksNoExperience_AndReportsFull()
        {
            _professions.AddXp(_trade, 99999);
            var p = _professions.GetProgress(_trade);

            Assert.AreEqual(_trade.maxLevel, p.Level);
            Assert.IsTrue(p.IsMaxed);
            Assert.AreEqual(0, p.Xp, "experience on a level that can never be spent shows a bar "
                                     + "creeping up behind a number that cannot move");
            Assert.AreEqual(1f, p.Fraction01, "a finished trade reads as a full bar, not an empty one");
        }

        [Test]
        public void Progress_SurvivesASaveRoundTrip()
        {
            _professions.AddXp(_trade, 25);
            var before = _professions.GetProgress(_trade);

            var save = new ProgressionSaveData();
            _professions.WriteTo(save);

            var other = new GameObject("Reloaded");
            try
            {
                var reloaded = other.AddComponent<PlayerProfessions>();
                reloaded.ReadFrom(save);
                var after = reloaded.GetProgress(_trade);

                Assert.AreEqual(before.Level, after.Level);
                Assert.AreEqual(before.Xp, after.Xp);
            }
            finally { Object.DestroyImmediate(other); }
        }

        [Test]
        public void TruncatedSave_LoadsWhatItCan_RatherThanThrowing()
        {
            // A hand-edited or truncated save must not throw inside the load path: losing one
            // trade's progress is recoverable, failing to load the character is not.
            var save = new ProgressionSaveData();
            save.professionKeys.Add("testing");
            save.professionKeys.Add("orphan");
            save.professionLevels.Add(4);
            save.professionXp.Add(2);

            var other = new GameObject("Truncated");
            try
            {
                var reloaded = other.AddComponent<PlayerProfessions>();
                Assert.DoesNotThrow(() => reloaded.ReadFrom(save));
                Assert.AreEqual(4, reloaded.GetLevel("testing"));
                Assert.AreEqual(1, reloaded.GetLevel("orphan"));
            }
            finally { Object.DestroyImmediate(other); }
        }

        [Test]
        public void XpCurve_ReturnsZeroAtTheCap_SoCallersCanDetectIt()
        {
            Assert.AreEqual(0, _trade.XpForNextLevel(_trade.maxLevel));
            Assert.AreEqual(0, _trade.XpForNextLevel(_trade.maxLevel + 5));
            Assert.Greater(_trade.XpForNextLevel(1), 0);
        }
    }
}
