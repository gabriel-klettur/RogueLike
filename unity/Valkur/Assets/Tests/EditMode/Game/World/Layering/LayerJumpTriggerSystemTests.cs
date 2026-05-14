using NUnit.Framework;
using UnityEngine;
using Valkur.Gameplay.World.Layering;

namespace Valkur.Tests.EditMode.Game.World.Layering
{
    /// <summary>
    /// State-machine coverage for <see cref="LayerJumpTriggerSystem"/>'s cell-enter
    /// detector. We drive the system through the <c>TestBind</c> +
    /// <c>TestStepToCell</c> hooks rather than the real Update() loop because the
    /// production Update() resolves both the player and the LayerJumpMap via global
    /// singletons (FindObjectOfType + TileEditorManager.Instance) — none of which
    /// are reasonable to scaffold in an EditMode test. The test-only hooks compose
    /// the exact same private <c>EvaluateCellTransition</c> path, so coverage is
    /// faithful.
    /// </summary>
    [TestFixture]
    public class LayerJumpTriggerSystemTests
    {
        private GameObject _systemGo;
        private LayerJumpTriggerSystem _system;
        private GameObject _playerGo;
        private VisualLayerOccupant _player;
        private LayerJumpMap _jumps;

        [SetUp]
        public void SetUp()
        {
            _systemGo = new GameObject(nameof(LayerJumpTriggerSystem));
            _system = _systemGo.AddComponent<LayerJumpTriggerSystem>();

            _playerGo = new GameObject("TestPlayer");
            _player = _playerGo.AddComponent<VisualLayerOccupant>();

            _jumps = new LayerJumpMap();
            _system.TestBind(_player, _jumps);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_playerGo);
            Object.DestroyImmediate(_systemGo);
        }

        [Test]
        public void EmptyMap_NoFire()
        {
            int events = 0;
            _player.OnLayerChanged += (_, _) => events++;

            Assert.IsFalse(_system.TestStepToCell(new Vector2Int(5, 5)));
            Assert.AreEqual(0, events);
            Assert.AreEqual(0, _player.CurrentVisualLayer);
        }

        [Test]
        public void StepOntoJumpCell_FiresSetVisualLayer()
        {
            _jumps.Set(new Vector2Int(5, 5), "4");

            // First frame somewhere else — primes _lastCell.
            _system.TestStepToCell(new Vector2Int(3, 3));
            Assert.AreEqual(0, _player.CurrentVisualLayer);

            // Cross into the jump cell.
            Assert.IsTrue(_system.TestStepToCell(new Vector2Int(5, 5)));
            Assert.AreEqual(4, _player.CurrentVisualLayer);
        }

        [Test]
        public void StayOnJumpCell_DoesNotReFire()
        {
            _jumps.Set(new Vector2Int(5, 5), "4");

            _system.TestStepToCell(new Vector2Int(3, 3)); // prime
            _system.TestStepToCell(new Vector2Int(5, 5)); // first entry → fires
            Assert.AreEqual(4, _player.CurrentVisualLayer);

            int events = 0;
            _player.OnLayerChanged += (_, _) => events++;

            // Repeated ticks on the SAME cell must NOT re-fire — pinned because
            // it is the entire reason for the cell-enter design.
            Assert.IsFalse(_system.TestStepToCell(new Vector2Int(5, 5)));
            Assert.IsFalse(_system.TestStepToCell(new Vector2Int(5, 5)));
            Assert.IsFalse(_system.TestStepToCell(new Vector2Int(5, 5)));
            Assert.AreEqual(0, events,
                "Staying on a jump cell across ticks must not produce extra OnLayerChanged events.");
        }

        [Test]
        public void LeaveAndReturn_FiresAgain()
        {
            _jumps.Set(new Vector2Int(5, 5), "4");

            _system.TestStepToCell(new Vector2Int(3, 3));
            _system.TestStepToCell(new Vector2Int(5, 5)); // fires → 4

            // Reset by walking the player back to a different cell explicitly.
            _player.SetVisualLayer(0); // pretend something else moved them back
            _system.TestStepToCell(new Vector2Int(3, 3)); // off the jump cell

            Assert.IsTrue(_system.TestStepToCell(new Vector2Int(5, 5)),
                "Returning to the jump cell after leaving must fire again — there " +
                "is no persistent 'consumed' state.");
            Assert.AreEqual(4, _player.CurrentVisualLayer);
        }

        [Test]
        public void TargetEqualsCurrent_NoFireNoEvent()
        {
            _jumps.Set(new Vector2Int(5, 5), "0");
            _player.SetVisualLayer(0);

            int events = 0;
            _player.OnLayerChanged += (_, _) => events++;

            _system.TestStepToCell(new Vector2Int(3, 3));
            Assert.IsFalse(_system.TestStepToCell(new Vector2Int(5, 5)),
                "Jump target equal to the player's current layer must short-circuit.");
            Assert.AreEqual(0, events);
        }

        [Test]
        public void InvalidTargetString_NoFire()
        {
            // Bypass LayerJumpMap.Set's validation by writing directly via the
            // public Set with an invalid value — Set clears the entry silently.
            // To simulate a corrupt loader injecting garbage, stuff it through a
            // round trip via reflection-less LoadMatrix with a forged matrix.
            string[,] forged = new string[1, 1] { { "garbage" } };
            _jumps.LoadMatrix(5, 5, forged);

            // LoadMatrix routes through Set() which rejects invalid → map stays empty.
            Assert.AreEqual(0, _jumps.Count);

            _system.TestStepToCell(new Vector2Int(3, 3));
            Assert.IsFalse(_system.TestStepToCell(new Vector2Int(5, 5)));
            Assert.AreEqual(0, _player.CurrentVisualLayer);
        }

        [Test]
        public void ResetTracker_TreatsNextStepAsFreshEntry()
        {
            _jumps.Set(new Vector2Int(5, 5), "4");

            _system.TestStepToCell(new Vector2Int(5, 5)); // first entry; fires
            Assert.AreEqual(4, _player.CurrentVisualLayer);

            // Reset → next step is treated as fresh.
            _system.ResetTracker();
            _player.SetVisualLayer(0);

            Assert.IsTrue(_system.TestStepToCell(new Vector2Int(5, 5)),
                "After ResetTracker, the very next step into a jump cell fires again.");
            Assert.AreEqual(4, _player.CurrentVisualLayer);
        }
    }
}
