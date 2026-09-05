using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using Valkur.Core;
using Valkur.Core.Input;
using Valkur.UIKit;

namespace Valkur.Tests.EditMode.Game.Input
{
    /// <summary>
    /// The binding layer that makes the Controls editor honest.
    ///
    /// <para>Every one of these pins a defect that was live in the shipped project, not a
    /// hypothetical. The headline is <see cref="Rebinding_MovesTheLegacyHalfToo"/>: every
    /// gameplay read here ORs the new InputSystem with the legacy backend to survive the
    /// 2022.3 event-drop bug, and the legacy half used to be a hardcoded
    /// <see cref="KeyCode"/> literal beside the action. So an override moved half of a
    /// rebind, in silence, and the old key went on working — which made every rebinding UI a
    /// lie about its own effect.</para>
    /// </summary>
    [TestFixture]
    public class ControlsBindingLayerTests
    {
        private InputService _svc;

        [SetUp]
        public void SetUp()
        {
            InputContextPolicy.ResetForTests();
            PlayerStance.ResetForTests();
            InputBindingResolver.ResetForTests();
            _svc = InputService.Initialize();
            Assert.IsNotNull(_svc, "InputService must bootstrap from the canonical asset.");
        }

        [TearDown]
        public void TearDown()
        {
            // Binding overrides live on the CANONICAL asset, which survives Domain Reload off
            // and every fixture in the session. A test that rebinds and does not clean up
            // leaves the next fixture — and the next Play session — reading a moved key, and
            // the failure surfaces somewhere with no connection to this file.
            _svc?.Asset?.RemoveAllBindingOverrides();
            InputBindingResolver.ResetForTests();
            InputContextPolicy.ResetForTests();
            PlayerStance.ResetForTests();
        }

        // ── The control table ────────────────────────────────────────────────

        [Test]
        public void EveryControlEntry_RoundTripsThroughItsPath()
        {
            var broken = new List<string>();
            foreach (var e in InputControlPaths.Entries)
            {
                if (!InputControlPaths.TryResolvePath(e.Path, out var back))
                { broken.Add($"{e.ControlName}: path did not resolve"); continue; }
                if (back.Key != e.Key)
                    broken.Add($"{e.ControlName}: Key {e.Key} came back as {back.Key}");
                if (back.Legacy != e.Legacy)
                    broken.Add($"{e.ControlName}: KeyCode {e.Legacy} came back as {back.Legacy}");
            }
            Assert.IsEmpty(broken, string.Join("\n", broken));
        }

        [Test]
        public void EveryLegacyKeyCode_MapsBackToItsPath()
        {
            var broken = new List<string>();
            foreach (var e in InputControlPaths.Entries)
            {
                if (e.Legacy == KeyCode.None) continue;   // the OEM keys, legitimately
                var path = InputControlPaths.PathForKeyCode(e.Legacy);
                if (path != e.Path) broken.Add($"{e.Legacy} → '{path}', expected '{e.Path}'");
            }
            Assert.IsEmpty(broken, string.Join("\n", broken));
        }

        [Test]
        public void MousePaths_ResolveInBothDirections()
        {
            foreach (MouseControl c in System.Enum.GetValues(typeof(MouseControl)))
            {
                if (c == MouseControl.None) continue;
                var path = InputControlPaths.PathForMouse(c);
                Assert.IsNotNull(path, $"{c} has no path.");
                Assert.AreEqual(c, InputControlPaths.ResolveMouse(path), $"{path} did not resolve back.");
            }
        }

        // ── The headline ─────────────────────────────────────────────────────

        [Test]
        public void Rebinding_MovesTheLegacyHalfToo()
        {
            var darkball = _svc.Gameplay.Spell("SpellDarkball");
            Assert.IsNotNull(darkball);

            var before = InputBindingResolver.Primary(darkball);
            Assert.AreEqual(KeyCode.Alpha1, before.Legacy,
                "Shipped darkball is on '1'; this test is about what happens when it moves.");

            darkball.ApplyBindingOverride(0, "<Keyboard>/5");
            InputBindingResolver.Invalidate();

            var after = InputBindingResolver.Primary(darkball);
            Assert.AreEqual("<Keyboard>/5", after.Path);
            Assert.AreEqual(Key.Digit5, after.Key);
            Assert.AreEqual(KeyCode.Alpha5, after.Legacy,
                "The legacy half must follow the rebind. When it did not, '1' went on casting " +
                "darkball through the OR-gate after the player had moved it to '5' — silently, " +
                "and only under the 2022.3 event-drop bug the OR-gate exists for.");
        }

        [Test]
        public void MoveComposite_ResolvesEveryPartWithItsDirection()
        {
            var bindings = InputBindingResolver.Resolve(_svc.Gameplay.Move);

            var parts = bindings.Where(b => b.IsCompositePart)
                                .Select(b => b.Part)
                                .Distinct()
                                .OrderBy(p => p)
                                .ToArray();

            CollectionAssert.AreEqual(new[] { "down", "left", "right", "up" }, parts,
                "A 2DVector's part names are what tell the legacy fallback which way each key " +
                "points. Without them ReadInput has to list W/A/S/D as literals, which is why " +
                "movement could not be rebound.");

            Assert.GreaterOrEqual(bindings.Length, 8,
                "Move carries WASD and the arrow keys; the arrows were only in the legacy " +
                "literals before, so the asset did not describe the controls the game had.");
        }

        [Test]
        public void Dash_CarriesEveryTriggerAsABinding()
        {
            var paths = InputBindingResolver.Resolve(_svc.Gameplay.Dash)
                                            .Select(b => b.Path)
                                            .OrderBy(p => p)
                                            .ToArray();

            CollectionAssert.AreEquivalent(
                new[] { "<Keyboard>/space", "<Keyboard>/rightShift",
                        "<Keyboard>/leftCtrl", "<Keyboard>/rightCtrl" },
                paths,
                "Three of the dash's four triggers were Key/KeyCode literals inside " +
                "PollTraversal, so rebinding the action moved space and left the other three " +
                "exactly where they were.");
        }

        // ── The whitelist ────────────────────────────────────────────────────

        [Test]
        public void Peace_RefusesEveryActionThatReachesDamage()
        {
            var accepted = new List<string>();
            foreach (var d in InputActionCatalog.All)
            {
                if (!d.ReachesDamage) continue;
                if (InputContextPolicy.Evaluate(d, InputContextMask.Peace) == InputAssignmentVerdict.Allowed)
                    accepted.Add(d.Id);
                if (InputContextPolicy.Evaluate(d, InputContextMask.Gameplay) == InputAssignmentVerdict.Allowed)
                    accepted.Add(d.Id + " (via Both)");
            }

            Assert.IsEmpty(accepted,
                "Peace is a SAFE POSTURE, not a second key layout. Nothing in the damage path " +
                "reads a faction, every NPC carries a Health, and left click both locks a " +
                "target and casts — which is how clicking a vendor to trade with her threw a " +
                "fireball at her. A guarantee the player can configure their way out of is not " +
                "a guarantee:\n" + string.Join("\n", accepted));
        }

        [Test]
        public void IsLive_RefusesDamageInPeace_EvenWhenTheStoredMaskSaysOtherwise()
        {
            var primary = InputActionCatalog.Find("Gameplay/PrimaryAttack");
            Assert.IsNotNull(primary);

            // A profile written by an older build — or by hand — must not be able to re-open
            // the hole. This is why the rule is enforced at READ time as well as at
            // assignment: the two checks are not redundant, they cover different attackers.
            InputContextPolicy.LoadOverrides(new[]
            {
                new KeyValuePair<string, InputContextMask>(primary.Id, InputContextMask.Gameplay),
            });

            Assert.IsFalse(InputContextPolicy.IsLive(primary, Stance.Peace),
                "A stored mask claiming a damage action is live in Peace must be refused at " +
                "the reader too.");
            Assert.IsTrue(InputContextPolicy.IsLive(primary, Stance.War));
        }

        [Test]
        public void EveryGameplayActionLiveInPeace_IsHarmless()
        {
            var dangerous = InputActionCatalog.All
                .Where(d => d.Map == InputActionCatalog.MapGameplay)
                .Where(d => InputContextPolicy.IsLive(d, Stance.Peace))
                .Where(d => d.ReachesDamage)
                .Select(d => d.Id)
                .ToList();

            Assert.IsEmpty(dangerous,
                "The shipped stance masks put a damage action in Peace:\n" +
                string.Join("\n", dangerous));
        }

        [Test]
        public void StanceOverrides_RoundTripThroughSnapshotAndLoad()
        {
            var interact = InputActionCatalog.Find("Gameplay/Interact");
            Assert.AreEqual(InputAssignmentVerdict.Allowed,
                InputContextPolicy.SetContexts(interact, InputContextMask.Peace));

            var snapshot = InputContextPolicy.SnapshotOverrides();
            Assert.AreEqual(1, snapshot.Count);

            InputContextPolicy.ResetForTests();
            Assert.AreEqual(interact.DefaultContexts, InputContextPolicy.ContextsOf(interact));

            InputContextPolicy.LoadOverrides(snapshot);
            Assert.AreEqual(InputContextMask.Peace, InputContextPolicy.ContextsOf(interact));
        }

        [Test]
        public void SettingTheShippedMask_ClearsTheOverrideRatherThanStoringIt()
        {
            var interact = InputActionCatalog.Find("Gameplay/Interact");
            InputContextPolicy.SetContexts(interact, InputContextMask.Peace);
            Assert.IsTrue(InputContextPolicy.HasOverrides);

            InputContextPolicy.SetContexts(interact, interact.DefaultContexts);

            // Not tidiness: ContextsOf short-circuits on an empty table, and that fast path is
            // what makes a per-frame stance check free in the case that is virtually always
            // true. A saved profile should also record only what the player really decided.
            Assert.IsFalse(InputContextPolicy.HasOverrides,
                "Writing the shipped default must REMOVE the override, not store it.");
        }

        [Test]
        public void AnActionMustBeLiveSomewhere()
        {
            var interact = InputActionCatalog.Find("Gameplay/Interact");
            Assert.AreEqual(InputAssignmentVerdict.RefusedEmptyMask,
                InputContextPolicy.SetContexts(interact, InputContextMask.None),
                "An action live in no stance is a control the player cannot find and cannot " +
                "switch back on from anywhere except this same editor.");
        }

        // ── Conflicts ────────────────────────────────────────────────────────

        [Test]
        public void GameplayMap_HasNoSameMapConflicts()
        {
            var offenders = InputConflictScanner.Scan(_svc.Asset)
                .Where(c => c.Severity == InputConflictSeverity.SameMap)
                .Where(c => c.A.Map == InputActionCatalog.MapGameplay)
                .Select(c => c.Describe())
                .ToList();

            Assert.IsEmpty(offenders,
                "Two gameplay actions on one key, both live in the same stance. This project " +
                "has shipped that three times — `e` on Interact and SpellSlash, `p` on Pause " +
                "and SpellMeteorShower (pausing threw meteors), and `tab` on the stance toggle " +
                "and an inventory action built in C# where no audit could see it:\n" +
                string.Join("\n", offenders));
        }

        /// <summary>
        /// The Editors map's known collisions, held as a ratchet rather than fixed.
        ///
        /// <para>All four are old and three survive because one half is reached with a
        /// modifier that lives in C# rather than in the binding — Lighting is Ctrl+F3, and the
        /// asset says plain F3. Which half should move is a design decision about hotkeys, not
        /// something a test should force. What the list DOES buy is that a fifth one fails
        /// here instead of being discovered by a user pressing F6.</para>
        /// </summary>
        [Test]
        public void EditorsMap_HasOnlyTheKnownCollisions()
        {
            var known = new HashSet<string>
            {
                "<Keyboard>/f2", "<Keyboard>/f3", "<Keyboard>/f5", "<Keyboard>/f9",
            };

            var unexpected = InputConflictScanner.Scan(_svc.Asset)
                .Where(c => c.Severity == InputConflictSeverity.SameMap)
                .Where(c => c.A.Map == InputActionCatalog.MapEditors)
                .Where(c => !known.Contains(c.Path))
                .Select(c => c.Describe())
                .ToList();

            Assert.IsEmpty(unexpected,
                "A new editor-hotkey collision:\n" + string.Join("\n", unexpected));
        }

        // ── The drawn board ──────────────────────────────────────────────────

        [Test]
        public void EveryDrawnKey_IsInTheControlTable()
        {
            var unknown = new List<string>();
            foreach (KeyboardLayoutKind kind in System.Enum.GetValues(typeof(KeyboardLayoutKind)))
                foreach (var name in KeyboardLayoutModel.ControlNames(kind))
                    if (!InputControlPaths.TryResolveControlName(name, out _))
                        unknown.Add($"{kind}: {name}");

            Assert.IsEmpty(unknown,
                "The drawn keyboard shows a key the translator cannot name, so clicking it " +
                "would bind a path with no legacy half — which works until the event-drop bug " +
                "fires:\n" + string.Join("\n", unknown));
        }

        [Test]
        public void EveryBoundKeyboardControl_IsDrawnByTheIsoLayout()
        {
            var drawn = new HashSet<string>(KeyboardLayoutModel.ControlNames(KeyboardLayoutKind.Iso),
                                            System.StringComparer.OrdinalIgnoreCase);

            var unreachable = new List<string>();
            foreach (var kv in InputConflictScanner.BindingsByPath(_svc.Asset))
            {
                if (!InputControlPaths.IsKeyboardPath(kv.Key)) continue;
                var control = InputControlPaths.ControlNameOf(kv.Key);
                if (!drawn.Contains(control))
                    unreachable.Add($"{control} ({string.Join(", ", kv.Value.Select(d => d.Id))})");
            }

            Assert.IsEmpty(unreachable,
                "A shipped binding sits on a key the drawn board does not show, so the player " +
                "can see the action but never the key it is on:\n" + string.Join("\n", unreachable));
        }

        [Test]
        public void NoDrawnKeyAppearsTwice()
        {
            foreach (KeyboardLayoutKind kind in System.Enum.GetValues(typeof(KeyboardLayoutKind)))
            {
                var names = KeyboardLayoutModel.ControlNames(kind).ToList();
                var dupes = names.GroupBy(n => n).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
                Assert.IsEmpty(dupes,
                    $"{kind} draws the same key twice, so one of the two caps can never be " +
                    "repainted:\n" + string.Join("\n", dupes));
            }
        }
    }
}
