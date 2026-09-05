using NUnit.Framework;
using UnityEngine;
using Valkur.Core.Input;

namespace Valkur.Tests.EditMode.Game.Input
{
    /// <summary>
    /// Escape has several independent readers — the General Editor toggle, the Save Log,
    /// the character sheet, the launcher's confirm dialog — and before
    /// <see cref="EscapeOwnership"/> one press reached all of them in the same frame. These
    /// pin the arbitration, and in particular the half that is easy to lose: a release has
    /// to stay in force for the rest of the frame it happened on, because the overlay that
    /// closes on the press may run its Update BEFORE the launcher runs its own.
    /// </summary>
    [TestFixture]
    public class EscapeOwnershipTests
    {
        private readonly object _overlayA = new object();
        private readonly object _overlayB = new object();

        [SetUp]    public void SetUp()    => EscapeOwnership.ResetForTests();
        [TearDown] public void TearDown() => EscapeOwnership.ResetForTests();

        [Test]
        public void Unclaimed_ByDefault()
        {
            Assert.IsFalse(EscapeOwnership.IsClaimed);
            Assert.AreEqual(0, EscapeOwnership.OwnerCount);
        }

        [Test]
        public void Claim_HoldsEscape_UntilReleased()
        {
            EscapeOwnership.Claim(_overlayA);
            Assert.IsTrue(EscapeOwnership.IsClaimed, "An open overlay owns Escape.");
            Assert.IsTrue(EscapeOwnership.IsClaimedOn(Time.frameCount + 100),
                "A live claim holds on every future frame, not only this one.");

            EscapeOwnership.Release(_overlayA);
            Assert.AreEqual(0, EscapeOwnership.OwnerCount);
        }

        [Test]
        public void Release_StaysInForce_ForTheRestOfTheFrame_AndNoLonger()
        {
            EscapeOwnership.Claim(_overlayA);
            EscapeOwnership.Release(_overlayA);

            Assert.IsTrue(EscapeOwnership.IsClaimed,
                "The overlay that closed on this frame's press released inside its own " +
                "Update; a launcher that runs later in the same frame must still yield, or " +
                "the one press closes the overlay AND toggles the launcher.");
            Assert.IsFalse(EscapeOwnership.IsClaimedOn(Time.frameCount + 1),
                "The release is a one-frame grace, not a permanent hold.");
        }

        [Test]
        public void TwoOverlays_EscapeStaysClaimed_UntilBothRelease()
        {
            EscapeOwnership.Claim(_overlayA);
            EscapeOwnership.Claim(_overlayB);
            EscapeOwnership.Release(_overlayA);
            Assert.IsTrue(EscapeOwnership.IsClaimedOn(Time.frameCount + 1),
                "The second overlay is still open; its claim is independent of the first's.");

            EscapeOwnership.Release(_overlayB);
            Assert.IsFalse(EscapeOwnership.IsClaimedOn(Time.frameCount + 1));
        }

        [Test]
        public void Claim_IsIdempotentPerOwner()
        {
            EscapeOwnership.Claim(_overlayA);
            EscapeOwnership.Claim(_overlayA);
            Assert.AreEqual(1, EscapeOwnership.OwnerCount,
                "Claiming twice must not need releasing twice — an overlay re-shown without " +
                "being closed (Save Log's Toggle → Show on an existing instance) does exactly that.");
            EscapeOwnership.Release(_overlayA);
            Assert.IsFalse(EscapeOwnership.IsClaimedOn(Time.frameCount + 1));
        }

        [Test]
        public void Release_ByAStranger_DoesNotStampTheFrame()
        {
            EscapeOwnership.Release(_overlayB);
            Assert.IsFalse(EscapeOwnership.IsClaimed,
                "An overlay that never claimed must not be able to steal the frame by " +
                "releasing — Close() paths run on objects that were never shown.");
        }

        [Test]
        public void Null_IsIgnored()
        {
            EscapeOwnership.Claim(null);
            EscapeOwnership.Release(null);
            Assert.IsFalse(EscapeOwnership.IsClaimed);
            Assert.AreEqual(0, EscapeOwnership.OwnerCount);
        }
    }
}
