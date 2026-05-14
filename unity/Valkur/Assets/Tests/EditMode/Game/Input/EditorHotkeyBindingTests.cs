using System.Linq;
using NUnit.Framework;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;
using Valkur.Core.Input;

namespace Valkur.Tests.EditMode.Game.Input
{
    /// <summary>
    /// Structural EditMode counterpart to
    /// <see cref="Valkur.Tests.PlayMode.Input.EditorHotkeyDispatchPlayTests"/>.
    ///
    /// Why a parallel fixture: the PlayMode original exercises the full
    /// keyboard-event → InputAction → callback pipeline. That pipeline depends
    /// on (a) the Unity Editor window having focus AND (b) the InputSystem
    /// action evaluator running on Player updates — neither of which holds
    /// when the suite runs via MCP / batch mode. The PlayMode tests are
    /// therefore <c>[Ignore]</c>'d in CI but kept for local validation.
    ///
    /// This EditMode fixture verifies the structural wiring that, if intact,
    /// guarantees the dispatch would fire when focus is present:
    ///
    ///   1. Each editor hotkey action exists on the canonical asset.
    ///   2. Each action's effective binding path points to the documented key.
    ///   3. The Editors action map is enabled after <c>InputService.Initialize()</c>.
    ///   4. <c>EditorHotkeyBindings.Resolve(hotkey)</c> returns the same canonical
    ///      action reference as <c>InputService.Editors.{Action}</c> — every editor
    ///      shares one source of truth.
    ///   5. <c>EditorHotkeyBindings.Resolve</c> falls back to a usable ad-hoc
    ///      action when <c>InputService</c> is not initialized (the EditMode
    ///      build path other editor fixtures depend on).
    ///
    /// Together (1)–(5) catch every realistic regression a refactor could
    /// introduce — action rename, binding-path typo, map left disabled,
    /// enum disconnected from <c>Resolve</c>. The only class of bug they
    /// cannot detect is a Unity-side break of the action evaluator itself,
    /// which is covered by the (focus-dependent) PlayMode tests.
    /// </summary>
    [TestFixture]
    public class EditorHotkeyBindingTests
    {
        [SetUp]
        public void SetUp()
        {
            LogAssert.ignoreFailingMessages = true;
            InputService.ResetForTests();
            InputService.Initialize();

            // Cross-fixture defence: with Domain Reload OFF, any prior Play Mode
            // session (or test) that ran EditorBindingsApplier.ReapplyAll() leaves
            // ApplyBindingOverride calls living on the canonical InputActionAsset
            // — those overrides survive ResetForTests because that only nulls the
            // service instance, not the shared asset's mutable state. Strip them
            // here so this fixture verifies the asset's authored defaults rather
            // than whatever user-customised key was last applied in PauseMenuUI.
            InputService.Instance.Editors.Map.RemoveAllBindingOverrides();
        }

        [TearDown]
        public void TearDown()
        {
            // Leave the asset in a clean state for downstream fixtures.
            if (InputService.HasInstance)
                InputService.Instance.Editors.Map.RemoveAllBindingOverrides();
            InputService.ResetForTests();
            LogAssert.ignoreFailingMessages = false;
        }

        // Each row: (Hotkey, expected effective binding path).
        // The 13 entries mirror EditorHotkeyDispatchPlayTests 1-to-1 (12 dispatch
        // tests + CtrlModifier hold test). F5 → both ToggleEntities and QuickSave
        // verify the deliberately shared binding path.
        private static readonly object[] HotkeyCases =
        {
            new object[] { EditorHotkeyBindings.Hotkey.ToggleParticles,  "<Keyboard>/f1" },
            new object[] { EditorHotkeyBindings.Hotkey.ToggleSpells,     "<Keyboard>/f4" },
            new object[] { EditorHotkeyBindings.Hotkey.ToggleEntities,   "<Keyboard>/f5" },
            new object[] { EditorHotkeyBindings.Hotkey.QuickSave,        "<Keyboard>/f5" },
            new object[] { EditorHotkeyBindings.Hotkey.ToggleInventory,  "<Keyboard>/f6" },
            new object[] { EditorHotkeyBindings.Hotkey.ToggleItems,      "<Keyboard>/f7" },
            new object[] { EditorHotkeyBindings.Hotkey.ToggleTile,       "<Keyboard>/f8" },
            new object[] { EditorHotkeyBindings.Hotkey.ToggleDebugHUD,   "<Keyboard>/f9" },
            new object[] { EditorHotkeyBindings.Hotkey.ToggleBuildings,  "<Keyboard>/f10" },
            new object[] { EditorHotkeyBindings.Hotkey.ToggleMap,        "<Keyboard>/f11" },
            new object[] { EditorHotkeyBindings.Hotkey.ToggleFSM,        "<Keyboard>/f12" },
            new object[] { EditorHotkeyBindings.Hotkey.ToggleDevConsole, "<Keyboard>/backquote" },
            new object[] { EditorHotkeyBindings.Hotkey.CtrlModifier,     "<Keyboard>/leftCtrl" },
        };

        // ── 1. Existence ────────────────────────────────────────────────────────

        [TestCaseSource(nameof(HotkeyCases))]
        public void Action_ExistsOnCanonicalAsset(
            EditorHotkeyBindings.Hotkey hotkey, string _expectedPath)
        {
            var action = EditorHotkeyBindings.Resolve(hotkey, out _);
            Assert.IsNotNull(action,
                $"Hotkey {hotkey} must resolve to a non-null InputAction. " +
                "Did someone delete it from the canonical Editors action map?");
        }

        // ── 2. Effective binding path ──────────────────────────────────────────

        [TestCaseSource(nameof(HotkeyCases))]
        public void Action_HasExpectedBindingPath(
            EditorHotkeyBindings.Hotkey hotkey, string expectedPath)
        {
            var action = EditorHotkeyBindings.Resolve(hotkey, out _);

            // Check the base path (the authored default on the asset) — the override
            // path is the user's customisation surface and not our concern here.
            // SetUp has stripped overrides so effectivePath should agree with path,
            // but base path is the semantically correct field to assert against.
            bool hasExpected = action.bindings.Any(b =>
                string.Equals(b.path, expectedPath, System.StringComparison.OrdinalIgnoreCase));
            Assert.IsTrue(hasExpected,
                $"Hotkey {hotkey} must have an authored binding with path '{expectedPath}'.\n" +
                $"Actual bindings: [{string.Join(", ", action.bindings.Select(b => $"'{b.path}'"))}]");
        }

        // ── 3. Map enabled after Initialize ─────────────────────────────────────

        [Test]
        public void EditorsMap_IsEnabledAfterInitialize()
        {
            Assert.IsTrue(InputService.Instance.Editors.Map.enabled,
                "Editors action map must be enabled after InputService.Initialize() — " +
                "every editor depends on its action.WasPerformedThisFrame() returning true, " +
                "which silently returns false when the map is disabled.");
        }

        // ── 4. Resolve returns the canonical reference (no ownership transfer) ─

        [TestCaseSource(nameof(HotkeyCases))]
        public void Resolve_ReturnsCanonicalReference_WhenServiceIsUp(
            EditorHotkeyBindings.Hotkey hotkey, string _expectedPath)
        {
            var resolved = EditorHotkeyBindings.Resolve(hotkey, out bool owns);
            Assert.IsFalse(owns,
                $"Resolve({hotkey}) must NOT transfer ownership when InputService is up — " +
                "callers would Dispose the canonical action and break the pipeline.");
            Assert.AreSame(resolved, GetCanonical(hotkey),
                $"Resolve({hotkey}) must return the same InputAction instance as " +
                "InputService.Instance.Editors so editors share one source of truth.");
        }

        // ── 5. Fallback when InputService is missing ────────────────────────────

        [Test]
        public void Resolve_FallbackPath_WhenServiceMissing_ReturnsAdHocActionWithF8Binding()
        {
            // Mirrors Resolve_FallbackPath_FiresOnF8Press from the PlayMode fixture:
            // editor fixtures often build a single editor in isolation without
            // booting the input service, and rely on this fallback. Verifies
            // ownership transfer + binding correctness; the firing assertion
            // belongs in PlayMode where the action evaluator can run.
            InputService.ResetForTests();
            Assert.IsFalse(InputService.HasInstance,
                "Precondition: InputService must be reset so the fallback branch is exercised.");

            var action = EditorHotkeyBindings.Resolve(
                EditorHotkeyBindings.Hotkey.ToggleTile, out bool owns);
            try
            {
                Assert.IsTrue(owns,
                    "Fallback must transfer ownership so the caller is responsible for Dispose.");
                Assert.IsNotNull(action);
                Assert.IsTrue(action.enabled,
                    "Fallback action must be enabled on return — editors poll it directly.");
                bool hasF8 = action.bindings.Any(b =>
                    string.Equals(b.path, "<Keyboard>/f8", System.StringComparison.OrdinalIgnoreCase));
                Assert.IsTrue(hasF8,
                    "Fallback action for ToggleTile must bind <Keyboard>/f8.");
            }
            finally
            {
                action.Dispose();
            }
        }

        // ── Helpers ─────────────────────────────────────────────────────────────

        private static InputAction GetCanonical(EditorHotkeyBindings.Hotkey h)
        {
            var e = InputService.Instance.Editors;
            return h switch
            {
                EditorHotkeyBindings.Hotkey.ToggleParticles  => e.ToggleParticles,
                EditorHotkeyBindings.Hotkey.ToggleSpells     => e.ToggleSpells,
                EditorHotkeyBindings.Hotkey.ToggleEntities   => e.ToggleEntities,
                EditorHotkeyBindings.Hotkey.QuickSave        => e.QuickSave,
                EditorHotkeyBindings.Hotkey.ToggleInventory  => e.ToggleInventory,
                EditorHotkeyBindings.Hotkey.ToggleItems      => e.ToggleItems,
                EditorHotkeyBindings.Hotkey.ToggleTile       => e.ToggleTile,
                EditorHotkeyBindings.Hotkey.ToggleDebugHUD   => e.ToggleDebugHUD,
                EditorHotkeyBindings.Hotkey.ToggleBuildings  => e.ToggleBuildings,
                EditorHotkeyBindings.Hotkey.ToggleMap        => e.ToggleMap,
                EditorHotkeyBindings.Hotkey.ToggleFSM        => e.ToggleFSM,
                EditorHotkeyBindings.Hotkey.ToggleDevConsole => e.ToggleDevConsole,
                EditorHotkeyBindings.Hotkey.CtrlModifier     => e.CtrlModifier,
                _ => null,
            };
        }
    }
}
