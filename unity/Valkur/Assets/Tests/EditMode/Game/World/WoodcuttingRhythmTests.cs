using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Valkur.Data;
using Valkur.Gameplay.Interaction;
using Valkur.Gameplay.World;

namespace Valkur.Tests.EditMode.Game.World
{
    /// <summary>
    /// Pins the two gears of chopping: the automatic swing, and tapping on the beat.
    ///
    /// <para>The properties that matter are COMPARATIVE, so the tests race the two gears against
    /// each other on a synthetic clock: a master keeping time is about twice as fast, a beginner
    /// keeping time is only a little faster, and hammering the key is never faster than waiting.
    /// The last one is the one that was wrong on the first cut — a striking first tap let mashing
    /// out-chop the automatic swing — and only a race could show it.</para>
    /// </summary>
    [TestFixture]
    public class WoodcuttingRhythmTests
    {
        private const float DT = 1f / 60f;

        private readonly List<Object> _cleanup = new List<Object>();
        private readonly List<HarvestNode> _nodes = new List<HarvestNode>();
        private float _now;
        private DestructionResistanceTable _matrix;

        [SetUp]
        public void SetUp()
        {
            _now = 100f;
            _matrix = ScriptableObject.CreateInstance<DestructionResistanceTable>();
            _matrix.SeedShippedMatrix();
            _cleanup.Add(_matrix);
            HarvestBlowResolver.OverrideTable(_matrix);
        }

        [TearDown]
        public void TearDown()
        {
            HarvestBlowResolver.OverrideTable(null);
            foreach (var node in _nodes)
                if (node != null) { InteractableRegistry.Unregister(node); HarvestSwingRegistry.Unregister(node); }
            _nodes.Clear();

            foreach (var pickup in Object.FindObjectsOfType<Valkur.Gameplay.Inventory.WorldPickup>())
                Object.DestroyImmediate(pickup.gameObject);

            for (int i = _cleanup.Count - 1; i >= 0; i--)
                if (_cleanup[i] != null) Object.DestroyImmediate(_cleanup[i]);
            _cleanup.Clear();
        }

        // ── Builders ───────────────────────────────────────────────────────────────

        private GatheringSkillDefinition Skill()
        {
            var wood = ScriptableObject.CreateInstance<ItemDefinition>();
            wood.itemId = "probe_wood";
            wood.stackable = true;
            wood.maxStack = 999;
            _cleanup.Add(wood);

            var table = ScriptableObject.CreateInstance<GatheringYieldTable>();
            table.tiers.Add(new GatheringYieldTable.Tier { key = "probe", items = new[] { wood } });
            _cleanup.Add(table);

            var skill = ScriptableObject.CreateInstance<GatheringSkillDefinition>();
            skill.skillKey = "woodcutting_probe";
            skill.gainBaseChance = 0f;
            skill.yieldTable = table;
            _cleanup.Add(skill);
            return skill;
        }

        /// <summary>A tree too big to fell inside a race, so blows are counted, not trees.</summary>
        private HarvestNode Tree(GatheringSkillDefinition skill)
        {
            var p = ScriptableObject.CreateInstance<DestructionProfile>();
            p.material = MaterialClass.Wood;
            p.durability = 100000;
            p.requiredToolTier = 1;
            p.harvestable = true;
            p.harvestMode = HarvestMode.Destroy;
            p.blowDamage = 10;
            p.secondsPerBlow = 0.6f;
            p.remainsWalkable = false;
            p.noiseRadius = 0f;
            p.blowNoiseRadius = 0f;
            p.gatheringSkill = skill;
            p.skillDifficulty = 15;
            p.workPerYield = 1000;
            p.interactionRadius = 50f;
            _cleanup.Add(p);

            var go = new GameObject("ProbeTree");
            _cleanup.Add(go);
            var building = go.AddComponent<BuildingObject>();
            var durability = go.AddComponent<BuildingDurability>();
            durability.Initialize(p, building);
            var node = go.AddComponent<HarvestNode>();
            node.Initialize(p, building, durability);
            node.ClockForTests = () => _now;
            _nodes.Add(node);
            return node;
        }

        private GameObject Player(int skillTenths)
        {
            var go = new GameObject("ProbePlayer") { tag = "Player" };
            _cleanup.Add(go);
            go.AddComponent<Valkur.Gameplay.Inventory.Inventory>();
            PlayerGatheringSkills.For(go).SetTenths("woodcutting_probe", skillTenths);
            return go;
        }

        /// <summary>Run a session for <paramref name="seconds"/>; <paramref name="tapper"/> decides per frame whether to tap.</summary>
        private int Race(int skillTenths, float seconds, System.Func<HarvestNode, bool> tapper)
        {
            var skill = Skill();
            var node = Tree(skill);
            var player = Player(skillTenths);

            node.BeginInteraction(player);
            float end = _now + seconds;
            while (_now < end)
            {
                _now += DT;
                if (tapper != null && tapper(node)) node.Tap(player);
                node.StepSessionForTests();
            }
            return node.SessionBlows;
        }

        /// <summary>Taps once on every beat, as close to it as a 60 fps frame allows.</summary>
        private static System.Func<HarvestNode, bool> OnTheBeat()
        {
            bool started = false;
            return node =>
            {
                if (!started) { started = true; return true; }
                return node.InHitWindow && node.BlowCadence01 >= 0.97f;
            };
        }

        // ── The pure maths ─────────────────────────────────────────────────────────

        [Test]
        public void MoreSkill_TapsFaster_WithAWiderWindow()
        {
            var s = Skill();
            Assert.That(s.RhythmTempo(1000), Is.GreaterThan(s.RhythmTempo(0)));
            Assert.That(s.RhythmTempo(1000), Is.EqualTo(2f).Within(0.001f), "A master chops twice as fast on the beat.");
            Assert.That(s.HitWindowSeconds(1000, 1f), Is.GreaterThan(s.HitWindowSeconds(0, 1f)));
        }

        [Test]
        public void TheWindow_NeverCoversMostOfTheBeat()
        {
            var s = Skill();
            float beat = s.RhythmBeatSeconds(1000, 0.6f);
            Assert.That(s.HitWindowSeconds(1000, beat), Is.LessThanOrEqualTo(beat * s.hitWindowMaxOfBeat + 1e-5f));
        }

        [Test]
        public void Judge_SeparatesPerfectGoodEarlyAndLate()
        {
            var s = Skill();
            Assert.That(s.Judge(0.005f, 0.1f), Is.EqualTo(RhythmVerdict.Perfect));
            Assert.That(s.Judge(-0.08f, 0.1f), Is.EqualTo(RhythmVerdict.Good));
            Assert.That(s.Judge(-0.2f, 0.1f), Is.EqualTo(RhythmVerdict.Early));
            Assert.That(s.Judge(0.2f, 0.1f), Is.EqualTo(RhythmVerdict.Late));
        }

        // ── The races ──────────────────────────────────────────────────────────────

        [Test]
        public void AMaster_KeepingTime_ChopsAboutTwiceAsFastAsTheAutomaticSwing()
        {
            int auto = Race(1000, 12f, null);
            int tapped = Race(1000, 12f, OnTheBeat());

            Assert.That(tapped, Is.GreaterThan(auto * 1.7f), $"auto {auto} blows, tapped {tapped}");
            Assert.That(tapped, Is.LessThanOrEqualTo(auto * 2.1f), "Tapping is capped at the skill's tempo.");
        }

        [Test]
        public void ABeginner_KeepingTime_IsOnlyALittleFaster()
        {
            int auto = Race(0, 12f, null);
            int tapped = Race(0, 12f, OnTheBeat());

            Assert.That(tapped, Is.GreaterThan(auto), "Keeping time must always pay something.");
            Assert.That(tapped, Is.LessThan(auto * 1.5f), "A beginner cannot tap at a master's tempo.");
        }

        [Test]
        public void Mashing_IsNeverFasterThanLettingTheAxeFall()
        {
            foreach (int skill in new[] { 0, 500, 1000 })
            {
                int auto = Race(skill, 12f, null);
                int mashed = Race(skill, 12f, _ => true);
                Assert.That(mashed, Is.LessThanOrEqualTo(auto), $"skill {skill}: auto {auto}, mashed {mashed}");
            }
        }

        [Test]
        public void StoppingTapping_HandsBackToTheAutomaticSwing()
        {
            var node = Tree(Skill());
            var player = Player(500);
            node.BeginInteraction(player);

            node.Tap(player);
            Assert.That(node.InRhythmMode, Is.True);

            for (int i = 0; i < 240; i++) { _now += DT; node.StepSessionForTests(); }

            Assert.That(node.InRhythmMode, Is.False, "Beats let pass untouched must return the axe to its own clock.");
            int before = node.SessionBlows;
            for (int i = 0; i < 90; i++) { _now += DT; node.StepSessionForTests(); }
            Assert.That(node.SessionBlows, Is.GreaterThan(before), "The automatic swing must carry on by itself.");
        }

        [Test]
        public void AMiss_LandsNoBlow()
        {
            var node = Tree(Skill());
            var player = Player(0);
            node.BeginInteraction(player);
            node.Tap(player);                    // starts the metronome
            int before = node.SessionBlows;

            var verdict = node.Tap(player);      // immediately again: far before the beat

            Assert.That(verdict, Is.EqualTo(RhythmVerdict.Early));
            Assert.That(node.SessionBlows, Is.EqualTo(before), "A miss is no blow, not a weaker one.");
        }

        [Test]
        public void TheBusyBadge_SaysHowToStop()
        {
            var node = Tree(Skill());
            var player = Player(0);
            node.BeginInteraction(player);

            var prompt = node.DescribePrompt(player);
            Assert.That(prompt.Availability, Is.EqualTo(InteractionAvailability.Busy));
            Assert.That(prompt.Detail, Does.Contain("mantén"), "A tap now strikes; the badge must say how to stop.");
        }
    }
}
