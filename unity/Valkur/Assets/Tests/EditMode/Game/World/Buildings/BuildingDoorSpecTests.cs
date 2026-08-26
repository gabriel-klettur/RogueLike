using NUnit.Framework;
using UnityEngine;
using Valkur.Data;

namespace Valkur.Tests.EditMode.Game.World.Buildings
{
    /// <summary>
    /// Pins <see cref="BuildingDoorSpec"/> — the per-instance half of a door.
    ///
    /// Two properties matter beyond the obvious: an empty destination must read as INVALID
    /// (an inert trigger on a doorway looks like a broken door, so the factory refuses to
    /// attach one), and <see cref="BuildingDoorSpec.Clone"/> must be a real copy (the spec
    /// travels from a parsed record to a live scene object and back to the serializer, and a
    /// shared instance would let an editor edit rewrite data the loader still believes is
    /// pristine).
    /// </summary>
    [TestFixture]
    public class BuildingDoorSpecTests
    {
        [Test]
        public void IsValid_RequiresANonBlankTarget()
        {
            Assert.IsFalse(new BuildingDoorSpec().IsValid, "A default spec leads nowhere.");
            Assert.IsFalse(new BuildingDoorSpec { target = "" }.IsValid);
            Assert.IsFalse(new BuildingDoorSpec { target = "   " }.IsValid,
                "Whitespace is what an author leaves behind when they clear the field.");
            Assert.IsFalse(new BuildingDoorSpec { target = null }.IsValid);

            Assert.IsTrue(new BuildingDoorSpec { target = "house_a_int.overlay.json" }.IsValid);
        }

        [Test]
        public void IsValid_IgnoresTheOtherFields()
        {
            // A door that leads somewhere is valid even with everything else at defaults —
            // useDefaultSpawn exists precisely so a destination needs no coordinates.
            var spec = new BuildingDoorSpec { target = "x.overlay.json", useDefaultSpawn = true };

            Assert.IsTrue(spec.IsValid);
        }

        [Test]
        public void SpawnPosition_MirrorsTheSerializedComponents()
        {
            var spec = new BuildingDoorSpec { target = "x.overlay.json", spawnX = 25.5f, spawnY = -3.25f };

            Assert.AreEqual(new Vector2(25.5f, -3.25f), spec.SpawnPosition);
        }

        [Test]
        public void Clone_CopiesEveryField()
        {
            var source = new BuildingDoorSpec
            {
                target          = "cave.overlay.json",
                useDefaultSpawn = true,
                spawnX          = 12.5f,
                spawnY          = 4.25f,
                prompt          = "Enter the cave",
            };

            var copy = source.Clone();

            Assert.AreEqual(source.target,          copy.target);
            Assert.AreEqual(source.useDefaultSpawn, copy.useDefaultSpawn);
            Assert.AreEqual(source.spawnX,          copy.spawnX);
            Assert.AreEqual(source.spawnY,          copy.spawnY);
            Assert.AreEqual(source.prompt,          copy.prompt);
        }

        [Test]
        public void Clone_IsIndependentOfItsSource()
        {
            var source = new BuildingDoorSpec { target = "a.overlay.json", spawnX = 1f };

            var copy = source.Clone();
            copy.target = "b.overlay.json";
            copy.spawnX = 99f;

            Assert.AreEqual("a.overlay.json", source.target,
                "Mutating a clone must not reach back into the record the loader parsed.");
            Assert.AreEqual(1f, source.spawnX);
            Assert.AreNotSame(source, copy);
        }
    }
}
