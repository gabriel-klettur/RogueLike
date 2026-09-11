using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Valkur.Core.UI;
using Valkur.Data;
using Valkur.Gameplay.Quests;

namespace Valkur.Tests.EditMode.Game.Quests
{
    /// <summary>
    /// Pins the navigation layer: the noticeboard two assemblies use to talk, the rule that
    /// a placeless objective produces no marker, and the badge states a character can show.
    ///
    /// <para><b>Why the board needs its own fixture.</b> It is the only channel between the
    /// quest layer and the minimap — `Valkur.Gameplay` and `Valkur.UI` may not reference
    /// each other — so a defect in it does not fail to compile anywhere. It fails as a map
    /// with no dots on it, which is exactly what the player saw before this existed and
    /// therefore exactly what nobody would notice had regressed.</para>
    /// </summary>
    [TestFixture]
    public class QuestNavigationTests
    {
        [SetUp]
        public void ClearBoard()
        {
            WorldMarkerBoard.Clear("quests");
            WorldMarkerBoard.Clear("other");
        }

        [TearDown]
        public void CleanUp()
        {
            WorldMarkerBoard.Clear("quests");
            WorldMarkerBoard.Clear("other");
        }

        // ── The noticeboard ────────────────────────────────────────────────

        [Test]
        public void Publish_ReplacesOnlyItsOwnChannel()
        {
            // The whole reason the board is keyed: a second publisher must not erase the
            // first, and the failure would be a marker that vanishes for no visible reason.
            WorldMarkerBoard.Publish("quests", new[]
            {
                new WorldMarker(new Vector2(1f, 2f), WorldMarkerKind.QuestOffer, "A"),
            });
            WorldMarkerBoard.Publish("other", new[]
            {
                new WorldMarker(new Vector2(9f, 9f), WorldMarkerKind.QuestObjective, "B"),
            });

            Assert.AreEqual(2, WorldMarkerBoard.All.Count);

            WorldMarkerBoard.Publish("quests", new[]
            {
                new WorldMarker(new Vector2(3f, 4f), WorldMarkerKind.QuestTurnIn, "C"),
                new WorldMarker(new Vector2(5f, 6f), WorldMarkerKind.QuestOffer, "D"),
            });

            Assert.AreEqual(3, WorldMarkerBoard.All.Count,
                "republishing one channel must leave the other channel's markers alone");
        }

        [Test]
        public void PublishingAnEmptyList_IsHowAChannelSaysNothingRightNow()
        {
            WorldMarkerBoard.Publish("quests", new[]
            {
                new WorldMarker(Vector2.zero, WorldMarkerKind.QuestOffer),
            });
            Assert.AreEqual(1, WorldMarkerBoard.All.Count);

            WorldMarkerBoard.Publish("quests", new List<WorldMarker>());
            Assert.AreEqual(0, WorldMarkerBoard.All.Count);
        }

        [Test]
        public void Clear_DropsAChannel_AndIsSafeForOneThatNeverPublished()
        {
            Assert.DoesNotThrow(() => WorldMarkerBoard.Clear("never-used"));

            WorldMarkerBoard.Publish("quests", new[]
            {
                new WorldMarker(Vector2.one, WorldMarkerKind.QuestOffer),
            });
            WorldMarkerBoard.Clear("quests");

            Assert.AreEqual(0, WorldMarkerBoard.All.Count);
        }

        [Test]
        public void AnEmptyChannelName_IsIgnoredRatherThanStored()
        {
            // A publisher with no channel is a bug at the call site; storing it under ""
            // would make it un-clearable and it would sit on the map forever.
            WorldMarkerBoard.Publish(null, new[] { new WorldMarker(Vector2.zero, WorldMarkerKind.QuestOffer) });
            WorldMarkerBoard.Publish("", new[] { new WorldMarker(Vector2.zero, WorldMarkerKind.QuestOffer) });

            Assert.AreEqual(0, WorldMarkerBoard.All.Count);
        }

        // ── What has a place, and what deliberately does not ────────────────

        [Test]
        public void PlacelessObjectiveKinds_ProduceNoMarker()
        {
            // Inventing a destination for these would be worse than none: an arrow that
            // points somewhere arbitrary teaches the player to distrust every arrow.
            var placeless = new IObjective[]
            {
                new CollectItemObjective("a", "d", 3, "iron_ore", true),
                new CraftObjective("b", "d", 1, "locro"),
                new CastSpellObjective("c", "d", 5, "fireball"),
                new SurviveObjective("d", "d", 60),
                new ReachLevelObjective("e", "d", 12),
                new EarnCoinsObjective("f", "d", 500),
            };

            foreach (var obj in placeless)
            {
                Assert.IsFalse(
                    QuestObjectiveLocator.TryLocate(obj, out _, out _),
                    obj.GetType().Name + " has no world location and must not claim one");
            }
        }

        [Test]
        public void ACompletedObjective_NeverProducesAMarker()
        {
            // A tick that stays on the map is worse than no tick: it sends the player back
            // to something they have already done.
            var reach = new ReachZoneObjective("q.obj0", "Llega al bosque", "Forest");
            reach.RestoreProgress(1);

            Assert.IsTrue(reach.IsComplete);
            Assert.IsFalse(QuestObjectiveLocator.TryLocate(reach, out _, out _));
        }

        [Test]
        public void ANullObjective_IsRefusedRatherThanThrowing()
        {
            // The publisher walks live objective lists that a pruned definition can leave
            // holes in; a throw there would take the whole marker pass down.
            Assert.DoesNotThrow(() => QuestObjectiveLocator.TryLocate(null, out _, out _));
            Assert.IsFalse(QuestObjectiveLocator.TryLocate(null, out _, out _));
        }

        [Test]
        public void AnUnknownPersona_ResolvesToNothing_RatherThanTheWorldOrigin()
        {
            // (0,0) is a real place. Answering it for something that could not be found
            // would plant a marker in the corner of the map and send the player there.
            Assert.IsFalse(QuestObjectiveLocator.TryLocatePersona("nobody_at_all", out _, out _));
        }

        // ── Badge states ───────────────────────────────────────────────────

        [Test]
        public void BadgeStates_AreOrderedByUrgency()
        {
            // The publisher keeps the HIGHEST state for a character who is several things
            // at once, so this order is load-bearing rather than cosmetic — and the first
            // cut had it wrong: InProgress outranked TurnIn, which would have hidden a
            // finished quest's reward behind the one glyph the player can do nothing with.
            Assert.Less((int)QuestBadgeState.None, (int)QuestBadgeState.InProgress);
            Assert.Less((int)QuestBadgeState.InProgress, (int)QuestBadgeState.Offer,
                "new work outranks work already accepted");
            Assert.Less((int)QuestBadgeState.Offer, (int)QuestBadgeState.TurnIn,
                "a reward waiting to be collected outranks everything");
        }

        [Test]
        public void EveryBadgeState_HasItsOwnGlyph()
        {
            var offer = QuestBadgeSprites.For(QuestBadgeState.Offer);
            var turnIn = QuestBadgeSprites.For(QuestBadgeState.TurnIn);
            var progress = QuestBadgeSprites.For(QuestBadgeState.InProgress);

            Assert.IsNotNull(offer);
            Assert.IsNotNull(turnIn);
            Assert.IsNotNull(progress);

            // Three distinct shapes, because the colour alone is not enough: the marks are
            // small, and a colour-blind player would see one glyph in three greys.
            Assert.AreNotSame(offer, turnIn);
            Assert.AreNotSame(turnIn, progress);
            Assert.AreNotSame(offer, progress);
        }

        [Test]
        public void GlyphsAreCached_SoABadgeRepaintCostsNoTexture()
        {
            // SetState is called twice a second for every persona in the world.
            var first = QuestBadgeSprites.For(QuestBadgeState.Offer);
            var second = QuestBadgeSprites.For(QuestBadgeState.Offer);
            Assert.AreSame(first, second);
        }

        [Test]
        public void GlyphsUseFullRect_NotTheTightDefault()
        {
            // Free on a 16x32 texture, and the habit is the point: the same call against an
            // atlas page cost this project 60 % of its boot.
            var sprite = QuestBadgeSprites.For(QuestBadgeState.Offer);
            Assert.AreEqual(4, sprite.vertices.Length,
                "a FullRect sprite is a quad; a tight-meshed one has an outline-fitted mesh");
        }

        // ── Locked offers ──────────────────────────────────────────────────

        [Test]
        public void LockedFor_ListsWhatTheCharacterWouldGive_IfThePlayerWereFurtherAlong()
        {
            var host = new GameObject("QuestServiceHost");
            try
            {
                var service = host.AddComponent<QuestService>();

                var open = ScriptableObject.CreateInstance<QuestDefinition>();
                open.questId = "q_open";
                open.displayName = "Abierta";
                open.giverPersonaId = "smith";
                open.objectives = new[] { Entry(ObjectiveKind.Talk, "smith") };

                var gated = ScriptableObject.CreateInstance<QuestDefinition>();
                gated.questId = "q_gated";
                gated.displayName = "Cerrada";
                gated.giverPersonaId = "smith";
                gated.requiredLevel = 40;
                gated.objectives = new[] { Entry(ObjectiveKind.Talk, "smith") };

                var catalog = ScriptableObject.CreateInstance<QuestCatalog>();
                catalog.quests.Add(open);
                catalog.quests.Add(gated);
                service.SetCatalog(catalog);

                var offers = new List<QuestDefinition>();
                var locked = new List<QuestDefinition>();
                service.OffersFor("smith", offers);
                service.LockedFor("smith", locked);

                CollectionAssert.Contains(offers, open);
                CollectionAssert.DoesNotContain(offers, gated);

                CollectionAssert.Contains(locked, gated,
                    "a level-gated quest must be SHOWN as locked — filtered out, it is " +
                    "indistinguishable from one that does not exist");
                CollectionAssert.DoesNotContain(locked, open);

                StringAssert.Contains("40", service.DescribeLock(gated),
                    "the locked row has to say what the player is waiting for");
                Assert.IsEmpty(service.DescribeLock(open));

                Object.DestroyImmediate(open);
                Object.DestroyImmediate(gated);
                Object.DestroyImmediate(catalog);
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void AnAcceptedQuest_IsNeitherOfferedNorLocked()
        {
            // It belongs to the in-progress section, and appearing in two places at once is
            // how a player ends up looking at a row that cannot do anything.
            var host = new GameObject("QuestServiceHost");
            try
            {
                var service = host.AddComponent<QuestService>();

                var def = ScriptableObject.CreateInstance<QuestDefinition>();
                def.questId = "q_taken";
                def.displayName = "Tomada";
                def.giverPersonaId = "smith";
                def.objectives = new[] { Entry(ObjectiveKind.Craft, "locro") };

                var catalog = ScriptableObject.CreateInstance<QuestCatalog>();
                catalog.quests.Add(def);
                service.SetCatalog(catalog);

                Assert.IsTrue(service.Manager.StartQuest(def));

                var offers = new List<QuestDefinition>();
                var locked = new List<QuestDefinition>();
                var active = new List<QuestDefinition>();
                service.OffersFor("smith", offers);
                service.LockedFor("smith", locked);
                service.ActiveFrom("smith", active);

                CollectionAssert.DoesNotContain(offers, def);
                CollectionAssert.DoesNotContain(locked, def);
                CollectionAssert.Contains(active, def);

                Object.DestroyImmediate(def);
                Object.DestroyImmediate(catalog);
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void Abandon_DropsTheQuest_AndRefusesOneThatIsNotActive()
        {
            var host = new GameObject("QuestServiceHost");
            try
            {
                var service = host.AddComponent<QuestService>();

                var def = ScriptableObject.CreateInstance<QuestDefinition>();
                def.questId = "q_drop";
                def.displayName = "Soltar";
                def.objectives = new[] { Entry(ObjectiveKind.Craft, "locro") };

                var catalog = ScriptableObject.CreateInstance<QuestCatalog>();
                catalog.quests.Add(def);
                service.SetCatalog(catalog);

                Assert.IsFalse(service.Abandon("q_drop"), "nothing to abandon yet");

                service.Manager.StartQuest(def);
                Assert.IsTrue(service.Manager.IsActive("q_drop"));

                Assert.IsTrue(service.Abandon("q_drop"));
                Assert.IsFalse(service.Manager.IsActive("q_drop"));
                Assert.IsFalse(service.Manager.IsCompleted("q_drop"),
                    "abandoning is not completing — the quest must be takeable again");

                Object.DestroyImmediate(def);
                Object.DestroyImmediate(catalog);
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        private static ObjectiveEntry Entry(ObjectiveKind kind, string target) =>
            new ObjectiveEntry { kind = kind, targetId = target, count = 1 };
    }
}
