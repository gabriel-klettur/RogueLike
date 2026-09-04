using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using Valkur.Core.Input;

namespace Valkur.Tests.EditMode.Game.Input
{
    /// <summary>
    /// Coverage for the legacy input backend obeying the modal block.
    ///
    /// <para>Every gameplay key in this project is read twice: once through its bound
    /// <c>InputAction</c>, and once through <c>UnityEngine.Input</c> as an OR-gate against
    /// the recurring Unity 2022.3 Editor bug where the new InputSystem drops events. Opening
    /// the chat disables the Gameplay action map, which silences the first half — and three
    /// callsites read the second half RAW, so it kept answering.</para>
    ///
    /// <para>The result was that typing a message cast spells. "hola gatita que tal estas
    /// hoy?" contains o, l, t, q and u, which are bound to meteor shower, healing totem,
    /// sphere shield, teleport and summon barbol; i opened the inventory mid-sentence and q
    /// threw an item on the ground. Nothing failed, nothing logged, and the block was
    /// working perfectly on the half of the pipeline anyone would have thought to check.</para>
    /// </summary>
    [TestFixture]
    public class ModalInputBlockingTests
    {
        [SetUp]
        public void SetUp() => InputBlocker.SetBlocked(false);

        [TearDown]
        public void TearDown() => InputBlocker.SetBlocked(false);

        [Test]
        public void WasKeyCodePressedThisFrame_WhileBlocked_RefusesAGameplayKey()
        {
            InputBlocker.SetBlocked(true);

            // No key is physically down in a test, so a false here proves only that the
            // guard returns early. That IS the contract: the guard must run before the
            // backend is consulted at all.
            Assert.IsFalse(KeyboardInputManager.WasKeyCodePressedThisFrame(KeyCode.Q),
                "Q is bound to a spell and to dropping an item. While a panel has focus it " +
                "must reach neither.");
        }

        [TestCase(KeyCode.Escape)]
        [TestCase(KeyCode.Return)]
        [TestCase(KeyCode.KeypadEnter)]
        [TestCase(KeyCode.BackQuote)]
        public void AlwaysAllowedKeys_SurviveTheBlock(KeyCode key)
        {
            Assert.IsTrue(InputBlocker.IsAlwaysAllowedKey(key),
                $"{key} is how a player gets OUT of the panel that raised the block. " +
                "Blocking it would trap them in the chat with no keyboard way to close it.");
        }

        [TestCase(KeyCode.Q)]
        [TestCase(KeyCode.I)]
        [TestCase(KeyCode.Tab)]
        [TestCase(KeyCode.Z)]
        [TestCase(KeyCode.O)]
        [TestCase(KeyCode.B)]
        [TestCase(KeyCode.Alpha1)]
        public void GameplayKeys_AreNotOnTheAlwaysAllowedList(KeyCode key)
        {
            Assert.IsFalse(InputBlocker.IsAlwaysAllowedKey(key),
                $"{key} does something in the world. Nothing that acts on the world belongs " +
                "on the list of keys a modal panel cannot suppress.");
        }

        /// <summary>
        /// The shipped input asset, read the same way the game reads it at runtime.
        /// <see cref="InputService.Instance"/> is null in Edit Mode, and skipping the test
        /// on that basis was worse than useless: a skipped test reports green while covering
        /// nothing, which is the exact failure mode this whole area keeps producing.
        /// </summary>
        private static InputActionAsset LoadShippedActions() =>
            Resources.Load<InputActionAsset>("Input/ValkurInputActions");

        [Test]
        public void EverySpellBinding_UsesAKeyAModalPanelCanSuppress()
        {
            var asset = LoadShippedActions();
            Assert.IsNotNull(asset, "Resources/Input/ValkurInputActions is what InputService loads.");

            var gameplay = asset.FindActionMap("Gameplay");
            Assert.IsNotNull(gameplay);

            int checkedBindings = 0;
            foreach (var action in gameplay.actions)
            {
                if (!action.name.StartsWith("Spell", System.StringComparison.Ordinal)) continue;

                foreach (var binding in action.bindings)
                {
                    if (string.IsNullOrEmpty(binding.path)) continue;
                    checkedBindings++;

                    // Escape, Enter and ~ are the keys a modal panel may NOT suppress,
                    // because they are how the player closes it. A spell bound to one of
                    // them could never be blocked while typing, whatever the readers do.
                    foreach (string reserved in new[] { "/escape", "/enter", "/numpadEnter", "/backquote" })
                    {
                        Assert.IsFalse(binding.path.EndsWith(reserved, System.StringComparison.OrdinalIgnoreCase),
                            $"'{action.name}' binds {binding.path}, which is on the " +
                            "always-allowed list and therefore uncancellable while the chat " +
                            "has focus.");
                    }
                }
            }

            Assert.Greater(checkedBindings, 20,
                "The project ships 24 spell bindings; finding almost none means the map or " +
                "the naming changed and this test stopped looking at anything.");
        }

        [Test]
        public void NoTwoGameplayActions_ShareAKeyboardBinding()
        {
            var gameplay = LoadShippedActions()?.FindActionMap("Gameplay");
            Assert.IsNotNull(gameplay);

            var owners = new System.Collections.Generic.Dictionary<string, string>();
            foreach (var action in gameplay.actions)
            {
                foreach (var binding in action.bindings)
                {
                    if (string.IsNullOrEmpty(binding.path) || !binding.path.StartsWith("<Keyboard>/")) continue;

                    if (owners.TryGetValue(binding.path, out string other))
                    {
                        Assert.Fail(
                            $"{binding.path} is bound to BOTH '{other}' and '{action.name}'. " +
                            "Two actions on one key fire together and neither can be reasoned " +
                            "about: <Keyboard>/e was Interact AND SpellSlash, so talking to an " +
                            "NPC swung a blade, and <Keyboard>/p was Pause AND SpellMeteorShower, " +
                            "so pausing threw meteors.");
                    }
                    owners[binding.path] = action.name;
                }
            }
        }

        [Test]
        public void Unblocked_TheHelperConsultsTheBackendAgain()
        {
            InputBlocker.SetBlocked(false);

            // Nothing is pressed, so this is false either way — what it pins is that the
            // helper does not latch the blocked state and refuse forever afterwards.
            Assert.DoesNotThrow(() => KeyboardInputManager.WasKeyCodePressedThisFrame(KeyCode.Q));
            Assert.IsFalse(InputBlocker.IsGameplayBlocked);
        }
    }
}
