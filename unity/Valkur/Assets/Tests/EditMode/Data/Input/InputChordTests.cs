using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using Valkur.Core.Input;
using Valkur.Data;

namespace Valkur.Tests.EditMode.Data.Input
{
    /// <summary>
    /// The Shift layer of the War keyboard, and the promise that the shipped layout keeps.
    ///
    /// <para>A chord is an InputSystem <c>OneModifier</c> composite, and every reader in this
    /// project used to walk composite PARTS as independent controls. That is right for WASD
    /// and wrong for "Shift+1" three ways at once, each silent: the legacy OR-gate fires the
    /// spell on the modifier alone, the conflict scanner sees thirty actions on Shift, and the
    /// Controls editor offers to rebind the Shift of a chord as a slot of its own. These pin the
    /// single walk that fixes all three, and the layout the walk exists for.</para>
    /// </summary>
    [TestFixture]
    public class InputChordTests
    {
        private const string ShiftPath = InputControlPaths.KeyboardPrefix + "leftShift";
        private const string OnePath   = InputControlPaths.KeyboardPrefix + "1";
        private const string QPath     = InputControlPaths.KeyboardPrefix + "q";

        /// <summary>The spells cast by a verb of their own and carrying no key slot: the three
        /// mouse buttons, and the dash (Space, right Shift, both Ctrls).</summary>
        private static readonly string[] MouseSpells = { "fireball", "slash", "laser_beam", "dash" };

        private InputActionMap _map;

        [SetUp]
        public void SetUp() => InputBindingResolver.ResetForTests();

        [TearDown]
        public void TearDown()
        {
            _map?.Dispose();
            _map = null;
            InputBindingResolver.ResetForTests();
        }

        private InputActionMap BuildMap(out InputAction bare, out InputAction chord)
        {
            _map = new InputActionMap("Gameplay");
            bare  = _map.AddAction("Bare",  InputActionType.Button);
            chord = _map.AddAction("Chord", InputActionType.Button);
            bare.AddBinding(OnePath);
            chord.AddCompositeBinding(InputChord.CompositeName)
                 .With(InputChord.ModifierPart, ShiftPath)
                 .With(InputChord.ButtonPart, OnePath);
            return _map;
        }

        // ── The walk ─────────────────────────────────────────────────────────

        [Test]
        public void Slots_FoldAChordIntoOneSlot_OnItsKey()
        {
            BuildMap(out _, out var chord);
            var slots = InputChord.Slots(chord);

            Assert.AreEqual(1, slots.Count, "A chord is ONE bindable control, not a header and two parts.");
            var slot = slots[0];
            Assert.IsTrue(slot.IsChord);
            Assert.AreEqual(OnePath, slot.Path);
            Assert.AreEqual(ShiftPath, slot.ModifierPath);
            Assert.AreEqual(InputChord.ButtonPart, chord.bindings[slot.Index].name,
                "The slot's index must be the KEY, so an override moves the key and keeps the Shift.");
        }

        [Test]
        public void Resolver_ReturnsTheChordAsOneBinding_WithItsModifier()
        {
            BuildMap(out var bare, out var chord);

            var chordBindings = InputBindingResolver.Resolve(chord);
            Assert.AreEqual(1, chordBindings.Length,
                "Resolved as two parts, the legacy half would cast the spell on Shift alone.");
            Assert.IsTrue(chordBindings[0].IsChord);
            Assert.AreEqual(UnityEngine.InputSystem.Key.LeftShift, chordBindings[0].ModifierKey);
            Assert.AreEqual("Shift+1", InputBindingResolver.PrimaryLabel(chord));

            var bareBindings = InputBindingResolver.Resolve(bare);
            Assert.AreEqual(1, bareBindings.Length);
            Assert.IsFalse(bareBindings[0].IsChord);
            Assert.AreEqual("1", InputBindingResolver.PrimaryLabel(bare));
        }

        [Test]
        public void ARebindOfTheChordKey_KeepsTheModifier()
        {
            BuildMap(out _, out var chord);
            int index = InputChord.Slots(chord)[0].Index;

            chord.ApplyBindingOverride(index, QPath);
            InputBindingResolver.Invalidate();

            var b = InputBindingResolver.Primary(chord);
            Assert.AreEqual(QPath, b.Path);
            Assert.AreEqual(ShiftPath, b.ModifierPath);
            Assert.AreEqual("Shift+Q", InputBindingResolver.PrimaryLabel(chord));
        }

        [Test]
        public void NothingHeld_NothingIsShadowed()
        {
            BuildMap(out var bare, out _);
            Assert.IsFalse(InputBindingResolver.IsShadowedByChord(bare, UnityEngine.InputSystem.Key.Digit1),
                "Bare 1 is only taken while its chord's Shift is held.");
        }

        [Test]
        public void ChordPaths_RoundTrip_AndReadAsTheChord()
        {
            string path = InputChord.Compose(ShiftPath, OnePath);
            Assert.IsTrue(InputChord.IsChordPath(path));
            Assert.IsTrue(InputChord.TrySplit(path, out var mod, out var button));
            Assert.AreEqual(ShiftPath, mod);
            Assert.AreEqual(OnePath, button);
            Assert.AreEqual("Shift+1", InputControlPaths.LabelForPath(path));
            Assert.AreEqual("S1", InputChord.CompactLabelFor(ShiftPath, OnePath));
            Assert.IsFalse(InputChord.IsChordPath(OnePath));
        }

        // ── The shipped War keyboard ─────────────────────────────────────────

        private static InputActionAsset Shipped() =>
            Resources.Load<InputActionAsset>("Input/ValkurInputActions");

        [Test]
        public void Scanner_KeysAChordApartFromItsBareKey_AndNeverKeysShift()
        {
            var byPath = InputConflictScanner.BindingsByPath(Shipped());

            Assert.IsFalse(byPath.ContainsKey(ShiftPath),
                "Thirty chords share one Shift; keyed by part, Shift would be the most " +
                "conflicted key on the board.");
            Assert.IsTrue(byPath.ContainsKey(OnePath));
            Assert.IsTrue(byPath.ContainsKey(InputChord.Compose(ShiftPath, OnePath)));
            CollectionAssert.AreNotEquivalent(byPath[OnePath], byPath[InputChord.Compose(ShiftPath, OnePath)]);
        }

        [Test]
        public void EverySpellSlot_HasExactlyOneBoundKey()
        {
            var gameplay = Shipped().FindActionMap("Gameplay");
            var problems = new List<string>();

            foreach (var d in InputActionCatalog.Spells())
            {
                var action = gameplay.FindAction(d.Action, throwIfNotFound: false);
                if (action == null) { problems.Add($"{d.Action}: no action in the asset"); continue; }
                int bound = InputChord.Slots(action).Count(s => s.IsBound);
                if (bound != 1) problems.Add($"{d.Action}: {bound} bound slots");
            }

            Assert.IsEmpty(problems, string.Join("\n", problems));
        }

        [Test]
        public void TheOnlyModifier_IsLeftShift()
        {
            var gameplay = Shipped().FindActionMap("Gameplay");
            var slots = new List<InputChord.Slot>();
            InputChord.Slots(gameplay.bindings, slots);

            var modifiers = slots.Where(s => s.IsChord).Select(s => s.ModifierPath).Distinct().ToList();
            CollectionAssert.AreEqual(new[] { ShiftPath }, modifiers,
                "Right Shift, both Ctrls and Space are the dash: a chord on any of them would " +
                "dash AND cast from one press.");
        }

        [Test]
        public void NoSpellKey_IsAlsoAnotherGameplayVerb()
        {
            // Bare keys only: a chord is its own press. Mouse buttons excluded — the mouse spells
            // are PlayerController's and carry no slot.
            var gameplay = Shipped().FindActionMap("Gameplay");
            var verbKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var spellKeys = new List<(string action, string path)>();

            foreach (var action in gameplay.actions)
            {
                var d = InputActionCatalog.Find(InputActionCatalog.MapGameplay, action.name);
                foreach (var slot in InputChord.Slots(action))
                {
                    if (!slot.IsBound || slot.IsChord) continue;
                    if (d != null && d.IsSpell) spellKeys.Add((action.name, slot.Path));
                    else verbKeys.Add(slot.Path);
                }
            }

            var clashes = spellKeys.Where(s => verbKeys.Contains(s.path))
                                   .Select(s => $"{s.action} on {s.path}").ToList();
            Assert.IsEmpty(clashes, string.Join("\n", clashes));
        }

        [Test]
        public void TheDash_IsKnownFromLevelZero()
        {
            // Space, right Shift and both Ctrls cast `dash` through TryCastByKey, which answers
            // false for a spell outside the book. A fresh character did not know it, so all four
            // keys did nothing — measured live — on the one verb that is context-locked because
            // losing it is a soft lock.
            var catalog = Resources.Load<ProgressionCatalog>("Progression/ProgressionCatalog");
            Assert.IsNotNull(catalog);
            Assert.IsTrue(catalog.IsAlwaysKnown("dash"),
                "`dash` must be in ProgressionCatalog.alwaysKnownSpellKeys, or the dash keys are dead until it is learned.");
        }

        [Test]
        public void EveryGrimoireSpell_IsOnAKey_OrOnTheMouse()
        {
            var catalog = Resources.Load<ProgressionCatalog>("Progression/ProgressionCatalog");
            Assert.IsNotNull(catalog, "Resources/Progression/ProgressionCatalog is what PlayerProgression loads.");

            var onKeys = new HashSet<string>(InputActionCatalog.Spells().Select(d => d.PayloadKey),
                                             StringComparer.Ordinal);
            var missing = new List<string>();
            int inspected = 0;

            foreach (var tree in catalog.spellTrees)
            {
                if (tree == null) continue;
                foreach (var node in tree.Nodes)
                {
                    if (node == null || node.spell == null) continue;
                    inspected++;
                    string key = node.spell.spellKey;
                    if (onKeys.Contains(key) || Array.IndexOf(MouseSpells, key) >= 0) continue;
                    missing.Add($"{tree.name}: {key}");
                }
            }

            Assert.Greater(inspected, 60, "The grimoire resolved to almost nothing, so this checked nothing.");
            Assert.IsEmpty(missing,
                "A spell the player can learn that no key casts. The War keyboard is built by " +
                "tools/input/build_war_keyboard.py — add it to LAYOUT and re-run:\n" +
                string.Join("\n", missing));
        }
    }
}
