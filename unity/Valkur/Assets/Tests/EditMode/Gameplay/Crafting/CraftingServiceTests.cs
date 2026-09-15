using NUnit.Framework;
using UnityEngine;
using Valkur.Data;
using Valkur.Gameplay.Crafting;
using Valkur.Gameplay.Skills;

namespace Valkur.Tests.EditMode.Gameplay.Crafting
{
    /// <summary>
    /// The crafting rules, and specifically the ones that would destroy a player's materials
    /// if they were wrong — plus the trade's 0-100 % skill that gates them and that they train.
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
        private PlayerSkills _skills;
        private SkillDefinition _skill;
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
            _skills = _go.AddComponent<PlayerSkills>();

            // A certain gain at 0 %: base chance 1 and no decay yet, so a craft that rolls is a
            // craft that moves the skill — these tests pin WHETHER a roll happens, not its odds.
            _skill = ScriptableObject.CreateInstance<SkillDefinition>();
            _skill.skillKey = "testing";
            _skill.displayName = "Testing";
            _skill.category = SkillCategory.Crafting;
            _skill.gainBaseChance = 1f;
            _skill.gainTenths = 5;

            _trade = ScriptableObject.CreateInstance<ProfessionDefinition>();
            _trade.professionKey = "testing";
            _trade.displayName = "Testing";
            _trade.skill = _skill;

            _plank = MakeItem("plank", 20);
            _nail = MakeItem("nail", 20);
            _chair = MakeItem("chair", 5);

            _recipe = ScriptableObject.CreateInstance<RecipeDefinition>();
            _recipe.recipeId = "chair";
            _recipe.displayName = "Chair";
            _recipe.profession = _trade;
            _recipe.output = _chair;
            _recipe.outputQuantity = 1;
            _recipe.skillGainRolls = 1;
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
            Object.DestroyImmediate(_skill);
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
            var a = CraftingService.Evaluate(_bag, _recipe, _skills, false);

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
            var a = CraftingService.Evaluate(_bag, _recipe, _skills, false);

            Assert.AreEqual(1, a.Shortfalls.Count);
            Assert.AreEqual(_nail, a.Shortfalls[0].Item);
            Assert.AreEqual(3, a.Shortfalls[0].Missing);
        }

        [Test]
        public void SkillIsCheckedBeforeIngredients()
        {
            // Both are wrong at once. Skill must win: it is the only refusal the player cannot
            // fix by walking somewhere or picking something up, so reporting the ingredients
            // first sends them to gather materials they still could not use.
            _recipe.requiredSkill = 30;
            var a = CraftingService.Evaluate(_bag, _recipe, _skills, false);

            Assert.AreEqual(CraftBlockReason.SkillTooLow, a.Reason);
            Assert.AreEqual(30, a.RequiredSkill);
        }

        [Test]
        public void EnoughSkill_Unlocks_ExactlyAtTheRequirement()
        {
            _bag.AddItem(_plank, 2);
            _bag.AddItem(_nail, 3);
            _recipe.requiredSkill = 30;

            _skills.SetTenths("testing", 299);
            Assert.AreEqual(CraftBlockReason.SkillTooLow,
                CraftingService.Evaluate(_bag, _recipe, _skills, false).Reason);

            _skills.SetTenths("testing", 300);
            Assert.IsTrue(CraftingService.Evaluate(_bag, _recipe, _skills, false).CanCraft);
        }

        [Test]
        public void IngredientsAreCheckedBeforeTheStation()
        {
            // "You are short two planks" is actionable anywhere; "find a workbench" sends the
            // player to a workbench they still cannot use.
            _recipe.requiresStation = true;
            var a = CraftingService.Evaluate(_bag, _recipe, _skills, false);

            Assert.AreEqual(CraftBlockReason.MissingIngredients, a.Reason);
        }

        [Test]
        public void StationRecipe_IsRefusedWithoutOne_AndAllowedWithOne()
        {
            _recipe.requiresStation = true;
            _bag.AddItem(_plank, 2);
            _bag.AddItem(_nail, 3);

            Assert.AreEqual(CraftBlockReason.NeedsStation,
                CraftingService.Evaluate(_bag, _recipe, _skills, false).Reason);
            Assert.IsTrue(CraftingService.Evaluate(_bag, _recipe, _skills, true).CanCraft);
        }

        [Test]
        public void NullSkills_ReadsAsZero_NeverAsARefusal()
        {
            // A missing component must not silently lock the whole system. 0 % recipes stay
            // craftable; only a genuine skill requirement refuses.
            _bag.AddItem(_plank, 2);
            _bag.AddItem(_nail, 3);
            Assert.IsTrue(CraftingService.Evaluate(_bag, _recipe, null, false).CanCraft);

            _recipe.requiredSkill = 1;
            Assert.AreEqual(CraftBlockReason.SkillTooLow,
                CraftingService.Evaluate(_bag, _recipe, null, false).Reason);
        }

        [Test]
        public void ATradeWithNoSkill_IsMalformed()
        {
            // A profession that trains nothing can neither gate nor teach its recipes; refusing
            // them as malformed is what makes the missing wiring visible.
            _trade.skill = null;
            Assert.IsFalse(_recipe.IsWellFormed);
            Assert.AreEqual(CraftBlockReason.Malformed,
                CraftingService.Evaluate(_bag, _recipe, _skills, false).Reason);
        }

        [Test]
        public void MalformedRecipe_IsRefusedRatherThanCrafted()
        {
            _recipe.output = null;
            _bag.AddItem(_plank, 2);
            _bag.AddItem(_nail, 3);

            Assert.AreEqual(CraftBlockReason.Malformed,
                CraftingService.Evaluate(_bag, _recipe, _skills, false).Reason);
            Assert.IsFalse(CraftingService.TryCraft(_bag, _recipe, _skills, false));
            Assert.AreEqual(2, _bag.GetItemCount(_plank), "a refused craft must consume nothing");
        }

        // ── Crafting and atomicity ──────────────────────────────────────────

        [Test]
        public void Craft_ConsumesExactly_AndYieldsTheOutput()
        {
            _bag.AddItem(_plank, 5);
            _bag.AddItem(_nail, 7);

            Assert.IsTrue(CraftingService.TryCraft(_bag, _recipe, _skills, false));
            Assert.AreEqual(3, _bag.GetItemCount(_plank));
            Assert.AreEqual(4, _bag.GetItemCount(_nail));
            Assert.AreEqual(1, _bag.GetItemCount(_chair));
        }

        [Test]
        public void RefusedCraft_ConsumesNothing()
        {
            _bag.AddItem(_plank, 2);
            Assert.IsFalse(CraftingService.TryCraft(_bag, _recipe, _skills, false));
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

            Assert.IsFalse(CraftingService.TryCraft(_bag, _recipe, _skills, false));

            Assert.AreEqual(20, _bag.GetItemCount(_plank));
            Assert.AreEqual(20, _bag.GetItemCount(_nail));
            Assert.AreEqual(0, _bag.GetItemCount(_chair));
        }

        [Test]
        public void RolledBackCraft_TrainsNothing()
        {
            // Otherwise a player with a full bag farms skill off a button that produces nothing.
            _bag.Initialize(2);
            _bag.AddItem(_plank, 20);
            _bag.AddItem(_nail, 20);

            CraftingService.TryCraft(_bag, _recipe, _skills, false);

            Assert.AreEqual(0, _skills.GetTenths("testing"));
        }

        [Test]
        public void SuccessfulCraft_RollsItsGains_OnTheTradesSkill()
        {
            _bag.AddItem(_plank, 4);
            _bag.AddItem(_nail, 6);
            _recipe.skillGainRolls = 2;

            Assert.IsTrue(CraftingService.TryCraft(_bag, _recipe, _skills, false));
            Assert.AreEqual(10, _skills.GetTenths("testing"), "two certain rolls of 0.5 % each");
        }

        [Test]
        public void ZeroRolls_TeachesNothing()
        {
            _bag.AddItem(_plank, 2);
            _bag.AddItem(_nail, 3);
            _recipe.skillGainRolls = 0;

            Assert.IsTrue(CraftingService.TryCraft(_bag, _recipe, _skills, false));
            Assert.AreEqual(0, _skills.GetTenths("testing"));
        }

        [Test]
        public void MaxBatches_IsBoundedByTheScarcestIngredient()
        {
            _bag.AddItem(_plank, 10);   // enough for 5
            _bag.AddItem(_nail, 7);     // enough for 2
            Assert.AreEqual(2, CraftingService.MaxBatches(_bag, _recipe));
        }

        // ── Saves ───────────────────────────────────────────────────────────

        [Test]
        public void CraftingSkill_SurvivesASaveRoundTrip()
        {
            _skills.SetTenths("testing", 345);
            var save = new ProgressionSaveData();
            _skills.WriteTo(save);

            var other = new GameObject("Reloaded");
            try
            {
                var reloaded = other.AddComponent<PlayerSkills>();
                reloaded.ReadFrom(save);
                Assert.AreEqual(345, reloaded.GetTenths("testing"));
            }
            finally { Object.DestroyImmediate(other); }
        }

        [Test]
        public void LegacyLevels_MapOntoTheSkillScale()
        {
            Assert.AreEqual(0, LegacyProfessionMigration.TenthsForLevel(1));
            Assert.AreEqual(1000, LegacyProfessionMigration.TenthsForLevel(20));
            Assert.AreEqual(1000, LegacyProfessionMigration.TenthsForLevel(99), "clamped to the old cap");
            Assert.AreEqual(0, LegacyProfessionMigration.TenthsForLevel(-3));
        }

        [Test]
        public void LegacySave_CarriesLevelsAcross_AndLumberjackBecomesWoodcutting()
        {
            var save = new ProgressionSaveData();
            save.professionKeys.Add("cooking");
            save.professionLevels.Add(11);
            save.professionKeys.Add("lumberjack");
            save.professionLevels.Add(20);
            save.professionKeys.Add("an-unknown-trade");
            save.professionLevels.Add(9);

            int moved = LegacyProfessionMigration.Apply(save, _skills);

            Assert.AreEqual(2, moved);
            Assert.AreEqual(LegacyProfessionMigration.TenthsForLevel(11), _skills.GetTenths("cooking"));
            Assert.AreEqual(1000, _skills.GetTenths("woodcutting"));
        }

        [Test]
        public void LegacyMigration_NeverLowersASkill()
        {
            // Loading a save twice is normal; a migration that could lower earned progress could
            // not be run twice safely.
            _skills.SetTenths("cooking", 900);
            var save = new ProgressionSaveData();
            save.professionKeys.Add("cooking");
            save.professionLevels.Add(2);

            Assert.AreEqual(0, LegacyProfessionMigration.Apply(save, _skills));
            Assert.AreEqual(900, _skills.GetTenths("cooking"));
        }

        [Test]
        public void TruncatedLegacySave_LoadsWhatItCan_RatherThanThrowing()
        {
            // A hand-edited or truncated save must not throw inside the load path.
            var save = new ProgressionSaveData();
            save.professionKeys.Add("cooking");
            save.professionKeys.Add("blacksmith");
            save.professionLevels.Add(4);

            Assert.DoesNotThrow(() => LegacyProfessionMigration.Apply(save, _skills));
            Assert.Greater(_skills.GetTenths("cooking"), 0);
            Assert.AreEqual(0, _skills.GetTenths("blacksmith"));
        }
    }
}
