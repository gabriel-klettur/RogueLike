using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Valkur.Data;
using Valkur.Gameplay.Inventory;

namespace Valkur.Tests.EditMode.Game.WorldDrops
{
    /// <summary>
    /// A drop is thrown out of the body along a hop and lands exactly on its bob baseline;
    /// picking it up flashes in its rarity's colour.
    /// </summary>
    [TestFixture]
    public class WorldPickupThrowTests
    {
        private readonly List<GameObject> _spawned = new();

        [TearDown]
        public void Cleanup()
        {
            foreach (var go in _spawned) if (go != null) Object.DestroyImmediate(go);
            _spawned.Clear();
        }

        [Test]
        public void TheThrow_RisesInTheMiddle_AndLandsExactly()
        {
            var go = new GameObject("drop");
            _spawned.Add(go);
            var pickup = go.AddComponent<WorldPickup>();
            go.transform.position = new Vector3(10f, 10f, 0f);
            var landing = new Vector3(11.5f, 9.4f, 0f);

            pickup.Launch(landing);
            Assert.IsTrue(pickup.IsArcing);

            pickup.AdvanceArc(0.21f);
            float lineY = Mathf.Lerp(10f, 9.4f, 0.75f);
            Assert.That(go.transform.position.y, Is.GreaterThan(lineY + 0.3f), "Half way, it is in the air.");

            pickup.AdvanceArc(1f);
            Assert.IsFalse(pickup.IsArcing);
            Assert.That(go.transform.position, Is.EqualTo(landing), "And it lands on the spot, no snap.");
        }

        [Test]
        public void EveryRarity_FlashesItsOwnColour()
        {
            var seen = new HashSet<Color>();
            foreach (ItemRarity r in System.Enum.GetValues(typeof(ItemRarity)))
                seen.Add(WorldPickup.RarityFlashColour(r));
            Assert.That(seen.Count, Is.EqualTo(System.Enum.GetValues(typeof(ItemRarity)).Length));
            var legendary = WorldPickup.RarityFlashColour(ItemRarity.Legendary);
            Assert.That(legendary.r, Is.GreaterThan(legendary.b), "Legendary is gold.");
        }
    }
}
