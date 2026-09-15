using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Valkur.Core.UI;
using Valkur.Data;
using Valkur.Gameplay;
using Valkur.Gameplay.NPC;
using Valkur.UI.HUD;

namespace Valkur.Tests.EditMode.UI.HUD.Minimap
{
    /// <summary>
    /// The composition of the map: what each entity is drawn as, what hides under the fog, and
    /// what is drawn over what. Each of these shipped wrong in the minimap this replaced, and
    /// each was invisible to its unit tests because every half looked right alone:
    /// <list type="bullet">
    /// <item>the six vendors were red enemy dots, because every NPC registered as a Monster;</item>
    /// <item>every quest in town was invisible, drawn UNDER the vendor square that offered it;</item>
    /// <item>and every creature in the world showed through unexplored fog.</item>
    /// </list>
    /// </summary>
    public class MinimapCompositionTests
    {
        private readonly List<GameObject> _made = new List<GameObject>();
        private const string Channel = "tests.minimap.composition";

        [SetUp] public void SetUp() => LogAssert.ignoreFailingMessages = true;

        [TearDown]
        public void TearDown()
        {
            WorldMarkerBoard.Clear(Channel);
            foreach (var go in _made) if (go != null) Object.DestroyImmediate(go);
            _made.Clear();
            LogAssert.ignoreFailingMessages = false;
        }

        private GameObject Make(string name, Vector2 at)
        {
            var go = new GameObject(name);
            go.transform.position = at;
            _made.Add(go);
            return go;
        }

        private static MinimapDot AddDot(GameObject go, MinimapDotType type)
        {
            var dot = go.AddComponent<MinimapDot>();
            dot.Configure(type, Color.white);
            MinimapManager.Register(dot);       // OnEnable does not run for AddComponent in Edit Mode
            return dot;
        }

        // ── Classification ──────────────────────────────────────────────────

        [Test]
        public void ANeutralFaction_IsAVillager_EvenWhenRegisteredAsAMonster()
        {
            var go = Make("villager", Vector2.zero);
            go.AddComponent<EntityFaction>().SetAuthoredFaction("NEUTRAL");
            Assert.AreEqual(MinimapEntityKind.Neutral, MinimapEntityClassifier.Classify(go, MinimapDotType.Monster));
        }

        [Test]
        public void AVendor_IsAVendor_NotAnEnemy()
        {
            var go = Make("smith", Vector2.zero);
            go.AddComponent<VendorNPC>();
            Assert.AreEqual(MinimapEntityKind.Vendor, MinimapEntityClassifier.Classify(go, MinimapDotType.Monster));
        }

        [Test]
        public void AHostileWithNoFactionComponent_IsAnEnemy()
        {
            var go = Make("barbol", Vector2.zero);
            Assert.AreEqual(MinimapEntityKind.Enemy, MinimapEntityClassifier.Classify(go, MinimapDotType.Monster));
        }

        [Test]
        public void TheDead_AreNotDrawn()
        {
            var go = Make("corpse", Vector2.zero);
            go.AddComponent<Health>().Initialize(10, 0);
            Assert.AreEqual(MinimapEntityKind.Hidden, MinimapEntityClassifier.Classify(go, MinimapDotType.Monster));
        }

        [TestCase("blacksmith", MinimapIcon.Hammer)]
        [TestCase("lumberjack", MinimapIcon.Axe)]
        [TestCase("food", MinimapIcon.Bowl)]
        [TestCase("mineral", MinimapIcon.Pickaxe)]
        [TestCase("alchemy", MinimapIcon.Flask)]
        [TestCase("magic", MinimapIcon.Star)]
        [TestCase(null, MinimapIcon.Coin)]
        public void EveryShippedTrade_HasItsOwnIcon(string trade, MinimapIcon expected)
        {
            Assert.AreEqual(expected, MinimapEntityClassifier.IconForTrade(trade));
        }

        // ── Scene ───────────────────────────────────────────────────────────

        [Test]
        public void AQuestMark_IsDrawnAfterTheVendorWhoOffersIt()
        {
            var at = new Vector2(500f, 500f);
            var vendor = Make("gatita", at);
            vendor.AddComponent<VendorNPC>();
            var dot = AddDot(vendor, MinimapDotType.NPC);
            WorldMarkerBoard.Publish(Channel, new[] { new WorldMarker(at, WorldMarkerKind.QuestOffer) });

            try
            {
                var scene = new MinimapScene();
                scene.Collect(MinimapStyle.Active, 0f);
                int vendorIndex = -1, questIndex = -1;
                for (int i = 0; i < scene.Items.Count; i++)
                {
                    var it = scene.Items[i];
                    if (Vector2.Distance(it.World, at) > 0.01f) continue;
                    if (it.Icon == MinimapIcon.Exclaim) questIndex = i;
                    else vendorIndex = i;
                }
                Assert.That(vendorIndex, Is.GreaterThanOrEqualTo(0), "the vendor is on the map");
                Assert.That(questIndex, Is.GreaterThan(vendorIndex), "the quest mark is drawn OVER its giver");
                Assert.IsTrue(scene.Items[questIndex].Badge, "and lifted above them");
            }
            finally { MinimapManager.Unregister(dot); }
        }

        [Test]
        public void TheFog_HidesCreatures_RemembersPlaces_AndAlwaysShowsQuests()
        {
            var monster = Make("monster", new Vector2(900f, 900f));
            var monsterDot = AddDot(monster, MinimapDotType.Monster);
            var shop = Make("shop", new Vector2(901f, 900f));
            shop.AddComponent<VendorNPC>();
            var shopDot = AddDot(shop, MinimapDotType.NPC);
            WorldMarkerBoard.Publish(Channel, new[] { new WorldMarker(new Vector2(902f, 900f), WorldMarkerKind.QuestTurnIn) });

            try
            {
                var scene = new MinimapScene();
                scene.Collect(MinimapStyle.Active, 0f);
                MinimapReveal monsterReveal = default, shopReveal = default, questReveal = default;
                bool m = false, s = false, q = false;
                foreach (var it in scene.Items)
                {
                    if (Vector2.Distance(it.World, monster.transform.position) < 0.01f) { monsterReveal = it.Reveal; m = true; }
                    else if (Vector2.Distance(it.World, shop.transform.position) < 0.01f) { shopReveal = it.Reveal; s = true; }
                    else if (it.Icon == MinimapIcon.Question) { questReveal = it.Reveal; q = true; }
                }
                Assert.IsTrue(m && s && q, "all three collected");
                Assert.AreEqual(MinimapReveal.Seen, monsterReveal, "a creature moves: only what is seen is known");
                Assert.AreEqual(MinimapReveal.Explored, shopReveal, "a shop stays where it was found");
                Assert.AreEqual(MinimapReveal.Always, questReveal, "the errand told the player where to go");
            }
            finally
            {
                MinimapManager.Unregister(monsterDot);
                MinimapManager.Unregister(shopDot);
            }
        }

        [Test]
        public void TheTurnIn_IsPinnedToTheRim_WhenOutOfView()
        {
            WorldMarkerBoard.Publish(Channel, new[] { new WorldMarker(new Vector2(-900f, 10f), WorldMarkerKind.QuestTurnIn) });
            var scene = new MinimapScene();
            scene.Collect(MinimapStyle.Active, 0f);
            bool found = false;
            foreach (var it in scene.Items)
                if (it.Icon == MinimapIcon.Question) { found = true; Assert.IsTrue(it.EdgePin); }
            Assert.IsTrue(found);
        }

        [Test]
        public void TheWaypoint_IsCollected_AndClearedOnArrival()
        {
            MinimapWaypoint.Set(new Vector2(40f, 40f));
            try
            {
                var scene = new MinimapScene();
                scene.Collect(MinimapStyle.Active, 0f);
                bool pin = false;
                foreach (var it in scene.Items) if (it.Icon == MinimapIcon.Pin) pin = it.EdgePin;
                Assert.IsTrue(pin, "a pinned destination is always findable");

                Assert.IsFalse(MinimapWaypoint.ClearIfReached(new Vector2(0f, 0f)));
                Assert.IsTrue(MinimapWaypoint.ClearIfReached(new Vector2(41f, 39f)));
                Assert.IsFalse(MinimapWaypoint.HasWaypoint);
            }
            finally { MinimapWaypoint.Clear(); }
        }
    }
}
