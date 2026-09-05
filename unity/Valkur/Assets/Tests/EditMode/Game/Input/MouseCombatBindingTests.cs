using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using Valkur.Data;

namespace Valkur.Tests.EditMode.Game.Input
{
    /// <summary>
    /// The three mouse buttons must actually CAST in War stance, on a character who has just
    /// been created.
    ///
    /// <para>This is a COMPOSITION test, and it exists because both halves were internally
    /// consistent while the composition was broken — the shape
    /// <c>SPAWNER_COORDINATE_SPACE_DRIFT</c> is named for. <c>PollCombatActions</c> correctly
    /// asked for <c>fireball</c> / <c>slash</c> / <c>laser_beam</c>; <c>SyncSpellBook</c>
    /// correctly replaced the book with what the character knows. Nothing logged, nothing
    /// failed, and all three buttons were dead from level 0 — measured live:
    /// <c>KnowsSpell("fireball") == False</c> on a spawned player. <c>SyncSpellBook</c>'s own
    /// comment even claims slot 0 is spared "because having it empty on a fresh character
    /// would read as the game not responding to clicks"; that protects the spell BAR slot,
    /// while <c>ReplaceSpellBook</c> clears the BOOK regardless, so the intent was defeated by
    /// the layer underneath it.</para>
    ///
    /// <para>Asserting on either half alone proves nothing, so this reads the key out of the
    /// production source and looks for it in the shipped catalog.</para>
    /// </summary>
    [TestFixture]
    public class MouseCombatBindingTests
    {
        private const string CatalogResourcePath = "Progression/ProgressionCatalog";

        private static string ScriptsRoot()
            => Path.Combine(Application.dataPath, "_Project", "Scripts");

        private static string Movement()
            => File.ReadAllText(Path.Combine(ScriptsRoot(),
                "Gameplay", "Player", "PlayerController.Movement.cs"));

        private static ProgressionCatalog LoadCatalog()
        {
            var catalog = Resources.Load<ProgressionCatalog>(CatalogResourcePath);
            Assert.IsNotNull(catalog,
                $"Shipped catalog missing at Resources/{CatalogResourcePath}. " +
                "PlayerProgression is AddComponent-ed and has no inspector slot, so this " +
                "Resources path IS the wiring.");
            return catalog;
        }

        /// <summary>
        /// Left click is resolved through <c>DEFAULT_PRIMARY_SPELL_KEY</c> rather than being
        /// written inline, so the constant is where the answer lives.
        /// </summary>
        [Test]
        public void LeftClick_CastsFireball()
        {
            StringAssert.Contains("private const string DEFAULT_PRIMARY_SPELL_KEY = \"fireball\";",
                Movement(),
                "Left click's primary cast key changed. Historical config (Python parity) is fireball.");
        }

        [Test]
        public void RightClick_CastsSlash()
        {
            StringAssert.Contains("TryCastByKey(\"slash\", _facingDirection)", Movement(),
                "Right click must cast slash — the historical M_RIGHT binding.");
        }

        [Test]
        public void MiddleClick_CastsLaserBeam()
        {
            StringAssert.Contains("TryCastByKey(\"laser_beam\", _facingDirection)", Movement(),
                "Middle click must cast laser_beam — the historical M_MIDDLE binding.");
        }

        /// <summary>
        /// The half that was missing. A mouse button bound to a spell the character cannot
        /// cast is a button that does nothing, and it is indistinguishable from the game
        /// having stopped responding.
        /// </summary>
        [Test]
        public void EveryMouseSpell_IsKnownFromLevelZero()
        {
            var catalog = LoadCatalog();
            var innate = (catalog.alwaysKnownSpellKeys ?? new string[0]).ToList();

            foreach (var key in new[] { "fireball", "slash", "laser_beam" })
                Assert.Contains(key, innate,
                    $"'{key}' is bound to a mouse button but is not in alwaysKnownSpellKeys, " +
                    "so SyncSpellBook drops it from the book and that button silently does " +
                    "nothing on a fresh character.");
        }

        /// <summary>
        /// <c>slash</c> and <c>slash_regular</c> are DIFFERENT assets, and the difference is
        /// exactly what made the earlier state so hard to see: the character really did know a
        /// slash from level 0, just not the one right click casts.
        /// </summary>
        [Test]
        public void SlashAndSlashRegular_AreBothInnateAndDistinct()
        {
            var innate = (LoadCatalog().alwaysKnownSpellKeys ?? new string[0]).ToList();

            Assert.Contains("slash", innate, "Right click's spell.");
            Assert.Contains("slash_regular", innate,
                "The innate melee slash, reserved to the armed attack animation.");
            Assert.AreNotEqual("slash", "slash_regular",
                "Guard against someone 'tidying' these into one key: they are separate assets " +
                "with separate animation reservations.");
        }

        /// <summary>
        /// Peace must take all three away and War must give all three back — otherwise the
        /// stance is decorative on the input the player uses most.
        /// </summary>
        [Test]
        public void AllThreeMouseCasts_LiveInsideTheGatedPoll()
        {
            string src = Movement();
            int combatDecl = src.IndexOf("private void PollCombatActions()", StringComparison.Ordinal);
            int traversalDecl = src.IndexOf("private void PollTraversal()", StringComparison.Ordinal);

            Assert.Greater(combatDecl, 0, "PollCombatActions not found.");
            Assert.Greater(traversalDecl, 0, "PollTraversal not found.");
            Assert.Less(traversalDecl, combatDecl, "Expected PollTraversal declared first.");

            // The three reads go through InputBindingResolver against the three ACTIONS, not
            // through MouseInputManager's per-button helpers. Both OR the two backends; only
            // the resolver asks the action what it is bound to, which is what makes the mouse
            // rebindable at all — the helper call hardcoded "the primary attack is the left
            // mouse button" in the one place a Controls editor cannot reach.
            foreach (var needle in new[]
                     {
                         "InputBindingResolver.IsPressed(primaryAction)",
                         "InputBindingResolver.WasPerformedThisFrame(SecondaryAttackAction)",
                         "InputBindingResolver.IsPressed(middleAction)",
                     })
            {
                // Searched FROM the declaration, not from the start of the file. The primary
                // read appears twice: PollRedirectedPrimaryCast reads it too, and that one is
                // deliberately ahead of the stance gate so the F4 Spells Editor can still
                // cast. A plain IndexOf finds the editor's copy and calls correct code broken
                // — which is what it did on the first run of this fixture.
                int at = src.IndexOf(needle, combatDecl, StringComparison.Ordinal);
                Assert.Greater(at, combatDecl,
                    $"\"{needle}\" must appear inside PollCombatActions so the Peace stance " +
                    "gates it. Outside that method it keeps firing in a stance whose whole " +
                    "promise is that the player cannot attack.");
            }

            // Each is ALSO gated on its own descriptor's stance mask, so a player can silence
            // one without leaving War. The coarse gate and the per-action mask are different
            // guarantees and both have to be here.
            foreach (var needle in new[]
                     {
                         "InputContextPolicy.IsLive(_descPrimaryAttack)",
                         "InputContextPolicy.IsLive(_descSecondaryAttack)",
                         "InputContextPolicy.IsLive(_descMiddleClick)",
                     })
            {
                Assert.Greater(src.IndexOf(needle, combatDecl, StringComparison.Ordinal), combatDecl,
                    $"\"{needle}\" must gate its mouse cast inside PollCombatActions.");
            }
        }
    }
}
