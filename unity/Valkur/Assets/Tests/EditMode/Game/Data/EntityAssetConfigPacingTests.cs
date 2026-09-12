using NUnit.Framework;
using Valkur.Data;

namespace Valkur.Tests.EditMode.Game.Data
{
    /// <summary>
    /// <see cref="EntityAssetConfig.SetStateSpeedMultiplier"/> — the writer that sits beside
    /// <see cref="EntityAssetConfig.StateSpeedMultiplier"/> so the two cannot disagree about
    /// what an ABSENT row means.
    /// </summary>
    [TestFixture]
    public class EntityAssetConfigPacingTests
    {
        [Test]
        public void WritingAValue_AddsTheRow_AndTheReaderAgrees()
        {
            var config = new EntityAssetConfig();

            config.SetStateSpeedMultiplier("idle", 0.40f);

            Assert.That(config.statePacing.Count, Is.EqualTo(1));
            Assert.That(config.StateSpeedMultiplier("idle"), Is.EqualTo(0.40f).Within(0.0001f));
        }

        [Test]
        public void WritingTheSameStateTwice_UpdatesRatherThanAppends()
        {
            var config = new EntityAssetConfig();

            config.SetStateSpeedMultiplier("walk", 0.5f);
            config.SetStateSpeedMultiplier("WALK", 1.5f);

            Assert.That(config.statePacing.Count, Is.EqualTo(1),
                "state names are matched case-insensitively, as every other lookup here is");
            Assert.That(config.StateSpeedMultiplier("walk"), Is.EqualTo(1.5f).Within(0.0001f));
        }

        [Test]
        public void WritingOne_RemovesTheRow_BecauseThatIsWhatAnAbsentStateAlreadyMeans()
        {
            var config = new EntityAssetConfig();
            config.SetStateSpeedMultiplier("idle", 0.40f);

            config.SetStateSpeedMultiplier("idle", 1f);

            Assert.That(config.statePacing, Is.Empty,
                "a row that says 1 says nothing, and an asset full of them is what makes the " +
                "one authored row (Gatita's slow idle) impossible to spot");
            Assert.That(config.StateSpeedMultiplier("idle"), Is.EqualTo(1f));
        }

        [Test]
        public void WritingOne_OnAStateWithNoRow_AddsNothing()
        {
            var config = new EntityAssetConfig();

            config.SetStateSpeedMultiplier("death", 1f);

            Assert.That(config.statePacing, Is.Empty);
        }

        [Test]
        public void AMultiplierIsFloored_SoAnAnimationCannotBeStopped()
        {
            var config = new EntityAssetConfig();

            config.SetStateSpeedMultiplier("cast", 0f);

            Assert.That(config.StateSpeedMultiplier("cast"), Is.EqualTo(0.05f).Within(0.0001f),
                "zero is not slow, it is stopped — and the reader already floors at 0.05");
        }

        [Test]
        public void AnEmptyStateName_IsRefusedRatherThanStored()
        {
            var config = new EntityAssetConfig();

            config.SetStateSpeedMultiplier("", 0.5f);
            config.SetStateSpeedMultiplier(null, 0.5f);

            Assert.That(config.statePacing, Is.Empty);
        }
    }
}
