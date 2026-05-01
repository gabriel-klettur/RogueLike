using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;
using Valkur.Core.Input;

namespace Valkur.Tests.EditMode.Game.Input
{
    /// <summary>
    /// Regression suite for the recurring "Mouse + F-keys silently die in Play
    /// Mode" bug. Locks down the invariants <see cref="InputSystemConfigurator"/>
    /// guarantees:
    ///
    ///   I1. There is at most one Mouse and one Keyboard registered (no
    ///       duplicate-device accumulation under Domain Reload OFF).
    ///   I2. Every <see cref="InputDevice"/> has the CanRunInBackground +
    ///       CanRunInBackgroundHasBeenQueried bits set.
    ///   I3. The runtime settings (<see cref="InputSettings.backgroundBehavior"/>
    ///       and <see cref="InputSettings.editorInputBehaviorInPlayMode"/>) are
    ///       only mutated when <c>Application.isPlaying</c> — EditMode tests
    ///       always see Unity defaults.
    ///   I4. <c>ApplyRuntimeSettings</c> pins the two settings to
    ///       <c>IgnoreFocus</c> + <c>AllDeviceInputAlwaysGoesToGameView</c>
    ///       (these are the only combination that makes OS events reach
    ///       <c>InputAction</c>s in Editor Play Mode when MCP / another
    ///       EditorWindow has keyboard focus at Play-start).
    /// </summary>
    [TestFixture]
    public class InputSystemConfiguratorTests
    {
        private InputSettings.BackgroundBehavior _savedBg;
        private InputSettings.EditorInputBehaviorInPlayMode _savedEditor;

        [SetUp]
        public void SetUp()
        {
            LogAssert.ignoreFailingMessages = true;
            if (Mouse.current == null)    InputSystem.AddDevice<Mouse>();
            if (Keyboard.current == null) InputSystem.AddDevice<Keyboard>();
            _savedBg     = InputSystem.settings.backgroundBehavior;
            _savedEditor = InputSystem.settings.editorInputBehaviorInPlayMode;
        }

        [TearDown]
        public void TearDown()
        {
            LogAssert.ignoreFailingMessages = false;
            InputSystem.settings.backgroundBehavior = _savedBg;
            InputSystem.settings.editorInputBehaviorInPlayMode = _savedEditor;
        }

        [Test]
        public void RemoveDuplicateMouseAndKeyboard_DropsExtraMouseDevices()
        {
            InputSystem.AddDevice<Mouse>();
            int mouseCount = 0;
            foreach (var d in InputSystem.devices) if (d is Mouse) mouseCount++;
            Assert.GreaterOrEqual(mouseCount, 2);

            int removed = InputSystemConfigurator.RemoveDuplicateMouseAndKeyboard();

            mouseCount = 0;
            foreach (var d in InputSystem.devices) if (d is Mouse) mouseCount++;
            Assert.AreEqual(1, mouseCount, "I1: dedup must collapse Mouse duplicates.");
            Assert.GreaterOrEqual(removed, 1);
        }

        [Test]
        public void RemoveDuplicateMouseAndKeyboard_DropsExtraKeyboardDevices()
        {
            InputSystem.AddDevice<Keyboard>();
            int kbCount = 0;
            foreach (var d in InputSystem.devices) if (d is Keyboard) kbCount++;
            Assert.GreaterOrEqual(kbCount, 2);

            InputSystemConfigurator.RemoveDuplicateMouseAndKeyboard();

            kbCount = 0;
            foreach (var d in InputSystem.devices) if (d is Keyboard) kbCount++;
            Assert.AreEqual(1, kbCount, "I1: dedup must collapse Keyboard duplicates.");
        }

        [Test]
        public void RemoveDuplicateMouseAndKeyboard_KeepsCurrentMouseAndKeyboard()
        {
            InputSystem.AddDevice<Mouse>();
            InputSystem.AddDevice<Keyboard>();
            var keepMouse = Mouse.current;
            var keepKb = Keyboard.current;

            InputSystemConfigurator.RemoveDuplicateMouseAndKeyboard();

            Assert.AreSame(keepMouse, Mouse.current);
            Assert.AreSame(keepKb, Keyboard.current);
        }

        [Test]
        public void EnableCanRunInBackgroundOnAllDevices_FlipsBothBits()
        {
            var fld = typeof(InputDevice).GetField("m_DeviceFlags",
                BindingFlags.NonPublic | BindingFlags.Instance);
            var flagType = fld.FieldType;
            int canRunFlag = (int)System.Enum.Parse(flagType, "CanRunInBackground");
            int queriedFlag = (int)System.Enum.Parse(flagType, "CanRunInBackgroundHasBeenQueried");
            int wantBits = canRunFlag | queriedFlag;
            foreach (var d in InputSystem.devices)
            {
                int cur = (int)fld.GetValue(d);
                fld.SetValue(d, System.Enum.ToObject(flagType, cur & ~wantBits));
            }

            InputSystemConfigurator.EnableCanRunInBackgroundOnAllDevices();

            foreach (var d in InputSystem.devices)
            {
                int cur = (int)fld.GetValue(d);
                Assert.AreEqual(wantBits, cur & wantBits,
                    $"I2: {d.name} must have both bits set.");
            }
        }

        [Test]
        public void Apply_DoesNotMutateInputSettingsInEditMode()
        {
            // I3: Apply() outside Play Mode must not touch the project-wide
            // InputSettings, which other EditMode fixtures depend on.
            var bgBefore = InputSystem.settings.backgroundBehavior;
            var edBefore = InputSystem.settings.editorInputBehaviorInPlayMode;

            Assert.IsFalse(Application.isPlaying, "Test only meaningful in EditMode.");
            InputSystemConfigurator.Apply();

            Assert.AreEqual(bgBefore, InputSystem.settings.backgroundBehavior);
            Assert.AreEqual(edBefore, InputSystem.settings.editorInputBehaviorInPlayMode);
        }

        [Test]
        public void ApplyRuntimeSettings_PinsBackgroundBehaviorToIgnoreFocus()
        {
            InputSystem.settings.backgroundBehavior =
                InputSettings.BackgroundBehavior.ResetAndDisableNonBackgroundDevices;

            InputSystemConfigurator.ApplyRuntimeSettings();

            Assert.AreEqual(InputSettings.BackgroundBehavior.IgnoreFocus,
                InputSystem.settings.backgroundBehavior,
                "I4: ApplyRuntimeSettings must pin backgroundBehavior to IgnoreFocus.");
        }

        [Test]
        public void ApplyRuntimeSettings_PinsEditorInputBehaviorToAllDeviceInputAlwaysGoesToGameView()
        {
            InputSystem.settings.editorInputBehaviorInPlayMode =
                InputSettings.EditorInputBehaviorInPlayMode.PointersAndKeyboardsRespectGameViewFocus;

            InputSystemConfigurator.ApplyRuntimeSettings();

            Assert.AreEqual(InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView,
                InputSystem.settings.editorInputBehaviorInPlayMode,
                "I4: ApplyRuntimeSettings must pin editor behavior to AllDeviceInputAlwaysGoesToGameView.");
        }

        [Test]
        public void RestoreOriginalSettings_RevertsCapturedValues()
        {
            // Force the captured-state via reflection (Apply() in EditMode skips
            // capture, so we simulate the Play-mode case manually).
            var origBg = InputSystem.settings.backgroundBehavior;
            var origEd = InputSystem.settings.editorInputBehaviorInPlayMode;
            var t = typeof(InputSystemConfigurator);
            t.GetField("_origBackgroundBehavior", BindingFlags.NonPublic | BindingFlags.Static)
                .SetValue(null, origBg);
            t.GetField("_origEditorInputBehavior", BindingFlags.NonPublic | BindingFlags.Static)
                .SetValue(null, origEd);
            t.GetField("_settingsCaptured", BindingFlags.NonPublic | BindingFlags.Static)
                .SetValue(null, true);

            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.editorInputBehaviorInPlayMode =
                InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;

            InputSystemConfigurator.RestoreOriginalSettings();

            Assert.AreEqual(origBg, InputSystem.settings.backgroundBehavior);
            Assert.AreEqual(origEd, InputSystem.settings.editorInputBehaviorInPlayMode);
        }

        [Test]
        public void ForceFocusFlagTrue_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => InputSystemConfigurator.ForceFocusFlagTrue(),
                "ForceFocusFlagTrue must swallow reflection failures gracefully.");
        }

        [Test]
        public void Apply_IsIdempotent()
        {
            InputSystemConfigurator.Apply();
            int devicesAfter = InputSystem.devices.Count;
            InputSystemConfigurator.Apply();
            Assert.AreEqual(devicesAfter, InputSystem.devices.Count);
        }

        [Test]
        public void Apply_SetsHasAppliedFlag()
        {
            var f = typeof(InputSystemConfigurator).GetField("_applied",
                BindingFlags.NonPublic | BindingFlags.Static);
            f.SetValue(null, false);
            InputSystemConfigurator.Apply();
            Assert.IsTrue(InputSystemConfigurator.HasApplied);
        }
    }
}
