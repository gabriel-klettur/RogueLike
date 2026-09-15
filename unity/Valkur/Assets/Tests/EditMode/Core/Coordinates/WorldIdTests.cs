using NUnit.Framework;
using Valkur.Core.Coordinates;

namespace Valkur.Tests.EditMode.Core.Coordinates
{
    /// <summary>
    /// Pins the equality and identity contract of <see cref="WorldId"/>: two
    /// IDs with the same GUID compare equal regardless of slug, slug-only
    /// difference does not split identity, and <see cref="WorldId.Base"/> is
    /// the empty/default world.
    /// </summary>
    [TestFixture]
    public class WorldIdTests
    {
        [Test]
        public void Base_HasEmptyGuidAndBaseSlug()
        {
            Assert.AreEqual(System.Guid.Empty, WorldId.Base.Value);
            Assert.AreEqual("base", WorldId.Base.Slug);
            // Base IS initialized — it has a slug. IsEmpty is reserved for the
            // uninitialized struct default (no slug, empty Guid).
            Assert.IsFalse(WorldId.Base.IsEmpty);
        }

        [Test]
        public void Default_IsEmpty()
        {
            // The struct's default value (no slug, Guid.Empty) is the only
            // shape that satisfies IsEmpty.
            Assert.IsTrue(default(WorldId).IsEmpty);
        }

        [Test]
        public void Equality_IsByGuidNotSlug()
        {
            var g = System.Guid.NewGuid();
            var a = new WorldId(g, "alpha");
            var b = new WorldId(g, "beta");
            Assert.AreEqual(a, b);
            Assert.IsTrue(a == b);
            Assert.AreEqual(a.GetHashCode(), b.GetHashCode());
        }

        [Test]
        public void Inequality_DifferentGuid()
        {
            var a = new WorldId(System.Guid.NewGuid(), "x");
            var b = new WorldId(System.Guid.NewGuid(), "x");
            Assert.AreNotEqual(a, b);
            Assert.IsTrue(a != b);
        }

        [Test]
        public void ToString_PrefersSlugWhenSet()
        {
            var w = new WorldId(System.Guid.NewGuid(), "the_abyss");
            Assert.AreEqual("the_abyss", w.ToString());
        }
    }
}
