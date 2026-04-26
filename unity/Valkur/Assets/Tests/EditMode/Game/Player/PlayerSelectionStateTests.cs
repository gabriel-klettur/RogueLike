using NUnit.Framework;
using UnityEngine;
using Valkur.Data;

namespace Valkur.Tests.EditMode.Game.Player
{
    public class PlayerSelectionStateTests
    {
        [SetUp]
        public void SetUp()
        {
            PlayerSelectionState.ResetToDefault();
        }

        [TearDown]
        public void TearDown()
        {
            PlayerSelectionState.ResetToDefault();
        }

        [Test]
        public void SetSelectedPlayer_UpdatesKeyAndMarker()
        {
            PlayerSelectionState.SetSelectedPlayer("dwarf");

            Assert.IsTrue(PlayerSelectionState.HasExplicitSelection);
            Assert.AreEqual("dwarf", PlayerSelectionState.SelectedPlayerKey);
            Assert.AreEqual('D', PlayerSelectionState.SelectedMarker);
        }

        [Test]
        public void ResetToDefault_ClearsExplicitSelection()
        {
            PlayerSelectionState.SetSelectedPlayer("mague");
            PlayerSelectionState.ResetToDefault();

            Assert.IsFalse(PlayerSelectionState.HasExplicitSelection);
            Assert.AreEqual("barbarian", PlayerSelectionState.SelectedPlayerKey);
            Assert.AreEqual('B', PlayerSelectionState.SelectedMarker);
        }

        [Test]
        public void PlayerClassCatalog_CreateRuntimeDefinition_ReturnsExpectedPresetStats()
        {
            var def = PlayerClassCatalog.CreateRuntimeDefinition("valkyrie");

            Assert.IsNotNull(def);
            Assert.AreEqual("valkyrie", def.playerKey);
            Assert.AreEqual(7f, def.basicSpeed);
            Assert.AreEqual(90, def.maxStrength);
            Assert.AreEqual(35, def.initialIntelligence);

            Object.DestroyImmediate(def);
        }

        [Test]
        public void PlayerClassCatalog_CreateRuntimeDefinition_UnknownKey_ReturnsNull()
        {
            var def = PlayerClassCatalog.CreateRuntimeDefinition("unknown_class");
            Assert.IsNull(def);
        }
    }
}
