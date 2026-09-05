using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using Valkur.Core;
using Valkur.Gameplay;

namespace Valkur.Tests.EditMode.Game.Editors
{
    /// <summary>
    /// Left click casts the Spells Editor's selected spell while F4 is open, and the ordinary
    /// primary attack while it is not.
    ///
    /// The important half of that sentence is the second one. Left click is the player's most
    /// used input; a redirect that leaks past the editor's open state, or that fires when the
    /// pointer is on the editor's own panels, is a far worse bug than the feature is a feature.
    /// So the resolver is a pure static — the same shape as
    /// <c>PlayerController.ShouldSuspendCombatFor</c> — and these tests drive it directly
    /// rather than standing up a scene.
    /// </summary>
    [TestFixture]
    public class SpellsEditorPrimaryCastTests
    {
        private const string DEFAULT_KEY = "fireball";

        /// <summary>An editor that does not opt in — every editor except the Spells one.</summary>
        private sealed class PlainEditor : GameEditorManager.IGameEditor
        {
            public string EditorName => "Plain";
            public bool IsActive => true;
            public void Activate() { }
            public void Deactivate() { }
        }

        /// <summary>An editor that opts in, with a settable selection.</summary>
        private sealed class ChoosingEditor : GameEditorManager.IGameEditor, IChoosesPrimaryCastSpell
        {
            public string Key;
            public bool IgnoreManaCost;
            public string EditorName => "Choosing";
            public bool IsActive => true;
            public void Activate() { }
            public void Deactivate() { }
            public string PrimaryCastSpellKey => Key;
            public bool PrimaryCastIgnoresManaCost => IgnoreManaCost;
        }

        // ── Closed, or an editor that does not opt in ────────────────────────────

        [Test]
        public void NoEditorOpen_KeepsTheOrdinaryPrimaryAttack()
        {
            Assert.AreEqual(DEFAULT_KEY,
                PlayerController.ResolvePrimaryCastKey(null, DEFAULT_KEY),
                "With nothing open, left click must behave exactly as it always has.");
        }

        [Test]
        public void AnEditorThatDoesNotOptIn_ChangesNothing()
        {
            Assert.AreEqual(DEFAULT_KEY,
                PlayerController.ResolvePrimaryCastKey(new PlainEditor(), DEFAULT_KEY),
                "Opting in is per editor. Buildings, Tile, Items and the rest must be untouched.");
        }

        // ── Open, with a selection ───────────────────────────────────────────────

        [Test]
        public void AnOptedInEditorRedirectsTheClickToItsSelection()
        {
            var ed = new ChoosingEditor { Key = "laser_beam_green" };

            Assert.AreEqual("laser_beam_green",
                PlayerController.ResolvePrimaryCastKey(ed, DEFAULT_KEY));
        }

        [Test]
        public void AnOptedInEditorWithNoSelectionFallsBack()
        {
            // Opening the editor before picking anything must not disarm left click.
            Assert.AreEqual(DEFAULT_KEY,
                PlayerController.ResolvePrimaryCastKey(new ChoosingEditor { Key = null }, DEFAULT_KEY));
            Assert.AreEqual(DEFAULT_KEY,
                PlayerController.ResolvePrimaryCastKey(new ChoosingEditor { Key = "" }, DEFAULT_KEY));
        }

        [Test]
        public void TheRedirectIsReadEveryFrameRatherThanLatched()
        {
            // Changing the selection has to take effect immediately: the resolver holds no
            // state of its own, so there is nothing to go stale.
            var ed = new ChoosingEditor { Key = "laser_beam_red" };
            Assert.AreEqual("laser_beam_red", PlayerController.ResolvePrimaryCastKey(ed, DEFAULT_KEY));

            ed.Key = "iceball";
            Assert.AreEqual("iceball", PlayerController.ResolvePrimaryCastKey(ed, DEFAULT_KEY));

            ed.Key = null;
            Assert.AreEqual(DEFAULT_KEY, PlayerController.ResolvePrimaryCastKey(ed, DEFAULT_KEY));
        }

        [Test]
        public void ManaWaiverExistsOnlyForTheActiveRedirectedSelection()
        {
            var spellsEditor = new ChoosingEditor
            {
                Key = "fireball",
                IgnoreManaCost = true
            };

            Assert.IsTrue(PlayerController.PrimaryCastIgnoresManaCost(spellsEditor),
                "An active F4 selection should be free to cast while authoring.");

            spellsEditor.Key = null;
            Assert.IsFalse(PlayerController.PrimaryCastIgnoresManaCost(spellsEditor),
                "An editor with no selected spell has no cast to exempt.");

            Assert.IsFalse(PlayerController.PrimaryCastIgnoresManaCost(null),
                "Closing F4 removes the active editor, restoring normal mana rules.");
            Assert.IsFalse(PlayerController.PrimaryCastIgnoresManaCost(new PlainEditor()),
                "Other runtime editors must not affect spell mana costs.");
        }

        // ── The two editor contracts do not overlap ──────────────────────────────

        [Test]
        public void AnEditorThatSuspendsCombatIsNotAlsoAskedToChooseASpell()
        {
            // Tile-style editors paint with left click and implement ISuspendsPlayerCombat so
            // the painting gesture does not also cast. Implementing both would be asking for a
            // cast that is then suppressed — contradictory, and a sign someone picked the wrong
            // contract.
            //
            // Reflection rather than a source scan: PlayerController legitimately names both
            // interfaces because it is the code that consults them, and no amount of text
            // matching separates "mentions" from "implements".
            var offenders = System.AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => a.GetName().Name.StartsWith("Valkur."))
                .SelectMany(a =>
                {
                    try { return a.GetTypes(); }
                    catch (System.Reflection.ReflectionTypeLoadException e) { return e.Types.Where(t => t != null); }
                })
                .Where(t => t != null && !t.IsInterface
                         && typeof(IChoosesPrimaryCastSpell).IsAssignableFrom(t)
                         && typeof(ISuspendsPlayerCombat).IsAssignableFrom(t))
                .Select(t => t.FullName)
                .ToList();

            Assert.IsEmpty(offenders,
                "These implement both contracts. An editor either suspends combat or redirects " +
                "it — never both.\n  " + string.Join("\n  ", offenders));
        }

        // ── Reaching the click at all ────────────────────────────────────────────

        [Test]
        public void AnOptedInEditorIsTheOnlyOneThatGetsLeftClickBack()
        {
            // The Spells editor does not implement IAllowsPlayerMovement, so
            // ShouldSuspendInputFor is true and Update returns before it polls combat. Without
            // an explicit path the whole redirect is dead code — which is exactly what the
            // first version of this feature was.
            Assert.IsTrue(PlayerController.ShouldSuspendInputFor(new ChoosingEditor { Key = "fireball" }),
                "If this ever becomes false the editor has picked up IAllowsPlayerMovement, " +
                "which unfreezes WASD — and ReadInput has no focused-field guard, so typing in " +
                "the editor's search box would walk the player.");

            Assert.IsTrue(PlayerController.EditorRedirectsPrimaryCast(new ChoosingEditor { Key = "fireball" }));
            Assert.IsFalse(PlayerController.EditorRedirectsPrimaryCast(new PlainEditor()));
            Assert.IsFalse(PlayerController.EditorRedirectsPrimaryCast(null));
            Assert.IsFalse(PlayerController.EditorRedirectsPrimaryCast(new ChoosingEditor { Key = "" }),
                "An editor open with nothing selected must stay fully frozen, not half-armed.");
        }

        [Test]
        public void TheNarrowPathRunsWhileInputIsSuspended()
        {
            string src = Source("Gameplay", "Player", "PlayerController.Movement.cs");

            Assert.IsTrue(src.Contains("if (!isSpirit) PollRedirectedPrimaryCast();"),
                "The redirect has to run inside the input-suspended branch. Placed after the " +
                "early return it never executes while any editor is open.");
            Assert.IsFalse(src.Contains("if (isStunned || inputSuspended) return;"),
                "That combined early return is what made the first version dead code.");
        }

        [Test]
        public void TheSpellsEditorDoesNotUnfreezeTheRestOfTheGame()
        {
            string src = Source("Gameplay", "Editors", "Spells", "SpellsRuntimeEditor.cs");

            Assert.IsFalse(src.Contains("IAllowsPlayerMovement"),
                "Marking it movement-allowed would give back WASD, dash, right-click slash and " +
                "the number-key casts, and would re-arm WorldDropInteractor's left-click drag " +
                "to fight this one for the same click.");
        }

        // ── Wiring ───────────────────────────────────────────────────────────────

        private static string Source(params string[] parts) =>
            File.ReadAllText(Path.Combine(Application.dataPath, "_Project", "Scripts",
                Path.Combine(parts)));

        [Test]
        public void TheSpellsEditorReportsNothingWhileClosed()
        {
            string src = Source("Gameplay", "Editors", "Spells", "SpellsRuntimeEditor.cs");

            Assert.IsTrue(src.Contains("PrimaryCastSpellKey => _active ? _selectedKey : null"),
                "The editor is a scene singleton that outlives its open state. Returning the " +
                "selection unconditionally would keep redirecting the player's primary attack " +
                "long after F4 was closed.");
        }

        [Test]
        public void TheClickIsResolvedEveryFrameRatherThanHardcoded()
        {
            string src = Source("Gameplay", "Player", "PlayerController.Movement.cs");

            Assert.IsFalse(src.Contains("TryCastByKey(\"fireball\""),
                "The left-click branch must go through the resolver. A hardcoded key here " +
                "silently wins over whatever the editor selected.");
            Assert.IsTrue(src.Contains("CastHeldPrimary(ResolvePrimaryCastKey())"));
        }

        [Test]
        public void ReleasingLeftClickOnlyStopsTheBeamLeftClickStarted()
        {
            string src = Source("Gameplay", "Player", "PlayerController.Movement.cs");

            Assert.IsTrue(src.Contains("if (_leftHeldBeam != null) _leftHeldBeam.Stop();"),
                "There is one LaserBeamController per caster, so an unqualified Stop() on left " +
                "release would also cut short a beam the player is channelling on middle click.");
            Assert.IsTrue(src.Contains("_leftHeldBeam = null;"),
                "The reference must be dropped on release or the next click stops a beam that " +
                "is already gone.");
        }

        [Test]
        public void AChannelledSpellIsRefreshedRatherThanRecast()
        {
            string src = Source("Gameplay", "Player", "PlayerController.Movement.cs");

            Assert.IsTrue(src.Contains("spell.type == SpellType.Beam"),
                "Left click can now be pointed at any spell, beams included. A beam cast once " +
                "and never refreshed dies after AUTO_STOP_GRACE and reads as a flicker.");
            Assert.IsTrue(src.Contains("beam.Refresh()"));
        }

        [Test]
        public void ClicksOnTheEditorsOwnPanelsStillDoNotCast()
        {
            // This guard predates the feature and is what stops a click on the picker grid from
            // also firing into the world behind it. The redirect makes it load-bearing in a way
            // it was not before, so it is pinned here.
            string src = Source("Gameplay", "Player", "PlayerController.Movement.cs");

            Assert.IsTrue(src.Contains("if (IsPointerOverInteractiveUI()) return;"),
                "Without this, every click on a spell tile would also cast into the world.");
        }

        // ---------------------------------------------------------------------------------
        // Held-click throttle.
        //
        // The redirected poll reads the button as HELD, so a spell whose own cooldownDuration
        // is 0 — every AnimationProbe, and several authoring-time spells — fired once per
        // frame for as long as the button was down. Sixty casts a second stack into something
        // the author cannot read, in the one editor that exists for looking at a spell.
        // ---------------------------------------------------------------------------------

        [Test]
        public void AFreshPressAlwaysCasts()
        {
            // The gate is for the HOLD. A deliberate click that waits out an interval left
            // over from the previous one reads as the editor having ignored it.
            Assert.IsTrue(PlayerController.AllowsRedirectedCast(
                channelled: false, pressedThisFrame: true, now: 10f, nextAllowedTime: 10.9f));
        }

        [Test]
        public void AHeldClickIsRefusedUntilTheIntervalHasElapsed()
        {
            Assert.IsFalse(PlayerController.AllowsRedirectedCast(
                channelled: false, pressedThisFrame: false, now: 10.5f, nextAllowedTime: 11f));
        }

        [Test]
        public void AHeldClickCastsAgainOnceTheIntervalHasElapsed()
        {
            Assert.IsTrue(PlayerController.AllowsRedirectedCast(
                channelled: false, pressedThisFrame: false, now: 11f, nextAllowedTime: 11f));
        }

        [Test]
        public void AChannelledSpellIsNeverThrottled()
        {
            // A beam is refreshed every frame and ended by the release, so gating it would
            // chop the laser into one-second pulses rather than limit anything.
            Assert.IsTrue(PlayerController.AllowsRedirectedCast(
                channelled: true, pressedThisFrame: false, now: 10.5f, nextAllowedTime: 11f));
        }

        [Test]
        public void TheIntervalIsOneSecondAndOnlyTheRedirectedPollUsesIt()
        {
            // Ordinary gameplay must be untouched: the primary attack is not throttled, and a
            // spell's own cooldownDuration stays the only thing pacing it outside the editor.
            string src = Source("Gameplay", "Player", "PlayerController.Movement.cs");

            Assert.IsTrue(src.Contains("REDIRECTED_CAST_REPEAT_INTERVAL = 1f"),
                "One second between repeats of a held click in a redirecting editor.");
            // Two: the declaration and its single call site inside PollRedirectedPrimaryCast.
            // A third would mean something outside the redirected poll is being paced by it.
            Assert.AreEqual(2, CountOccurrences(src, "AllowsRedirectedCast("),
                "The gate belongs to PollRedirectedPrimaryCast alone. A second reader would be " +
                "pacing ordinary gameplay too.");
        }

        private static int CountOccurrences(string haystack, string needle)
        {
            int n = 0;
            for (int i = haystack.IndexOf(needle, System.StringComparison.Ordinal);
                 i >= 0;
                 i = haystack.IndexOf(needle, i + needle.Length, System.StringComparison.Ordinal))
                n++;
            return n;
        }
    }
}
