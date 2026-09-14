using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay.Interaction;
using Valkur.Gameplay.World;
using Valkur.Gameplay.Inventory;

namespace Valkur.Tests.EditMode.Game.World
{
    /// <summary>
    /// Pins the COMPOSITION the audit found broken: blows x tool x skill x yield, through the
    /// real <see cref="BuildingDurability"/> entry point every source of damage uses.
    ///
    /// <para>Each half had its own green tests while the whole was wrong — the resolver scored
    /// tools correctly, the node rolled yields correctly — and together they paid bare hands ten
    /// times what an axe paid for the same tree. Only a test that fells a tree end to end and
    /// counts what reached the ground can see that.</para>
    /// </summary>
    [TestFixture]
    public class WoodcuttingCompositionTests
    {
        private readonly List<Object> _cleanup = new List<Object>();
        private readonly List<HarvestNode> _nodes = new List<HarvestNode>();
        private int _gathered;
        private DestructionResistanceTable _matrix;

        [SetUp]
        public void SetUp()
        {
            _gathered = 0;
            GameEvents.OnResourceGathered += CountGathered;
            LogAssert.ignoreFailingMessages = false;

            _matrix = ScriptableObject.CreateInstance<DestructionResistanceTable>();
            _matrix.SeedShippedMatrix();
            _cleanup.Add(_matrix);
            HarvestBlowResolver.OverrideTable(_matrix);
        }

        [TearDown]
        public void TearDown()
        {
            GameEvents.OnResourceGathered -= CountGathered;
            HarvestBlowResolver.OverrideTable(null);

            foreach (var node in _nodes)
                if (node != null) { InteractableRegistry.Unregister(node); HarvestSwingRegistry.Unregister(node); }
            _nodes.Clear();

            foreach (var pickup in Object.FindObjectsOfType<WorldPickup>())
                Object.DestroyImmediate(pickup.gameObject);

            for (int i = _cleanup.Count - 1; i >= 0; i--)
                if (_cleanup[i] != null) Object.DestroyImmediate(_cleanup[i]);
            _cleanup.Clear();
        }

        private void CountGathered(GameObject who, string skill, string item, int qty) => _gathered += qty;

        // ── Builders ───────────────────────────────────────────────────────────────

        private GatheringSkillDefinition Skill(float gainChance = 0f)
        {
            var wood = ScriptableObject.CreateInstance<ItemDefinition>();
            wood.itemId = "probe_wood";
            wood.displayName = "Madera de prueba";
            _cleanup.Add(wood);

            var table = ScriptableObject.CreateInstance<GatheringYieldTable>();
            table.tiers.Add(new GatheringYieldTable.Tier { key = "probe", displayName = "Prueba", items = new[] { wood } });
            _cleanup.Add(table);

            var skill = ScriptableObject.CreateInstance<GatheringSkillDefinition>();
            skill.skillKey = "woodcutting_probe";
            skill.gainBaseChance = gainChance;
            skill.yieldTable = table;
            _cleanup.Add(skill);
            return skill;
        }

        private DestructionProfile Profile(GatheringSkillDefinition skill, int durability = 40, int difficulty = 15)
        {
            var p = ScriptableObject.CreateInstance<DestructionProfile>();
            p.material = MaterialClass.Wood;
            p.durability = durability;
            p.requiredToolTier = 1;
            p.harvestable = true;
            p.harvestMode = HarvestMode.Destroy;
            p.blowDamage = 10;
            p.remainsWalkable = false;
            p.noiseRadius = 0f;
            p.blowNoiseRadius = 0f;
            p.gatheringSkill = skill;
            p.skillDifficulty = difficulty;
            p.workPerYield = 10;
            p.fellBonusYields = 1;
            _cleanup.Add(p);
            return p;
        }

        private BuildingDurability Tree(DestructionProfile profile)
        {
            var go = new GameObject("ProbeTree");
            _cleanup.Add(go);
            var building = go.AddComponent<BuildingObject>();
            var durability = go.AddComponent<BuildingDurability>();
            durability.Initialize(profile, building);
            var node = go.AddComponent<HarvestNode>();
            node.Initialize(profile, building, durability);
            _nodes.Add(node);
            return durability;
        }

        private GameObject Player(DamageClass tool = DamageClass.None, bool inBag = false, int skillTenths = -1,
            string skillKey = "woodcutting_probe")
        {
            var go = new GameObject("ProbePlayer") { tag = "Player" };
            _cleanup.Add(go);
            var inventory = go.AddComponent<Valkur.Gameplay.Inventory.Inventory>();

            if (tool != DamageClass.None)
            {
                var item = ScriptableObject.CreateInstance<ItemDefinition>();
                item.itemId = "probe_tool";
                item.toolClass = tool;
                item.toolTier = 1;
                _cleanup.Add(item);
                if (inBag) inventory.SetSlot(0, item, 1);
                else inventory.SetEquipmentSlot(0, item, 1);
            }

            if (skillTenths >= 0)
            {
                var skills = PlayerGatheringSkills.For(go);
                skills.SetTenths(skillKey, skillTenths);
            }
            return go;
        }

        /// <summary>Fell the tree with fixed-size blows; returns how many blows it took.</summary>
        private static int Fell(BuildingDurability tree, GameObject attacker, SpellElement? element = null)
        {
            int blows = 0;
            while (tree.AcceptsDamage && blows < 1000)
            {
                tree.ApplyObstacleDamage(10, attacker, Vector2.zero, element);
                blows++;
            }
            return blows;
        }

        // ── The audit's exploit ────────────────────────────────────────────────────

        [Test]
        public void BareHands_TakeMoreBlows_ButNeverPayMoreWoodThanAnAxe()
        {
            var skill = Skill();

            var axeTree = Tree(Profile(skill));
            int axeBlows = Fell(axeTree, Player(DamageClass.Axe, skillTenths: 0));
            int axeWood = _gathered;

            _gathered = 0;
            var handTree = Tree(Profile(skill));
            int handBlows = Fell(handTree, Player(skillTenths: 0));
            int handWood = _gathered;

            Assert.That(handBlows, Is.GreaterThan(axeBlows * 3), "The axe must be much faster.");
            Assert.That(axeWood, Is.GreaterThan(0), "A felled tree must pay something.");
            Assert.That(handWood, Is.LessThanOrEqualTo(axeWood),
                "Wood is a property of the TREE, paid by work: bare hands must never out-earn an axe.");
        }

        [Test]
        public void AnAxeInTheBag_ChopsExactlyLikeAnEquippedOne()
        {
            var skill = Skill();
            int equipped = Fell(Tree(Profile(skill)), Player(DamageClass.Axe, inBag: false, skillTenths: 0));
            int carried = Fell(Tree(Profile(skill)), Player(DamageClass.Axe, inBag: true, skillTenths: 0));

            Assert.That(carried, Is.EqualTo(equipped),
                "A tool counts from the bag: a woodcutter carries the axe on the belt.");
        }

        [Test]
        public void AWeaponInTheBag_DoesNotCountAsATool()
        {
            var skill = Skill();
            int bare = Fell(Tree(Profile(skill)), Player(skillTenths: 0));
            int spareSword = Fell(Tree(Profile(skill)), Player(DamageClass.Blade, inBag: true, skillTenths: 0));

            Assert.That(spareSword, Is.EqualTo(bare), "A spare sword in the bag is not a hatchet.");
        }

        [Test]
        public void AMagicBlow_FellsTheTree_ButYieldsNothing()
        {
            var skill = Skill();
            var tree = Tree(Profile(skill));
            Fell(tree, Player(skillTenths: 0), SpellElement.Fire);

            Assert.That(tree.IsDestroyed, Is.True);
            Assert.That(_gathered, Is.Zero, "A tree burnt down leaves ash, not logs.");
        }

        [Test]
        public void ANonPlayerAttacker_FellsTheTree_ButYieldsNothing()
        {
            var skill = Skill();
            var tree = Tree(Profile(skill));
            var monster = new GameObject("ProbeMonster");
            _cleanup.Add(monster);

            Fell(tree, monster);

            Assert.That(tree.IsDestroyed, Is.True);
            Assert.That(_gathered, Is.Zero, "A monster clipping a trunk must not scatter logs.");
            Assert.That(monster.GetComponent<PlayerGatheringSkills>(), Is.Null,
                "Only the player grows a gathering skill.");
        }

        // ── Skill ──────────────────────────────────────────────────────────────────

        [Test]
        public void AMaster_FellsFasterThanABeginner_OnTheSameTree()
        {
            var skill = Skill();
            int beginner = Fell(Tree(Profile(skill, durability: 120, difficulty: 50)), Player(DamageClass.Axe, skillTenths: 0));
            int master = Fell(Tree(Profile(skill, durability: 120, difficulty: 50)), Player(DamageClass.Axe, skillTenths: 1000));

            Assert.That(master, Is.LessThan(beginner));
        }

        [Test]
        public void AMaster_EarnsABiggerFellBonus()
        {
            var skill = Skill();
            Fell(Tree(Profile(skill)), Player(DamageClass.Axe, skillTenths: 0));
            int beginner = _gathered;

            _gathered = 0;
            Fell(Tree(Profile(skill)), Player(DamageClass.Axe, skillTenths: 1000));
            int master = _gathered;

            Assert.That(master, Is.GreaterThanOrEqualTo(beginner + skill.BonusYields(1000)));
        }

        [Test]
        public void ChoppingTrainsTheSkill_AndOnlyUpward()
        {
            var skill = Skill(gainChance: 1f);
            skill.trivialMargin = 100f;
            var player = Player(DamageClass.Axe, skillTenths: 0);

            Fell(Tree(Profile(skill)), player);

            var skills = player.GetComponent<PlayerGatheringSkills>();
            Assert.That(skills, Is.Not.Null);
            Assert.That(skills.GetTenths(skill.skillKey), Is.GreaterThan(0));
            Assert.That(skills.GetTenths(skill.skillKey), Is.LessThanOrEqualTo(GatheringSkillDefinition.MaxTenths));
        }

        [Test]
        public void EveryBlowFromAnySource_IsReportedToTheNode()
        {
            var skill = Skill();
            var tree = Tree(Profile(skill));
            var node = tree.GetComponent<HarvestNode>();
            int landed = 0;
            node.BlowLanded += (b, y) => landed++;

            int blows = Fell(tree, Player(DamageClass.Axe, skillTenths: 0));

            Assert.That(landed, Is.EqualTo(blows),
                "A tree chopped with a sword swing must look and pay like one chopped with the key.");
        }

        // ── Feedback ───────────────────────────────────────────────────────────────

        [Test]
        public void AFelling_AnnouncesOnlyWhatWasExtracted_InOneLine()
        {
            var skill = Skill();
            var tree = Tree(Profile(skill));
            Fell(tree, Player(DamageClass.Axe, skillTenths: 0));

            var feedback = tree.GetComponent<HarvestFeedback>();
            Assert.That(feedback, Is.Not.Null, "A tree felled by a swing must get the same feedback as the key.");

            feedback.Flush();
            Assert.That(feedback.LastMessage, Does.StartWith("+" + _gathered + " Madera de prueba"),
                "The one line names the total and the wood; the fall itself says nothing in words.");
        }

        [Test]
        public void Yields_GoStraightIntoTheBag_NotOntoTheGround()
        {
            var skill = Skill();
            var player = Player(DamageClass.Axe, skillTenths: 0);
            Fell(Tree(Profile(skill)), player);

            var bag = player.GetComponent<Valkur.Gameplay.Inventory.Inventory>();
            int inBag = 0;
            foreach (var slot in bag.Slots)
                if (!slot.IsEmpty && slot.Item != null && slot.Item.itemId == "probe_wood") inBag += slot.Quantity;

            Assert.That(_gathered, Is.GreaterThan(0));
            Assert.That(inBag, Is.EqualTo(_gathered), "Every extracted log must be in the bag.");
            Assert.That(Object.FindObjectsOfType<WorldPickup>(), Is.Empty, "Nothing may be left on the ground.");
        }

        [Test]
        public void AFullBag_DropsTheLogAtTheWorkersFeet_AndSaysSoOnce()
        {
            var skill = Skill();
            var player = Player(DamageClass.Axe, skillTenths: 0);
            var bag = player.GetComponent<Valkur.Gameplay.Inventory.Inventory>();

            var filler = ScriptableObject.CreateInstance<ItemDefinition>();
            filler.itemId = "probe_filler";
            filler.stackable = false;
            _cleanup.Add(filler);
            for (int i = 0; i < 500 && bag.AddItem(filler, 1) == 0; i++) { }

            var tree = Tree(Profile(skill));
            Fell(tree, player);

            Assert.That(_gathered, Is.GreaterThan(0), "A full bag must not cost the player the wood.");
            Assert.That(Object.FindObjectsOfType<WorldPickup>().Length, Is.EqualTo(_gathered));
            Assert.That(tree.GetComponent<HarvestFeedback>().LastMessage, Is.EqualTo("Mochila llena"));
        }

        [Test]
        public void TheWorkBar_Fills_MarksEveryLog_AndCountsDown()
        {
            var skill = Skill();
            var tree = Tree(Profile(skill));
            var node = tree.GetComponent<HarvestNode>();
            var player = Player(DamageClass.Axe, skillTenths: 0);

            tree.ApplyObstacleDamage(10, player, Vector2.zero, null);

            var bar = tree.GetComponent<HarvestNodeBar>();
            Assert.That(bar, Is.Not.Null, "Any blow must bring up the bar.");
            bar.Step(0.016f);

            Assert.That(bar.IsVisible, Is.True);
            Assert.That(bar.NotchCount, Is.EqualTo(3), "40 durability at 10 work per log is a notch at 25, 50 and 75 %.");
            Assert.That(bar.Progress, Is.GreaterThan(0f).And.LessThan(1f), "The bar FILLS toward the tree coming down.");
            Assert.That(node.SecondsRemaining, Is.GreaterThan(0f));
            Assert.That(bar.EtaText, Does.EndWith(" s"));
        }

        [Test]
        public void TheWrongToolWarning_IsInSpanish()
        {
            var skill = Skill();
            var tree = Tree(Profile(skill));
            tree.ApplyObstacleDamage(10, Player(skillTenths: 0), Vector2.zero, null);

            var feedback = tree.GetComponent<HarvestFeedback>();
            Assert.That(feedback.LastMessage, Is.EqualTo("Herramienta inadecuada"));
        }

        [Test]
        public void HarvestFeedbackSource_CarriesNoEnglishPlayerText()
        {
            string path = System.IO.Path.Combine(Application.dataPath,
                "_Project/Scripts/Gameplay/World/Harvesting/HarvestFeedback.cs");
            string source = System.IO.File.ReadAllText(path);

            Assert.That(source, Does.Not.Contain("\"Wrong tool\""));
            Assert.That(source, Does.Not.Contain("\"Immune\""));
        }

        // ── The work mark ──────────────────────────────────────────────────────────

        [Test]
        public void ChoppingEngagesTheTrunkMark_OnThePlayerOnly()
        {
            var skill = Skill();
            var tree = Tree(Profile(skill));
            var node = tree.GetComponent<HarvestNode>();
            var player = Player(DamageClass.Axe, skillTenths: 0);

            tree.ApplyObstacleDamage(10, player, Vector2.zero, null);

            var mark = player.GetComponent<HarvestWorkMark>();
            Assert.That(mark, Is.Not.Null, "A blow on a tree must bring up the trunk mark.");
            Assert.That(mark.Node, Is.SameAs(node));

            var monster = new GameObject("ProbeMonster");
            _cleanup.Add(monster);
            Assert.That(HarvestWorkMark.For(monster), Is.Null, "Only the player gets a work mark.");
        }

        [Test]
        public void TheAimChevron_OnlyStepsAsideWhenPushed_AndKeepsItsOneJob()
        {
            string path = System.IO.Path.Combine(Application.dataPath,
                "_Project/Scripts/Gameplay/Combat/WorldUI/FacingIndicator.State.cs");
            string source = System.IO.File.ReadAllText(path);

            // The chevron is TOLD to step aside; it must never learn about trees to decide it.
            Assert.That(source, Does.Contain("SetSteppedAside"));
            Assert.That(source, Does.Not.Contain("HarvestNode"));
            Assert.That(source, Does.Not.Contain("HarvestWorkMark"));
        }

        // ── Persistence ────────────────────────────────────────────────────────────

        [Test]
        public void GatheringSkills_RoundTripThroughTheProgressionDocument()
        {
            var a = Player(skillTenths: 347);
            var data = new ProgressionSaveData();
            a.GetComponent<PlayerGatheringSkills>().WriteTo(data);

            Assert.That(data.IsEmpty, Is.False, "A document holding only a gathering skill is not empty.");

            var b = Player();
            var restored = PlayerGatheringSkills.For(b);
            int changes = 0;
            restored.SkillChanged += (k, o, n) => changes++;
            restored.ReadFrom(data);

            Assert.That(restored.GetTenths("woodcutting_probe"), Is.EqualTo(347));
            Assert.That(changes, Is.Zero, "A load must not announce six milestones.");
        }

        [Test]
        public void RestoringAnOutOfRangeValue_IsClamped()
        {
            var data = new ProgressionSaveData();
            data.gatheringSkillKeys.Add("woodcutting_probe");
            data.gatheringSkillTenths.Add(99999);

            var skills = PlayerGatheringSkills.For(Player());
            skills.ReadFrom(data);

            Assert.That(skills.GetTenths("woodcutting_probe"), Is.EqualTo(GatheringSkillDefinition.MaxTenths));
        }
    }
}
