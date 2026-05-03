using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine.InputSystem;
using Valkur.Core.Input;

namespace Valkur.Tests.EditMode.Game.Input
{
    /// <summary>
    /// Verifies the core contract of <see cref="InputBlocker"/>:
    ///   • The static flag toggles correctly via <see cref="InputBlocker.SetBlocked"/>.
    ///   • <see cref="InputBlocker.OnBlockChanged"/> fires exactly on transitions.
    ///   • <see cref="InputBlocker.IsAlwaysAllowedKey(Key)"/> and the
    ///     <see cref="UnityEngine.KeyCode"/> overload recognise exactly
    ///     Escape, backquote (~), Enter, and NumpadEnter — nothing more.
    /// </summary>
    [TestFixture]
    public class InputBlockerTests
    {
        [SetUp]
        public void SetUp()
        {
            // Guarantee a clean, unblocked state before every test.
            InputBlocker.SetBlocked(false);
            // Drain any leftover subscriber that might leak across tests.
        }

        [TearDown]
        public void TearDown()
        {
            // Always leave the blocker clear so subsequent tests are not poisoned.
            InputBlocker.SetBlocked(false);
        }

        // ── Flag state ──────────────────────────────────────────────────────

        [Test]
        public void Default_IsGameplayBlocked_IsFalse()
        {
            Assert.IsFalse(InputBlocker.IsGameplayBlocked,
                "InputBlocker must start unblocked (SetUp enforces this).");
        }

        [Test]
        public void SetBlocked_True_FlagBecomesTrue()
        {
            InputBlocker.SetBlocked(true);
            Assert.IsTrue(InputBlocker.IsGameplayBlocked);
        }

        [Test]
        public void SetBlocked_False_FlagBecomesFalse()
        {
            InputBlocker.SetBlocked(true);
            InputBlocker.SetBlocked(false);
            Assert.IsFalse(InputBlocker.IsGameplayBlocked);
        }

        // ── Event behaviour ─────────────────────────────────────────────────

        [Test]
        public void SetBlocked_FiresOnBlockChanged_OnTransition()
        {
            var fired = new List<bool>();
            InputBlocker.OnBlockChanged += v => fired.Add(v);

            InputBlocker.SetBlocked(true);   // false → true  → fires
            InputBlocker.SetBlocked(false);  // true  → false → fires

            InputBlocker.OnBlockChanged -= v => fired.Add(v); // best-effort unsub

            Assert.AreEqual(2, fired.Count, "Expected exactly 2 event firings.");
            Assert.IsTrue(fired[0],  "First event must report blocked=true.");
            Assert.IsFalse(fired[1], "Second event must report blocked=false.");
        }

        [Test]
        public void SetBlocked_DoesNotFireEvent_WhenSameValue()
        {
            int fireCount = 0;
            void Handler(bool _) => fireCount++;
            InputBlocker.OnBlockChanged += Handler;

            // Already false; calling false again must be a no-op.
            InputBlocker.SetBlocked(false);
            InputBlocker.SetBlocked(false);

            InputBlocker.OnBlockChanged -= Handler;

            Assert.AreEqual(0, fireCount,
                "No transition → OnBlockChanged must not fire.");
        }

        // ── IsAlwaysAllowedKey — Key overload ───────────────────────────────

        [Test]
        public void IsAlwaysAllowedKey_RecognizesEscEnterTildeOnly_KeyOverload()
        {
            // Allowed keys
            Assert.IsTrue(InputBlocker.IsAlwaysAllowedKey(Key.Escape),      "Escape must be allowed.");
            Assert.IsTrue(InputBlocker.IsAlwaysAllowedKey(Key.Backquote),   "Backquote (~) must be allowed.");
            Assert.IsTrue(InputBlocker.IsAlwaysAllowedKey(Key.Enter),       "Enter must be allowed.");
            Assert.IsTrue(InputBlocker.IsAlwaysAllowedKey(Key.NumpadEnter), "NumpadEnter must be allowed.");

            // Disallowed keys
            Assert.IsFalse(InputBlocker.IsAlwaysAllowedKey(Key.A),        "Key.A must NOT be allowed.");
            Assert.IsFalse(InputBlocker.IsAlwaysAllowedKey(Key.W),        "Key.W must NOT be allowed.");
            Assert.IsFalse(InputBlocker.IsAlwaysAllowedKey(Key.F1),       "Key.F1 must NOT be allowed.");
            Assert.IsFalse(InputBlocker.IsAlwaysAllowedKey(Key.LeftCtrl), "Key.LeftCtrl must NOT be allowed.");
        }

        // ── IsAlwaysAllowedKey — KeyCode overload ───────────────────────────

        [Test]
        public void IsAlwaysAllowedKey_RecognizesEscEnterTildeOnly_KeyCodeOverload()
        {
            // Allowed key codes
            Assert.IsTrue(InputBlocker.IsAlwaysAllowedKey(UnityEngine.KeyCode.Escape),      "KeyCode.Escape must be allowed.");
            Assert.IsTrue(InputBlocker.IsAlwaysAllowedKey(UnityEngine.KeyCode.BackQuote),   "KeyCode.BackQuote must be allowed.");
            Assert.IsTrue(InputBlocker.IsAlwaysAllowedKey(UnityEngine.KeyCode.Return),      "KeyCode.Return must be allowed.");
            Assert.IsTrue(InputBlocker.IsAlwaysAllowedKey(UnityEngine.KeyCode.KeypadEnter), "KeyCode.KeypadEnter must be allowed.");

            // Disallowed key codes
            Assert.IsFalse(InputBlocker.IsAlwaysAllowedKey(UnityEngine.KeyCode.A),           "KeyCode.A must NOT be allowed.");
            Assert.IsFalse(InputBlocker.IsAlwaysAllowedKey(UnityEngine.KeyCode.W),           "KeyCode.W must NOT be allowed.");
            Assert.IsFalse(InputBlocker.IsAlwaysAllowedKey(UnityEngine.KeyCode.F1),          "KeyCode.F1 must NOT be allowed.");
            Assert.IsFalse(InputBlocker.IsAlwaysAllowedKey(UnityEngine.KeyCode.LeftControl), "KeyCode.LeftControl must NOT be allowed.");
        }
    }
}
