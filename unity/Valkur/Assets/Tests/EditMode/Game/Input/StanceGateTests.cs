using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace Valkur.Tests.EditMode.Game.Input
{
    /// <summary>
    /// Structural guard over the Peace stance gate in <c>PlayerController</c>.
    ///
    /// <para>Every claim here is about ORDER inside one method, which is exactly the kind of
    /// thing that survives a refactor looking correct and is wrong. A stance gate hoisted two
    /// lines up takes the dash away; hoisted to the top of <c>Update</c> it takes the F4
    /// Spells Editor's redirected click away. Neither fails anything at runtime — the code
    /// compiles, the console stays clean, and the loss is only visible by playing the exact
    /// case. Same family as <c>CastOriginContractTests</c>, and it carries that fixture's
    /// warning too: when a call legitimately MOVES, point this test at its new owner rather
    /// than re-inlining anything to satisfy the scan.</para>
    /// </summary>
    [TestFixture]
    public class StanceGateTests
    {
        private static string Movement()
            => ReadScript(Path.Combine("Gameplay", "Player", "PlayerController.Movement.cs"));

        private static string ReadScript(params string[] relative)
        {
            string path = Path.Combine(ScriptsRoot(), Path.Combine(relative));
            Assert.IsTrue(File.Exists(path), $"Expected production script at {path}");
            return File.ReadAllText(path);
        }

        // Application.dataPath = …/Valkur/Assets in the Editor.
        private static string ScriptsRoot()
            => Path.Combine(Application.dataPath, "_Project", "Scripts");

        private static int SoleIndexOf(string haystack, string needle)
        {
            int count = 0, from = 0, idx;
            while ((idx = haystack.IndexOf(needle, from, StringComparison.Ordinal)) >= 0)
            {
                count++;
                from = idx + needle.Length;
            }
            Assert.AreEqual(1, count,
                $"Expected exactly one occurrence of \"{needle}\"; found {count}. " +
                "If it legitimately moved, update this fixture rather than restoring the old shape.");
            return haystack.IndexOf(needle, StringComparison.Ordinal);
        }

        /// <summary>
        /// The dash runs on BOTH sides of the stance. A dash is how the player gets out of the
        /// way; a Peace stance that removes it is a Peace stance that gets them killed, and
        /// with nothing auto-switching there is no recovery from it.
        /// </summary>
        [Test]
        public void Traversal_RunsBeforeTheStanceGate()
        {
            string src = Movement();
            int traversal = SoleIndexOf(src, "PollTraversal();");
            int gate      = SoleIndexOf(src, "if (PlayerStance.IsPeace) return;");

            Assert.Less(traversal, gate,
                "PollTraversal must be called before the Peace gate, or Peace removes the dash.");
        }

        [Test]
        public void CombatPoll_RunsAfterTheStanceGate()
        {
            string src = Movement();
            int gate   = SoleIndexOf(src, "if (PlayerStance.IsPeace) return;");
            int combat = SoleIndexOf(src, "PollCombatActions();");

            Assert.Less(gate, combat,
                "The Peace gate must precede PollCombatActions, or Peace does nothing at all.");
        }

        /// <summary>
        /// The dash cast belongs to PollTraversal, not to PollCombatActions. The two calls are
        /// ordered above, but order alone would still pass if the dash were left behind in the
        /// combat half — so this pins WHICH method actually contains it.
        /// </summary>
        [Test]
        public void DashCast_LivesInsideTraversal()
        {
            string src = Movement();
            int traversalDecl = SoleIndexOf(src, "private void PollTraversal()");
            int combatDecl    = SoleIndexOf(src, "private void PollCombatActions()");
            int dashCast      = SoleIndexOf(src, "TryCastByKey(\"dash\"");

            Assert.Less(traversalDecl, combatDecl,
                "PollTraversal is expected to be declared above PollCombatActions.");
            Assert.That(dashCast, Is.GreaterThan(traversalDecl).And.LessThan(combatDecl),
                "The dash cast must sit inside PollTraversal so Peace cannot take it away.");
        }

        /// <summary>
        /// The F4 Spells Editor's redirected left click reaches the world through
        /// <c>PollRedirectedPrimaryCast</c>, inside the editor-suspended branch far above the
        /// gate. It works by CONSTRUCTION rather than by intent, which is precisely why it
        /// needs pinning: the obvious tidy-up is to hoist the stance check to the top of
        /// Update, and that would silently stop an author trying a spell out while in Peace.
        /// </summary>
        [Test]
        public void EditorRedirectedCast_IsNotBehindTheStanceGate()
        {
            string src = Movement();
            int redirected = SoleIndexOf(src, "PollRedirectedPrimaryCast();");
            int gate       = SoleIndexOf(src, "if (PlayerStance.IsPeace) return;");

            Assert.Less(redirected, gate,
                "PollRedirectedPrimaryCast must be reached before the Peace gate, or the F4 " +
                "Spells Editor cannot cast while the player is in Peace.");
        }

        /// <summary>
        /// One gate, one reader. Peace gates the spell hotkeys by gating the single method
        /// that polls them — so a second poll anywhere else in production would be 24 combat
        /// bindings that Peace does not cover, and nothing would fail.
        /// </summary>
        [Test]
        public void SpellBindings_HaveExactlyOneProductionConsumer()
        {
            string root = ScriptsRoot();
            var consumers = Directory
                .GetFiles(root, "*.cs", SearchOption.AllDirectories)
                .Where(f => File.ReadAllText(f).Contains("EnumerateSpellBindings()"))
                .Select(f => Path.GetFileName(f))
                .OrderBy(n => n, StringComparer.Ordinal)
                .ToList();

            CollectionAssert.AreEqual(
                new[] { "InputService.cs", "PlayerController.Movement.cs" },
                consumers,
                "EnumerateSpellBindings must be declared in InputService and consumed only by " +
                "PlayerController.Movement. A second consumer is a set of combat bindings the " +
                "Peace stance does not gate.");
        }

        /// <summary>
        /// Tab must reach ToggleStance and nothing else — in the SOURCE, not only in the
        /// asset. The asset-only version of this check passed while
        /// <c>InventoryUI</c> held a legacy <c>KeyCode.Tab</c> read with no matching entry in
        /// ValkurInputActions at all, so Tab opened the inventory AND flipped the stance and
        /// the binding audit could not see half of it. Two features on one physical key has
        /// now shipped three times here: <c>e</c> on Interact and SpellSlash, <c>p</c> on
        /// Pause and SpellMeteorShower (pausing threw meteors), and this.
        /// </summary>
        [Test]
        public void Tab_HasExactlyOneReaderInProductionSource()
        {
            // The legitimate readers, and NONE of them can be live at the same moment as the
            // stance toggle — each exclusion is structural, not a coincidence:
            //   • Core/Input/KeyboardInputManager IS the helper. It defines the key; it does
            //     not claim a meaning for it.
            //   • A runtime EDITOR owns the world while open (Buildings uses Tab to cycle),
            //     and PlayerStanceToggle.IsSuppressed refuses under AnyEditorActive.
            //   • DevConsole's tab-complete sits behind `if (!_open) return;`, and an open
            //     console is precisely when ChatInputGate sets InputBlocker.IsGameplayBlocked,
            //     which is the other half of that same IsSuppressed check.
            // Verified by reading each guard, because "it probably cannot happen" is how a
            // double binding survives — this project has shipped three of them.
            //
            // PLAYERSTANCETOGGLE IS NO LONGER ON THIS LIST, and that is the point rather than
            // a regression: it used to OR the bound action with a literal
            // WasTabPressedThisFrame(), so a player who moved the stance toggle elsewhere
            // still flipped it with Tab — half a rebind, silently. It reads through
            // InputBindingResolver now, which derives the legacy half from whatever the action
            // is bound to, so the file names no key at all. A production file that DOES name
            // Tab is once again the thing to worry about.
            var readers = Directory
                .GetFiles(ScriptsRoot(), "*.cs", SearchOption.AllDirectories)
                .Where(f =>
                {
                    string rel = f.Substring(ScriptsRoot().Length).Replace(Path.DirectorySeparatorChar, '/').Replace('\\', '/');
                    if (rel.Contains("/Core/Input/")) return false;
                    if (rel.Contains("/Editors/")) return false;
                    string src = File.ReadAllText(f);
                    return src.Contains("KeyCode.Tab") || src.Contains("Key.Tab")
                        || src.Contains("WasTabPressedThisFrame");
                })
                .Select(Path.GetFileName)
                .OrderBy(n => n, StringComparer.Ordinal)
                .ToList();

            CollectionAssert.AreEqual(
                new[] { "DevConsole.cs" },
                readers,
                "Tab may have exactly one GAMEPLAY meaning, and it is expressed as a BINDING " +
                "on ToggleStance, not as a literal in source. A literal reader outside the " +
                "input helpers and the runtime editors is two features on one key AND a half " +
                "that no rebind can move — which is exactly how InventoryUI's Tab survived " +
                "the asset check below for the whole life of the stance feature.");
        }

        [Test]
        public void Tab_IsBoundOnlyToToggleStance()
        {
            string asset = Path.Combine(Application.dataPath,
                "_Project", "Resources", "Input", "ValkurInputActions.inputactions");
            Assert.IsTrue(File.Exists(asset), $"Input asset missing at {asset}");

            string[] lines = File.ReadAllLines(asset);
            var actionsBoundToTab = lines
                .Select((line, i) => (line, i))
                .Where(t => t.line.Contains("\"path\": \"<Keyboard>/tab\""))
                .Select(t => lines.Skip(t.i).First(l => l.Contains("\"action\":")))
                .ToList();

            // Tab may now be bound more than once, and that is the context model working
            // rather than a regression: an open runtime editor owns the whole keyboard and
            // the postures do not apply inside one, so the Buildings editor's CG/CU scope
            // toggle is free to be Tab too. What must stay unique is Tab's GAMEPLAY meaning —
            // two gameplay actions on it would fire together, which is the shape this project
            // has shipped three times.
            var gameplayHolders = actionsBoundToTab
                .Where(a => !a.Contains("ToggleColliderScope"))
                .ToList();

            Assert.AreEqual(1, gameplayHolders.Count,
                "Exactly one GAMEPLAY binding may use <Keyboard>/tab. Editor tools may reuse " +
                "it because an editor context is a separate layout:\n" +
                string.Join("\n", actionsBoundToTab));
            StringAssert.Contains("ToggleStance", gameplayHolders[0]);
        }
    }
}
