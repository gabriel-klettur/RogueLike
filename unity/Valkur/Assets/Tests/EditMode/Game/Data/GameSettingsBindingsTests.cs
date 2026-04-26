using NUnit.Framework;
using Valkur.Core;

namespace Valkur.Tests.EditMode.Game.Data
{
    public class GameSettingsBindingsTests
    {
        [Test]
        public void AllActions_IncludesCoreBindings()
        {
            var list = GameSettingsBindings.AllActions;
            Assert.Contains("move_up", (System.Collections.ICollection)list);
            Assert.Contains("move_down", (System.Collections.ICollection)list);
            Assert.Contains("dash", (System.Collections.ICollection)list);
            Assert.Contains("spell_1", (System.Collections.ICollection)list);
        }

        [Test]
        public void Get_ReturnsDefaultValue()
        {
            var gs = new GameSettings();
            Assert.AreEqual("w", GameSettingsBindings.Get(gs, "move_up", 0));
            Assert.AreEqual("UpArrow", GameSettingsBindings.Get(gs, "move_up", 1));
        }

        [Test]
        public void Set_UpdatesBackingField()
        {
            var gs = new GameSettings();
            Assert.IsTrue(GameSettingsBindings.Set(gs, "move_up", 0, "z"));
            Assert.AreEqual("z", gs.moveUpKeyA);
            Assert.AreEqual("z", GameSettingsBindings.Get(gs, "move_up", 0));
        }

        [Test]
        public void Set_UnknownAction_ReturnsFalse()
        {
            var gs = new GameSettings();
            Assert.IsFalse(GameSettingsBindings.Set(gs, "does_not_exist", 0, "x"));
        }

        [Test]
        public void GetBindingCount_MovementHasTwo_SpellsHaveOne()
        {
            Assert.AreEqual(2, GameSettingsBindings.GetBindingCount("move_up"));
            Assert.AreEqual(1, GameSettingsBindings.GetBindingCount("spell_1"));
            Assert.AreEqual(0, GameSettingsBindings.GetBindingCount("unknown"));
        }

        [Test]
        public void Label_PrimaryAndSecondary_Correct()
        {
            Assert.AreEqual("primary", GameSettingsBindings.Label("move_up", 0));
            Assert.AreEqual("secondary", GameSettingsBindings.Label("move_up", 1));
        }
    }
}
