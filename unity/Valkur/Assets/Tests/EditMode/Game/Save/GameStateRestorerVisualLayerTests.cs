using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay.Save;
using Valkur.Gameplay.World.Layering;

namespace Valkur.Tests.EditMode.Game.Save
{
    /// <summary>
    /// End-to-end coverage for the M1.7 save → restore path of the player's visual
    /// layer. <see cref="GameStateRestorer.Restore(GameSaveData)"/> hydrates many
    /// player fields (position, HP, mana, XP, inventory) and must additionally
    /// route <see cref="PlayerSaveData.visualLayer"/> through
    /// <see cref="VisualLayerOccupant.SetVisualLayer(int)"/> so listeners receive
    /// the same <c>OnLayerChanged</c> event a gameplay-driven flip would fire.
    ///
    /// Test isolation: each case spawns a minimal player GameObject with ONLY a
    /// <c>VisualLayerOccupant</c> attached. The other Restore* helpers no-op on a
    /// missing component, so the chain runs cleanly without dragging in Health /
    /// Mana / Experience / Inventory scaffolding.
    /// </summary>
    [TestFixture]
    public class GameStateRestorerVisualLayerTests
    {
        private GameObject _player;
        private VisualLayerOccupant _occupant;

        [SetUp]
        public void SetUp()
        {
            // Inventory.Restore logs a "missing ItemCatalog" warning when psd.inventory
            // is non-null but no catalog is registered — we don't supply an inventory
            // here, so that path is skipped. Still, sister components warn-log on
            // missing optional dependencies; ignore failing messages keeps the run
            // clean if the chain emits any.
            LogAssert.ignoreFailingMessages = true;

            _player = new GameObject("TestPlayer");
            _occupant = _player.AddComponent<VisualLayerOccupant>();
            EntityRegistry.RegisterPlayer(_player);
        }

        [TearDown]
        public void TearDown()
        {
            EntityRegistry.UnregisterPlayer(_player);
            Object.DestroyImmediate(_player);
            LogAssert.ignoreFailingMessages = false;
        }

        private static GameSaveData BuildSave(int visualLayer)
        {
            return new GameSaveData
            {
                player = new PlayerSaveData
                {
                    playerClass = "", // empty → skip PlayerSelectionState path
                    position = Vector2.zero,
                    currentZone = "",
                    visualLayer = visualLayer,
                }
            };
        }

        [Test]
        public void Restore_NonZeroLayer_AppliesToOccupant()
        {
            // Pre-condition: fresh occupant defaults to 0.
            Assert.AreEqual(0, _occupant.CurrentVisualLayer);

            GameStateRestorer.Restore(BuildSave(visualLayer: 7));

            Assert.AreEqual(7, _occupant.CurrentVisualLayer,
                "GameStateRestorer must route PlayerSaveData.visualLayer through " +
                "SetVisualLayer so the occupant matches the saved value.");
        }

        [Test]
        public void Restore_LayerZero_OnFreshOccupant_NoOp()
        {
            // Subscribe to OnLayerChanged: a 0→0 restore must NOT fire the event
            // (the setter no-ops when the value didn't change). This pins the
            // contract that listeners only react to real transitions, which M2
            // will lean on heavily for collider mask re-computes.
            int fireCount = 0;
            _occupant.OnLayerChanged += (_, _) => fireCount++;

            GameStateRestorer.Restore(BuildSave(visualLayer: 0));

            Assert.AreEqual(0, _occupant.CurrentVisualLayer);
            Assert.AreEqual(0, fireCount,
                "Restoring a value equal to the occupant's current layer must not " +
                "fire OnLayerChanged — the setter treats it as a no-op.");
        }

        [Test]
        public void Restore_OutOfRangeLayer_ClampsToValidRange()
        {
            // 99 is out of range. The occupant clamps to [0, 8]; restoring this
            // unusual value must NOT crash and must end at the high boundary.
            GameStateRestorer.Restore(BuildSave(visualLayer: 99));
            Assert.AreEqual(VisualLayerOccupant.MaxLayer, _occupant.CurrentVisualLayer);
        }

        [Test]
        public void Restore_PlayerWithoutOccupant_DoesNotThrow()
        {
            // Strip the component to simulate an entity that opted out of the
            // layer system. The restore helper must early-return safely.
            Object.DestroyImmediate(_occupant);

            Assert.DoesNotThrow(() => GameStateRestorer.Restore(BuildSave(visualLayer: 4)),
                "Restore must tolerate a player GameObject that has no " +
                "VisualLayerOccupant — the helper checks GetComponent and returns.");
        }

        [Test]
        public void Restore_NullPlayer_DoesNotThrow()
        {
            // EntityRegistry.Player == null path: GameStateRestorer.Restore logs a
            // warning and returns. No mutation, no exception.
            EntityRegistry.UnregisterPlayer(_player);
            Object.DestroyImmediate(_player);
            _player = null;

            Assert.DoesNotThrow(() => GameStateRestorer.Restore(BuildSave(visualLayer: 4)));
        }

        [Test]
        public void Restore_FiresOnLayerChangedExactlyOnce()
        {
            int fireCount = 0;
            int oldSeen = -1, newSeen = -1;
            _occupant.OnLayerChanged += (o, n) => { fireCount++; oldSeen = o; newSeen = n; };

            GameStateRestorer.Restore(BuildSave(visualLayer: 5));

            Assert.AreEqual(1, fireCount, "Single layer transition must fire OnLayerChanged once.");
            Assert.AreEqual(0, oldSeen);
            Assert.AreEqual(5, newSeen);
        }
    }
}
