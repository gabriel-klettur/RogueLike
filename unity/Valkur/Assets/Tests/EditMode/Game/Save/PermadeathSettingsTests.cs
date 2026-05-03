using NUnit.Framework;
using Valkur.Core;

namespace Valkur.Tests.EditMode.Game.Save
{
    /// <summary>
    /// Pins the GameSettings permadeath flag: defaults to false (opt-in
    /// hardcore mode), persists through ResetToDefaults to false. Without
    /// these tests, a future settings refactor could silently flip the
    /// default to true and player runs would start deleting on death
    /// without warning.
    /// </summary>
    [TestFixture]
    public class PermadeathSettingsTests
    {
        [Test]
        public void DefaultValue_IsFalse()
        {
            var settings = new GameSettings();
            Assert.IsFalse(settings.permadeath,
                "Permadeath must default to OFF — it's an opt-in hardcore mode, " +
                "not the standard play. A True default would silently delete " +
                "every casual player's first run.");
        }

        [Test]
        public void ResetToDefaults_RestoresPermadeathToFalse()
        {
            var settings = new GameSettings();
            settings.permadeath = true;

            settings.ResetToDefaults();

            Assert.IsFalse(settings.permadeath,
                "ResetToDefaults must clear permadeath back to OFF along with " +
                "every other setting; otherwise the 'reset' button silently " +
                "preserves a player's most dangerous toggle.");
        }
    }
}
