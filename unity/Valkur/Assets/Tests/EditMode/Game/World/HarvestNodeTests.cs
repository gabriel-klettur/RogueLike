using NUnit.Framework;
using UnityEngine;
using Valkur.Data;
using Valkur.Gameplay.Interaction;
using Valkur.Gameplay.World;

namespace Valkur.Tests.EditMode.Game.World
{
    /// <summary>
    /// Pins the node's own state machine: what it offers the player, when it stops offering
    /// it, and what a restore from the save layer is allowed to change.
    ///
    /// <para>The regrow tests are the ones with history. A spent seam that recomputes its
    /// deadline on restore comes back with its full timer running again — and does it once
    /// more on every load, so the seam can never actually return. It was measured live before
    /// <c>RestoreSpent</c> existed.</para>
    /// </summary>
    [TestFixture]
    public class HarvestNodeTests
    {
        private const float RegrowSeconds = 240f;

        private DestructionProfile _profile;
        private GameObject _go;
        private HarvestNode _node;

        private DestructionProfile MakeProfile(HarvestMode mode, bool harvestable = true)
        {
            var profile = ScriptableObject.CreateInstance<DestructionProfile>();
            profile.material = MaterialClass.Stone;
            profile.harvestable = harvestable;
            profile.harvestMode = mode;
            profile.harvestVerb = "Mine";
            profile.charges = 5;
            profile.durability = 40;
            profile.secondsPerBlow = 0.7f;
            profile.blowDamage = 10;
            profile.interactionRadius = 2f;
            profile.regrowSeconds = RegrowSeconds;
            return profile;
        }

        private HarvestNode MakeNode(DestructionProfile profile)
        {
            _profile = profile;
            _go = new GameObject("Node");
            _node = _go.AddComponent<HarvestNode>();
            _node.Initialize(profile, building: null, durability: null);
            return _node;
        }

        [TearDown]
        public void TearDown()
        {
            // The fixture unregisters EXPLICITLY, and must: in Edit Mode a component added
            // outside Play never receives Awake, and Unity skips the matching OnDestroy for
            // the same reason — so the node's own teardown, which is what leaves the registry
            // in a real session, does not run here at all. Leaning on it left every node from
            // every test in a static list that has no per-test reset (its own reset is a
            // SubsystemRegistration hook, which fires on entering Play and never between
            // tests), and the count climbed for the rest of the run.
            if (_node != null)
            {
                InteractableRegistry.Unregister(_node);
                HarvestSwingRegistry.Unregister(_node);
            }
            if (_go != null) Object.DestroyImmediate(_go);
            if (_profile != null) Object.DestroyImmediate(_profile);

            _node = null;
            _go = null;
            _profile = null;

            Assert.That(InteractableRegistry.Count, Is.Zero, "Node leaked into the registry.");
            Assert.That(HarvestSwingRegistry.Count, Is.Zero, "Node leaked into the swing registry.");
        }

        // Registration -----------------------------------------------------------------

        [Test]
        public void AHarvestableNode_EntersTheInteractableRegistry()
        {
            MakeNode(MakeProfile(HarvestMode.Deplete));
            Assert.That(InteractableRegistry.Count, Is.EqualTo(1));
        }

        [Test]
        public void ANonHarvestableProfile_NeverEntersTheRegistry()
        {
            MakeNode(MakeProfile(HarvestMode.Destroy, harvestable: false));
            Assert.That(InteractableRegistry.Count, Is.Zero,
                "A building can be destructible without being workable by hand. Registering it " +
                "would make every barricade in the world offer the player a prompt.");
        }

        [Test]
        public void AnIdleNode_DoesNotTick()
        {
            var node = MakeNode(MakeProfile(HarvestMode.Deplete));
            Assert.That(node.enabled, Is.False,
                "Unity pays a managed-to-native call per Update per component. A forest is " +
                "hundreds of these and none of them has anything to do until it is worked.");
        }

        // What it offers ---------------------------------------------------------------

        [Test]
        public void AFreshDepleteNode_OffersItselfAtFullCharge()
        {
            var node = MakeNode(MakeProfile(HarvestMode.Deplete));

            Assert.That(node.CanInteract(null), Is.True);
            Assert.That(node.ChargesRemaining, Is.EqualTo(5));
            Assert.That(node.RemainingFraction, Is.EqualTo(1f).Within(0.0001f));
            var prompt = node.DescribePrompt(null);
            Assert.That(prompt.Availability, Is.EqualTo(InteractionAvailability.Ready));
            Assert.That(prompt.Verb, Is.EqualTo("Mine"));
            Assert.That(node.IsInteracting, Is.False);
        }

        [Test]
        public void ASpentNode_StopsOfferingItself()
        {
            var node = MakeNode(MakeProfile(HarvestMode.Deplete));
            node.RestoreCharges(0);

            Assert.That(node.IsSpent, Is.True);
            Assert.That(node.CanInteract(null), Is.False);
            Assert.That(node.RemainingFraction, Is.EqualTo(0f).Within(0.0001f));

            // It keeps a prompt, and the prompt is what makes the refusal legible. A spent
            // seam that showed nothing would be indistinguishable from a decorative rock.
            var prompt = node.DescribePrompt(null);
            Assert.That(prompt.Availability, Is.EqualTo(InteractionAvailability.Blocked));
            Assert.That(prompt.IsVisible, Is.True);
            Assert.That(prompt.IsActionable, Is.False);
            Assert.That(prompt.Detail, Is.Not.Empty,
                "A blocked prompt without a reason is just a greyed-out word.");
        }

        [Test]
        public void ASpentNode_CountsDownToItsOwnRegrow()
        {
            var node = MakeNode(MakeProfile(HarvestMode.Deplete));
            node.RestoreSpent(0, WorldDamageService.UnixNow() + 125d);

            var prompt = node.DescribePrompt(null);
            Assert.That(prompt.Detail, Does.Contain("2:0"),
                "Two minutes and five seconds should read as a clock, not as '125 s' — a " +
                "number the player has to convert before it means anything.");
        }

        [Test]
        public void ASpentNodeThatNeverRefills_SaysSoInsteadOfShowingAFrozenClock()
        {
            var profile = MakeProfile(HarvestMode.Deplete);
            profile.regrowSeconds = 0f;
            var node = MakeNode(profile);

            node.RestoreCharges(0);

            var prompt = node.DescribePrompt(null);
            Assert.That(prompt.Availability, Is.EqualTo(InteractionAvailability.Blocked));
            Assert.That(prompt.Detail, Does.Not.Contain(":"),
                "A countdown that never moves is worse than no countdown.");
        }

        [Test]
        public void AToolGatedNode_StillOffersTheKeyToAPlayerWithoutTheTool()
        {
            var profile = MakeProfile(HarvestMode.Deplete);
            profile.requiredToolTier = 2;
            profile.chipDamageFraction = 0.15f;
            var node = MakeNode(profile);

            var prompt = node.DescribePrompt(null);

            Assert.That(prompt.Availability, Is.EqualTo(InteractionAvailability.Ready),
                "A tool tier is a statement about speed, not permission. Blocking the key " +
                "here turns 'this is hard work without a pick' into 'you may not touch this'.");
            Assert.That(prompt.IsActionable, Is.True);
            Assert.That(prompt.Detail, Is.Not.Empty,
                "The player still has to be told WHY it will crawl, or forty slow blows read " +
                "as a broken node rather than as the wrong tool.");
        }

        [Test]
        public void ANodeNothingCanTouch_RefusesTheKeyRatherThanCrawling()
        {
            // The other end of the same axis: a chip fraction of exactly zero is a deliberate
            // immunity, and there the prompt must say no rather than start a shift that can
            // never finish.
            var profile = MakeProfile(HarvestMode.Deplete);
            profile.requiredToolTier = 2;
            profile.chipDamageFraction = 0f;
            var node = MakeNode(profile);

            var prompt = node.DescribePrompt(null);

            Assert.That(prompt.Availability, Is.EqualTo(InteractionAvailability.Blocked));
            Assert.That(prompt.IsActionable, Is.False);
        }

        // Swinging at a seam ------------------------------------------------------------

        [Test]
        public void ADepleteNode_IsReachableBySwingsButNotByTheObstacleInterface()
        {
            var node = MakeNode(MakeProfile(HarvestMode.Deplete));

            Assert.That(node.AcceptsSwing, Is.True);
            Assert.That(HarvestSwingRegistry.Count, Is.EqualTo(1));

            // The interface is the projectile's door, not the swing's. Projectile resolves it
            // with GetComponentInParent off the collider that was hit, so a seam implementing
            // it could be emptied by any stray fireball that clipped the building — no
            // proximity, no arc, no session. That is what the separate registry buys.
            Assert.That(node, Is.Not.InstanceOf<Valkur.Gameplay.Combat.IDestructibleObstacle>(),
                "A harvest seam must never implement IDestructibleObstacle.");
        }

        [Test]
        public void ADestroyNode_StaysOutOfTheSwingRegistry()
        {
            MakeNode(MakeProfile(HarvestMode.Destroy));

            Assert.That(HarvestSwingRegistry.Count, Is.Zero,
                "A tree already takes swings through its own BuildingDurability. A second " +
                "path to the same building would work it twice per swing.");
        }

        [Test]
        public void SwingingWithTheRightTool_FreesChargesFasterThanWithTheWrongOne()
        {
            int WithTool(DamageClass toolClass, int toolTier, int weaponDamage)
            {
                var profile = MakeProfile(HarvestMode.Deplete);
                profile.requiredToolTier = 1;
                var node = MakeNode(profile);

                var attacker = new GameObject("Swinger");
                var inventory = attacker.AddComponent<Valkur.Gameplay.Inventory.Inventory>();
                var tool = ScriptableObject.CreateInstance<ItemDefinition>();
                tool.itemId = "probe_tool";
                tool.toolClass = toolClass;
                tool.toolTier = toolTier;
                inventory.SetEquipmentSlot(0, tool, 1);

                int swings = 0;
                while (node.ChargesRemaining > 0 && swings < 3000)
                {
                    node.ApplySwing(weaponDamage, attacker, Vector2.zero, null);
                    swings++;
                }

                Object.DestroyImmediate(attacker);
                Object.DestroyImmediate(tool);
                TearDown();
                return swings;
            }

            int withPick = WithTool(DamageClass.Pick, 1, 8);
            int withBlade = WithTool(DamageClass.Blade, 1, 15);

            Assert.That(withPick, Is.LessThan(withBlade),
                "The pick has to be worth carrying even though the sword hits harder.");

            // The wrong tool costs MORE BLOWS, never slower ones, and it terminates. That is
            // the same guarantee a tree gives and it comes from the same arithmetic: the
            // matrix scales the blow's damage and HarvestBlowResolver.Scale stops a real
            // multiplier rounding to nothing, so the worst case is one work per blow.
            //
            // This assertion used to cap the spread at 4x, which encoded a rate clamp that no
            // longer exists — and the clamp was the thing making mining behave unlike
            // chopping. Measured on the shipped data the two now agree: axe-vs-bare on a tree
            // is 4 blows against 40, pick-vs-sword on a seam is 14 against 42.
            var profile = MakeProfile(HarvestMode.Deplete);
            int totalWork = profile.charges * profile.blowDamage;
            Object.DestroyImmediate(profile);

            Assert.That(withBlade, Is.LessThanOrEqualTo(totalWork),
                "One work per blow is the floor, so no tool can ever need more blows than the " +
                "seam holds work. More than that means a blow is landing zero.");
        }

        [Test]
        public void SwingingBanksPartialWorkRatherThanDiscardingIt()
        {
            var profile = MakeProfile(HarvestMode.Deplete);
            profile.blowDamage = 100;
            var node = MakeNode(profile);

            int before = node.ChargesRemaining;
            node.ApplySwing(1, null, Vector2.zero, null);

            Assert.That(node.ChargesRemaining, Is.EqualTo(before),
                "One tiny swing must not free a charge worth a hundred work.");
            Assert.That(node.ChargeProgress, Is.GreaterThan(0f),
                "It must still bank progress. Discarding a swing that lands under the " +
                "threshold makes a whole band of weapons do literally nothing, and the player " +
                "has no way to tell that band from being immune.");
        }

        [Test]
        public void AWorkingNode_OffersTheWayOutRatherThanTheWayIn()
        {
            var node = MakeNode(MakeProfile(HarvestMode.Deplete));
            node.BeginInteraction(null);

            var prompt = node.DescribePrompt(null);
            Assert.That(prompt.Availability, Is.EqualTo(InteractionAvailability.Busy));
            Assert.That(prompt.IsActionable, Is.True,
                "The key still does something while a session runs: it stops it.");
            Assert.That(prompt.Detail, Does.Contain("carga"),
                "A shift in progress should say how much is left.");
        }

        [Test]
        public void RestoreCharges_ClampsToTheProfileRatherThanTrustingTheSave()
        {
            var node = MakeNode(MakeProfile(HarvestMode.Deplete));

            node.RestoreCharges(500);
            Assert.That(node.ChargesRemaining, Is.EqualTo(5),
                "A profile rebalanced downward since the run was saved would otherwise leave a " +
                "node holding more than a full one.");

            node.RestoreCharges(-10);
            Assert.That(node.ChargesRemaining, Is.EqualTo(0));
        }

        // The regrow clock -------------------------------------------------------------

        [Test]
        public void GoingSpent_ArmsAWallClockDeadline()
        {
            var node = MakeNode(MakeProfile(HarvestMode.Deplete));
            double before = WorldDamageService.UnixNow();

            node.RestoreCharges(0);

            Assert.That(node.RegrowAtUnix, Is.GreaterThanOrEqualTo(before + RegrowSeconds));
            Assert.That(node.enabled, Is.True,
                "A spent node with a pending regrow is the one case that HAS to keep ticking.");
        }

        [Test]
        public void RestoreSpent_KeepsTheSavedDeadlineInsteadOfRecomputingIt()
        {
            var node = MakeNode(MakeProfile(HarvestMode.Deplete));

            // Emptied five minutes before the player quit, with sixty seconds left to run.
            double saved = WorldDamageService.UnixNow() + 60d;
            node.RestoreSpent(0, saved);

            Assert.That(node.IsSpent, Is.True);
            Assert.That(node.RegrowAtUnix, Is.EqualTo(saved).Within(0.001d),
                "RestoreCharges alone necessarily enters the spent state, and entering it " +
                "computes a FRESH deadline — so the seam came back with its full timer " +
                "restarted, and did it again on every load.");
        }

        [Test]
        public void RestoreSpent_AcceptsADeadlineThatHasAlreadyPassed()
        {
            var node = MakeNode(MakeProfile(HarvestMode.Deplete));

            // The normal case after a long absence. It must survive as a past deadline so the
            // live regrow path brings the node back, rather than a second restore path that
            // would drift from it.
            double passed = WorldDamageService.UnixNow() - 10d;
            node.RestoreSpent(0, passed);

            Assert.That(node.RegrowAtUnix, Is.EqualTo(passed).Within(0.001d));
            Assert.That(node.enabled, Is.True);
        }

        [Test]
        public void RestoreSpent_OnANodeThatStillHasChargesLeavesTheClockAlone()
        {
            var node = MakeNode(MakeProfile(HarvestMode.Deplete));

            node.RestoreSpent(3, WorldDamageService.UnixNow() + 999d);

            Assert.That(node.IsSpent, Is.False);
            Assert.That(node.ChargesRemaining, Is.EqualTo(3));
            Assert.That(node.RegrowAtUnix, Is.EqualTo(0d),
                "A node with charges is not waiting for anything; carrying a deadline would " +
                "make it look spent to the save layer.");
            Assert.That(node.enabled, Is.False);
        }

        [Test]
        public void AProfileWithNoRegrow_ArmsNoDeadlineAndStopsTicking()
        {
            var profile = MakeProfile(HarvestMode.Deplete);
            profile.regrowSeconds = 0f;
            var node = MakeNode(profile);

            node.RestoreCharges(0);

            Assert.That(node.IsSpent, Is.True);
            Assert.That(node.RegrowAtUnix, Is.EqualTo(0d));
            Assert.That(node.enabled, Is.False,
                "Nothing is going to happen to a seam that never refills, so it must not cost " +
                "an Update for the rest of the session.");
        }

        // Mode -------------------------------------------------------------------------

        [Test]
        public void ADestroyNodeWithoutDurability_ReportsNothingRemainingRatherThanThrowing()
        {
            // Destroy mode reads a durability component it does not own. The loader always
            // gives it one, but a programmatic spawn or a half-built prefab may not.
            var node = MakeNode(MakeProfile(HarvestMode.Destroy));

            Assert.That(node.Mode, Is.EqualTo(HarvestMode.Destroy));
            Assert.That(node.RemainingFraction, Is.EqualTo(0f));
            Assert.That(node.CanInteract(null), Is.False);
        }

        [Test]
        public void BeginInteraction_IsRefusedOnANodeThatCannotBeWorked()
        {
            var node = MakeNode(MakeProfile(HarvestMode.Deplete));
            node.RestoreCharges(0);

            node.BeginInteraction(null);

            Assert.That(node.IsInteracting, Is.False);
            Assert.That(node.SessionBlows, Is.Zero);
        }
    }
}
