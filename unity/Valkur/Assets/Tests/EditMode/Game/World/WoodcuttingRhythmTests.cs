using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Valkur.Data;
using Valkur.Gameplay.Interaction;
using Valkur.Gameplay.World;
using Valkur.Gameplay.Skills;

namespace Valkur.Tests.EditMode.Game.World
{
    /// <summary>
    /// Pins the two gears of chopping — the automatic swing, and tapping on the beat — and the six
    /// grades a tapped cut is judged in.
    ///
    /// <para>The properties that matter are COMPARATIVE, so the tests race the two gears against
    /// each other on a synthetic clock: a master keeping time is about twice as fast, a beginner
    /// keeping time is only a little faster, hammering the key is never faster than waiting, and a
    /// sloppy cut pays less than simply letting the axe fall. The target on the trunk is pinned
    /// against the grading itself: what the player sees as the centre IS what is graded as the
    /// centre.</para>
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

        private SkillDefinition Skill()
        {
            var wood = ScriptableObject.CreateInstance<ItemDefinition>();
            wood.itemId = "probe_wood";
            wood.stackable = true;
            wood.maxStack = 999;
            _cleanup.Add(wood);

            var table = ScriptableObject.CreateInstance<GatheringYieldTable>();
            table.tiers.Add(new GatheringYieldTable.Tier { key = "probe", items = new[] { wood } });
            _cleanup.Add(table);

            var skill = ScriptableObject.CreateInstance<SkillDefinition>();
            skill.skillKey = "woodcutting_probe";
            skill.gainBaseChance = 0f;
            skill.yieldTable = table;
            _cleanup.Add(skill);
            return skill;
        }

        /// <summary>A tree too big to fell inside a race, so blows are counted, not trees.</summary>
        private HarvestNode Tree(SkillDefinition skill, int hp = 100000, int blowDamage = 10)
        {
            var p = ScriptableObject.CreateInstance<DestructionProfile>();
            p.material = MaterialClass.Wood;
            p.durability = hp;
            p.requiredToolTier = 1;
            p.harvestable = true;
            p.harvestMode = HarvestMode.Destroy;
            p.blowDamage = blowDamage;
            p.secondsPerBlow = 0.6f;
            p.remainsWalkable = false;
            p.noiseRadius = 0f;
            p.blowNoiseRadius = 0f;
            p.gatheringSkill = skill;
            p.skillDifficulty = 15;
            p.workPerYield = 1000000;
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
            PlayerSkills.For(go).SetTenths("woodcutting_probe", skillTenths);
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

        /// <summary>Taps once to start, then waits until the first beat is due.</summary>
        private (HarvestNode node, GameObject player) StartAndWait(int skillTenths, int blowDamage = 10)
        {
            var node = Tree(Skill(), blowDamage: blowDamage);
            var player = Player(skillTenths);
            node.BeginInteraction(player);
            node.Tap(player);
            while (!(node.InRhythmMode && node.BlowCadence01 >= 0f && node.SecondsFromBeat >= -0.001f))
            {
                _now += 0.001f;
                node.StepSessionForTests();
            }
            return (node, player);
        }

        /// <summary>Tap exactly <paramref name="windows"/> hit-window half-widths from the current beat.</summary>
        private RhythmTap TapAt(HarvestNode node, GameObject player, float windows)
        {
            _now += windows * node.HitWindowSeconds - node.SecondsFromBeat;
            return node.Tap(player);
        }

        private static int Durability(HarvestNode node) => node.GetComponent<BuildingDurability>().CurrentDurability;

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
        public void GradeCut_IsASixStepLadder_FromTheCentreOutward_OnBothSides()
        {
            var s = Skill();
            const float w = 0.1f;
            foreach (float sign in new[] { -1f, 1f })
            {
                Assert.AreEqual(CutGrade.Perfect, s.GradeCut(sign * 0.2f * w, w));
                Assert.AreEqual(CutGrade.Good, s.GradeCut(sign * 0.5f * w, w));
                Assert.AreEqual(CutGrade.Ok, s.GradeCut(sign * 0.9f * w, w));
                Assert.AreEqual(CutGrade.Bad, s.GradeCut(sign * 1.2f * w, w));
                Assert.AreEqual(CutGrade.Awful, s.GradeCut(sign * 1.6f * w, w));
                Assert.AreEqual(CutGrade.Soquete, s.GradeCut(sign * 2.5f * w, w));
            }
        }

        [Test]
        public void TheBands_AreNested_OkIsTheWindow_AndTheEdgeIsTheLateGrace()
        {
            var s = Skill();
            float last = 0f;
            for (var g = CutGrade.Perfect; g <= CutGrade.Awful; g++)
            {
                float reach = s.CutReachOfWindow(g);
                Assert.Greater(reach, last, $"{g} must reach further than the band inside it");
                last = reach;
            }
            Assert.AreEqual(1f, s.CutReachOfWindow(CutGrade.Ok), 1e-5f, "OK is exactly the hit window");
            Assert.AreEqual(s.nearMissWindows, s.CutReachOfWindow(CutGrade.Awful), 1e-5f,
                "the target's edge is where the late grace ends: nothing past it can be graded");
        }

        [Test]
        public void ACutsWorth_FallsFromTheCentre_AndOnlyTheCentreBeatsTheAxe()
        {
            var s = Skill();
            Assert.Greater(s.CutWorth(CutGrade.Perfect), 1f, "the centre must be worth more than simply keeping time");
            Assert.AreEqual(1f, s.CutWorth(CutGrade.None), "the automatic swing is the unit");
            Assert.AreEqual(0f, s.CutWorth(CutGrade.Soquete), "a soquete never lands");
            for (var g = CutGrade.Perfect; g < CutGrade.Awful; g++)
                Assert.Greater(s.CutWorth(g), s.CutWorth(g + 1), $"{g} must be worth more than {g + 1}");
            Assert.Greater(s.CutWorth(CutGrade.Awful), 0f, "a pésimo still scuffs the bark");
        }

        [Test]
        public void ASloppyCut_AtAMastersTempo_PaysLessThanLettingTheAxeFall()
        {
            // Tapping multiplies blows; a weak cut must not multiply the WORK past the automatic swing,
            // or tapping carelessly would be the fast way to fell a tree.
            var s = Skill();
            float tempo = s.RhythmTempo(1000);
            Assert.Less(tempo * s.CutWorth(CutGrade.Bad), 1f);
            Assert.Less(tempo * s.CutWorth(CutGrade.Awful), 1f);
            Assert.GreaterOrEqual(s.RhythmTempo(0) * s.CutWorth(CutGrade.Ok), 1f,
                "an OK cut at a beginner's tempo should at least match the automatic swing");
        }

        [Test]
        public void OnlyOkOrBetter_KeepsTheCombo()
        {
            Assert.IsTrue(SkillDefinition.CutKeepsStreak(CutGrade.Perfect));
            Assert.IsTrue(SkillDefinition.CutKeepsStreak(CutGrade.Ok));
            Assert.IsFalse(SkillDefinition.CutKeepsStreak(CutGrade.Bad));
            Assert.IsFalse(SkillDefinition.CutKeepsStreak(CutGrade.Soquete));
            Assert.IsFalse(SkillDefinition.CutLands(CutGrade.Soquete));
            Assert.IsTrue(SkillDefinition.CutLands(CutGrade.Awful));
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

        // ── Grades in play ─────────────────────────────────────────────────────────

        [Test]
        public void ACutsWorkDone_FollowsItsGrade()
        {
            // Big blows, so the resolver's never-round-to-zero floor cannot flatten the difference.
            var (perfectNode, p1) = StartAndWait(1000, blowDamage: 5000);
            int before = Durability(perfectNode);
            var perfect = TapAt(perfectNode, p1, 0.1f);
            int perfectWork = before - Durability(perfectNode);

            var (awfulNode, p2) = StartAndWait(1000, blowDamage: 5000);
            before = Durability(awfulNode);
            var awful = TapAt(awfulNode, p2, 1.6f);   // late: a pésimo past the beat always lands
            int awfulWork = before - Durability(awfulNode);

            Assert.AreEqual(CutGrade.Perfect, perfect.Grade);
            Assert.AreEqual(CutGrade.Awful, awful.Grade);
            Assert.IsTrue(awful.Landed, "a late pésimo lands");
            Assert.Greater(awfulWork, 0, "a pésimo still does some work");
            Assert.Greater(perfectWork, awfulWork * 4, $"perfect {perfectWork} vs awful {awfulWork}");
        }

        [Test]
        public void ALateBadCut_Lands_ButBreaksTheCombo()
        {
            var (node, player) = StartAndWait(1000);
            Assert.IsTrue(TapAt(node, player, 0f).Landed);
            Assert.AreEqual(1, node.RhythmStreak);

            int blows = node.SessionBlows;
            var bad = TapAt(node, player, 1.2f);

            Assert.AreEqual(CutGrade.Bad, bad.Grade);
            Assert.AreEqual(RhythmTapOutcome.Landed, bad.Outcome);
            Assert.AreEqual(blows + 1, node.SessionBlows, "a bad cut is a weak blow, not no blow");
            Assert.AreEqual(0, node.RhythmStreak, "a bad cut breaks the combo");
        }

        [Test]
        public void ASoquete_LosesTheBeat_AndEarnsATaunt()
        {
            var (node, player) = StartAndWait(0);
            int blows = node.SessionBlows;

            var tap = TapAt(node, player, -3f);

            Assert.AreEqual(CutGrade.Soquete, tap.Grade);
            Assert.AreEqual(RhythmTapOutcome.Lost, tap.Outcome);
            Assert.AreEqual(blows, node.SessionBlows, "a soquete is no blow");
            Assert.IsTrue(node.RhythmBeatLost);

            var callout = player.GetComponent<HarvestRhythmCallout>();
            Assert.IsNotNull(callout);
            Assert.AreEqual("¡CORTE SOQUETE!", callout.JudgementText);
            CollectionAssert.Contains(SoquetePhrases.All, callout.TauntText, "a soquete is told what it is");
        }

        [Test]
        public void KeepingTime_BuildsACombo_AndASoqueteBreaksIt()
        {
            var node = Tree(Skill());
            var player = Player(1000);
            node.BeginInteraction(player);

            var beat = OnTheBeat();
            for (int i = 0; i < 360 && node.RhythmStreak < 4; i++)
            {
                _now += DT;
                if (beat(node)) node.Tap(player);
                node.StepSessionForTests();
            }
            Assert.GreaterOrEqual(node.RhythmStreak, 3, "fixture must build a streak first");

            var callout = player.GetComponent<HarvestRhythmCallout>();
            Assert.IsNotNull(callout, "tapping a player's tree must give the player a judgement line");
            StringAssert.StartsWith("COMBO x", callout.ComboLine);

            // Straight after a hit (past the debounce) is far from the next beat: off the target.
            _now += 0.1f;
            var tap = node.Tap(player);
            Assert.AreEqual(RhythmTapOutcome.Lost, tap.Outcome);
            Assert.AreEqual("COMBO ROTO", callout.ComboLine, "losing a real streak is announced");
            Assert.AreEqual("¡CORTE SOQUETE!", callout.JudgementText);
        }

        // ── Chances per blow ─────────────────────────────────────────────────────────

        [Test]
        public void Chances_GrowWithSkill_OneTwoThree()
        {
            var s = Skill();
            Assert.AreEqual(1, s.RhythmTries(0), "a beginner gets one chance per blow");
            Assert.AreEqual(2, s.RhythmTries(500));
            Assert.AreEqual(3, s.RhythmTries(1000));
        }

        [Test]
        public void TheGapBetweenBeats_LeavesNoRoomToMashIntoTheNextOne()
        {
            // The structural half of "mashing never wins": one beat's late grace and the next
            // beat's target must not touch, or a masher's first tap after a beat resolves could
            // land on the target instead of off it.
            var s = Skill();
            Assert.Less((1f + s.nearMissWindows) * s.hitWindowMaxOfBeat, 1f);
        }

        [Test]
        public void AnEarlyWeakCut_WithChancesLeft_IsHeldBack_AndTheBeatCanStillBeCut()
        {
            var (node, player) = StartAndWait(500);

            int blows = node.SessionBlows;
            var early = TapAt(node, player, -1.3f);
            Assert.AreEqual(RhythmTapOutcome.Retry, early.Outcome);
            Assert.AreEqual(CutGrade.Bad, early.Grade);
            Assert.AreEqual(1, node.RhythmTriesLeft, "one of two chances spent");
            Assert.AreEqual(blows, node.SessionBlows, "a held-back cut lands nothing");

            var v = TapAt(node, player, 0f);
            Assert.IsTrue(v.Landed, $"the second chance must be able to land, got {v}");
            Assert.AreEqual(CutGrade.Perfect, v.Grade);
            Assert.AreEqual(blows + 1, node.SessionBlows);
        }

        [Test]
        public void ABeginner_HasOneChance_AnEarlyWeakCutLandsAsItIs()
        {
            var (node, player) = StartAndWait(0);
            int blows = node.SessionBlows;

            var tap = TapAt(node, player, -1.3f);
            Assert.AreEqual(RhythmTapOutcome.Landed, tap.Outcome, "one chance at 0 %: no retry");
            Assert.AreEqual(CutGrade.Bad, tap.Grade);
            Assert.AreEqual(blows + 1, node.SessionBlows);
        }

        [Test]
        public void AFarCut_IsASoquete_EvenWithChancesLeft()
        {
            var (node, player) = StartAndWait(1000);
            _now -= node.BeatSeconds * 0.6f;
            var tap = node.Tap(player);
            Assert.AreEqual(RhythmTapOutcome.Lost, tap.Outcome);
            Assert.IsTrue(node.RhythmBeatLost, "a tap far from the beat is not a nervous finger");
        }

        [Test]
        public void ADoubleClick_SpendsOneChance_NotTwo()
        {
            var (node, player) = StartAndWait(1000);
            TapAt(node, player, -1.3f);
            _now += 0.01f;   // the bounce of the same press
            Assert.AreEqual(RhythmTapOutcome.Ignored, node.Tap(player).Outcome);
            Assert.AreEqual(2, node.RhythmTriesLeft, "three chances, one spent");
        }

        [Test]
        public void TheFellingHit_StillShowsItsCombo()
        {
            // The blow that fells the tree ends the session and resets the streak. The last hit of
            // a tree is the one a player is most pleased with, so its combo must not vanish.
            var node = Tree(Skill(), hp: 4);
            var player = Player(0);
            node.BeginInteraction(player);

            var beat = OnTheBeat();
            for (int i = 0; i < 600 && !node.IsSpent; i++)
            {
                _now += DT;
                if (beat(node)) node.Tap(player);
                node.StepSessionForTests();
            }

            var callout = player.GetComponent<HarvestRhythmCallout>();
            Assert.IsTrue(node.IsSpent, "fixture must fell the tree");
            Assert.IsNotNull(callout);
            StringAssert.StartsWith("COMBO x", callout.ComboLine,
                $"the felling hit reported '{callout.ComboLine}' instead of its combo");
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

        // ── The judgement line ─────────────────────────────────────────────────────

        private static RhythmTap Landed(CutGrade g, float offset, int streakBefore = 0, int streakAfter = 1) =>
            new RhythmTap(g, RhythmTapOutcome.Landed, offset, 0.1f, 0, streakBefore, streakAfter);

        [Test]
        public void Callouts_NameEveryGrade_AndWeakCutsPointTheWayTheyMissed()
        {
            Assert.AreEqual("¡CORTE PERFECTO!", RhythmCallouts.ForTap(Landed(CutGrade.Perfect, 0f)).Text);
            Assert.AreEqual("¡CORTE BUENO!", RhythmCallouts.ForTap(Landed(CutGrade.Good, 0.05f)).Text);
            Assert.AreEqual("CORTE OK", RhythmCallouts.ForTap(Landed(CutGrade.Ok, -0.09f)).Text);
            Assert.AreEqual("« CORTE MALO", RhythmCallouts.ForTap(Landed(CutGrade.Bad, -0.12f, 0, 0)).Text);
            Assert.AreEqual("CORTE MALO »", RhythmCallouts.ForTap(Landed(CutGrade.Bad, 0.12f, 0, 0)).Text);
            Assert.AreEqual("CORTE PÉSIMO »", RhythmCallouts.ForTap(Landed(CutGrade.Awful, 0.16f, 0, 0)).Text);
            Assert.AreEqual("¡CORTE SOQUETE!", RhythmCallouts.ForTap(
                new RhythmTap(CutGrade.Soquete, RhythmTapOutcome.Lost, -0.3f, 0.1f, 0, 0, 0)).Text);

            Assert.AreEqual(RhythmCalloutMotion.Rise, RhythmCallouts.ForTap(Landed(CutGrade.Ok, 0f)).Motion);
            Assert.AreEqual(RhythmCalloutMotion.Sag, RhythmCallouts.ForTap(Landed(CutGrade.Bad, 0.12f, 0, 0)).Motion);
            Assert.IsTrue(RhythmCallouts.ForTap(new RhythmTap(CutGrade.Soquete, RhythmTapOutcome.Lost, -0.3f, 0.1f, 0, 0, 0)).IsMiss);
        }

        [Test]
        public void EveryGrade_HasItsOwnColour()
        {
            var seen = new HashSet<Color>();
            for (var g = CutGrade.Perfect; g <= CutGrade.Soquete; g++)
                Assert.IsTrue(seen.Add(RhythmCallouts.CutColour(g)), $"{g} shares a colour with another grade");
        }

        [Test]
        public void Milestones_Shout_AndTheComboIsOnlyNamedFromTwo()
        {
            Assert.Greater(RhythmCallouts.ForTap(Landed(CutGrade.Good, 0f, 9, 10)).Weight,
                RhythmCallouts.ForTap(Landed(CutGrade.Good, 0f, 8, 9)).Weight);
            Assert.AreEqual("¡RACHA x10!", RhythmCallouts.ComboText(10));
            Assert.IsTrue(RhythmCallouts.IsMilestone(200));
            Assert.IsFalse(RhythmCallouts.IsMilestone(7));
            Assert.AreEqual(string.Empty, RhythmCallouts.ComboText(1));
            Assert.AreEqual("COMBO x2", RhythmCallouts.ComboText(2));
        }

        [Test]
        public void ABreak_IsAnnouncedForWeakCutsAndSoquetes_NeverForRetries()
        {
            Assert.IsTrue(RhythmCallouts.AnnouncesBreak(Landed(CutGrade.Bad, 0.12f, 5, 0)));
            Assert.IsTrue(RhythmCallouts.AnnouncesBreak(new RhythmTap(CutGrade.Soquete, RhythmTapOutcome.Lost, -1f, 0.1f, 0, 5, 0)));
            Assert.IsFalse(RhythmCallouts.AnnouncesBreak(new RhythmTap(CutGrade.Bad, RhythmTapOutcome.Retry, -0.12f, 0.1f, 1, 5, 5)));
            Assert.IsFalse(RhythmCallouts.AnnouncesBreak(Landed(CutGrade.Ok, 0.09f, 5, 6)));
            Assert.IsFalse(RhythmCallouts.AnnouncesBreak(Landed(CutGrade.Bad, 0.12f, 2, 0)), "a streak of two is not news");
        }

        // ── The soquete dictionary ─────────────────────────────────────────────────

        [Test]
        public void TheSoqueteDictionary_HasTwentyDistinctLines()
        {
            Assert.AreEqual(20, SoquetePhrases.All.Length);
            var distinct = new HashSet<string>(SoquetePhrases.All);
            Assert.AreEqual(20, distinct.Count, "every line must be different");
            foreach (var line in SoquetePhrases.All)
            {
                Assert.IsFalse(string.IsNullOrWhiteSpace(line));
                Assert.LessOrEqual(line.Length, 60, $"'{line}' will not fit two lines over a trunk");
            }
        }

        [Test]
        public void TheBag_DealsEveryLineOnce_BeforeAnyComesBack_AndNeverTwiceInARow()
        {
            var bag = new SoquetePhrases(12345);
            string previous = null;
            for (int round = 0; round < 10; round++)
            {
                var dealt = new HashSet<string>();
                for (int i = 0; i < SoquetePhrases.All.Length; i++)
                {
                    string line = bag.Next();
                    Assert.AreNotEqual(previous, line, "the same insult twice in a row reads as a bug");
                    Assert.IsTrue(dealt.Add(line), $"round {round}: '{line}' came back before the bag emptied");
                    previous = line;
                }
            }
        }

        // ── The target on the trunk ────────────────────────────────────────────────

        [Test]
        public void TheTarget_IsDrawnFromTheGrading_WhatLooksLikeTheCentreIsTheCentre()
        {
            var s = Skill();
            const float w = 0.1f;
            for (float x = 0f; x <= 1.8f; x += 0.01f)
            {
                var grade = s.GradeCut(x * w, w);
                float r = CutTargetGeometry.RadiusFor(s, x * w, w, 1f);
                float outer = CutTargetGeometry.BandRadius(s, grade);
                float inner = grade == CutGrade.Perfect ? 0f : CutTargetGeometry.BandRadius(s, grade - 1);
                Assert.That(r, Is.InRange(inner - 1e-4f, outer + 1e-4f),
                    $"a cut {x:0.00} windows out is graded {grade} and must be drawn inside that band");
            }
        }

        [Test]
        public void TheTargetBands_GrowOutward_ToTheOuterRadius()
        {
            var s = Skill();
            float last = 0f;
            for (var g = CutGrade.Perfect; g <= CutGrade.Awful; g++)
            {
                float r = CutTargetGeometry.BandRadius(s, g);
                Assert.Greater(r, last);
                last = r;
            }
            Assert.AreEqual(CutTargetGeometry.OuterRadius, last, 1e-5f);
            Assert.Greater(CutTargetGeometry.BandRadius(s, CutGrade.Perfect), 0.08f,
                "the centre must be big enough to see on a trunk");
        }

        [Test]
        public void ACutsMark_SitsOnTheSideItMissedOn_AndASoqueteOffTheTarget()
        {
            var s = Skill();
            Assert.Less(CutTargetGeometry.MarkPosition(s, CutGrade.Bad, -0.12f, 0.1f, 0).x, 0f, "early is left");
            Assert.Greater(CutTargetGeometry.MarkPosition(s, CutGrade.Bad, 0.12f, 0.1f, 0).x, 0f, "late is right");
            Assert.Less(Mathf.Abs(CutTargetGeometry.MarkPosition(s, CutGrade.Perfect, 0.002f, 0.1f, 0).x),
                CutTargetGeometry.BandRadius(s, CutGrade.Perfect));
            Assert.Greater(Mathf.Abs(CutTargetGeometry.MarkPosition(s, CutGrade.Soquete, -0.5f, 0.1f, 0).x),
                CutTargetGeometry.OuterRadius);
        }

        [Test]
        public void TheLiveGrade_IsWhatATapWouldGet()
        {
            var (node, player) = StartAndWait(500);
            _now += 1.2f * node.HitWindowSeconds - node.SecondsFromBeat;
            var predicted = node.GradeIfTappedNow;
            Assert.AreEqual(predicted, node.Tap(player).Grade, "the band the bar and target light must be the grade given");
            Assert.AreEqual(CutGrade.Bad, predicted);
        }
    }
}
