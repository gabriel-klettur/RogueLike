using System.Reflection;
using NUnit.Framework;
using Valkur.Core;
using Valkur.Gameplay;

namespace Valkur.Tests.EditMode.Game.Player
{
    /// <summary>
    /// Tests for <c>PlayerController.ShouldSuspendInputFor</c> — the predicate
    /// that decides whether an active runtime editor freezes player movement
    /// and combat. The predicate underpins the "let me walk while the Spawner
    /// Editor (F3) is open" UX, so a regression here silently locks the player
    /// in place again.
    ///
    /// The rule is:
    ///   • No active editor              → don't suspend.
    ///   • Active editor without marker  → suspend (default).
    ///   • Active editor with marker     → don't suspend.
    ///
    /// Marker = <see cref="IAllowsPlayerMovement"/>.
    /// </summary>
    [TestFixture]
    public class PlayerInputSuspensionTests
    {
        // Reflection helper: the predicate is internal in the gameplay assembly
        // (Valkur.Gameplay → InternalsVisibleTo Valkur.Tests.EditMode), so
        // calling it directly works without reflection IF the test runner
        // resolves the symbol. We still use reflection here so the test fixture
        // doesn't break compilation if the predicate is renamed during refactors —
        // it'll surface a clean assertion failure instead.

        private static bool Invoke(GameEditorManager.IGameEditor active)
        {
            var method = typeof(PlayerController)
                .GetMethod("ShouldSuspendInputFor",
                    BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(method,
                "Expected internal static PlayerController.ShouldSuspendInputFor(IGameEditor).");
            return (bool)method.Invoke(null, new object[] { active });
        }

        // ── Stub editors used as predicate inputs ─────────────────────────────

        private sealed class PlainEditor : GameEditorManager.IGameEditor
        {
            public string EditorName => "Plain";
            public bool   IsActive   => true;
            public void Activate()   { }
            public void Deactivate() { }
        }

        private sealed class MovementFriendlyEditor
            : GameEditorManager.IGameEditor, IAllowsPlayerMovement
        {
            public string EditorName => "MovementFriendly";
            public bool   IsActive   => true;
            public void Activate()   { }
            public void Deactivate() { }
        }

        // ── Cases ─────────────────────────────────────────────────────────────

        [Test]
        public void NoActiveEditor_DoesNotSuspend()
        {
            Assert.IsFalse(Invoke(null),
                "When no editor is active the player must move freely — the suspension flag must be false.");
        }

        [Test]
        public void EditorWithoutMarker_SuspendsInput()
        {
            Assert.IsTrue(Invoke(new PlainEditor()),
                "Default behaviour: an active editor that has not opted out via " +
                "IAllowsPlayerMovement must freeze the player.");
        }

        [Test]
        public void EditorWithMarker_DoesNotSuspendInput()
        {
            Assert.IsFalse(Invoke(new MovementFriendlyEditor()),
                "An editor implementing IAllowsPlayerMovement must keep the player able to walk.");
        }

        [Test]
        public void MarkerCheck_IsAReferenceTypeIsCheck_NotName()
        {
            // Sanity: a class whose NAME contains "Movement" but doesn't implement
            // the interface still suspends. Guards against a future refactor that
            // accidentally swaps the `is` check for a string match on EditorName.
            Assert.IsTrue(Invoke(new NamedLookalikeEditor()),
                "Suspension logic must be type-driven, not based on the editor's display name.");
        }

        private sealed class NamedLookalikeEditor : GameEditorManager.IGameEditor
        {
            public string EditorName => "AllowsPlayerMovement";
            public bool   IsActive   => true;
            public void Activate()   { }
            public void Deactivate() { }
        }

        // ── Production editor wiring — type-level assertions ──────────────────
        //
        // These guard the actual movement-friendly editors. Removing the marker
        // from any of them by accident would re-introduce the "I open the editor
        // and the player freezes" regression on real shipping code, not just the
        // stubs above.

        [Test]
        public void SpawnerEditor_ImplementsAllowsPlayerMovement()
        {
            Assert.IsTrue(typeof(IAllowsPlayerMovement).IsAssignableFrom(
                typeof(Valkur.Gameplay.Spawners.SpawnerEditorManager)),
                "SpawnerEditorManager must implement IAllowsPlayerMovement so the player can walk while F3 is open.");
        }

        [Test]
        public void BuildingsEditor_ImplementsAllowsPlayerMovement()
        {
            Assert.IsTrue(typeof(IAllowsPlayerMovement).IsAssignableFrom(
                typeof(Valkur.Gameplay.Buildings.BuildingsRuntimeEditor)),
                "BuildingsRuntimeEditor must implement IAllowsPlayerMovement to preserve its " +
                "collider-testing UX.");
        }

        [Test]
        public void TileEditor_ImplementsAllowsPlayerMovement()
        {
            Assert.IsTrue(typeof(IAllowsPlayerMovement).IsAssignableFrom(
                typeof(Valkur.Gameplay.TileEditor.TileEditorManager)),
                "TileEditorManager must implement IAllowsPlayerMovement to preserve its " +
                "in-game tile-painting UX.");
        }
    }
}
