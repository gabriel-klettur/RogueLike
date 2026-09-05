using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using Valkur.Core;
using Valkur.Core.Input;

namespace Valkur.Tests.EditMode.Game.Input
{
    /// <summary>
    /// The context axis: a play posture (War / Peace) or one runtime editor, and an open
    /// editor beats the posture unconditionally.
    ///
    /// <para>The runtime already behaved this way before the configuration layer knew about
    /// it — <c>IsGameplayInputSuspended</c> freezes gameplay input while any editor is open —
    /// so these pin the half that was missing: that the CONFIGURATION agrees, that a shared
    /// verb really is shared by all sixteen editors, and that an editor's own tool cannot fire
    /// in another editor even when the two put different tools on the same key.</para>
    /// </summary>
    [TestFixture]
    public class InputContextLayerTests
    {
        private InputService _svc;

        [SetUp]
        public void SetUp()
        {
            InputContexts.ResetForTests();
            InputContextPolicy.ResetForTests();
            PlayerStance.ResetForTests();
            InputBindingResolver.ResetForTests();
            _svc = InputService.Initialize();
            Assert.IsNotNull(_svc);
        }

        [TearDown]
        public void TearDown()
        {
            // With Domain Reload off, an editor override left set would tell every later
            // fixture in the session that an editor is open — and the failures would surface
            // in files with no connection to this one.
            InputContexts.ClearActiveEditorOverride();
            InputContexts.ResetForTests();
            InputContextPolicy.ResetForTests();
            PlayerStance.ResetForTests();
            _svc?.Asset?.RemoveAllBindingOverrides();
            InputBindingResolver.ResetForTests();
        }

        // ── The axis ─────────────────────────────────────────────────────────

        [Test]
        public void WithNoEditorOpen_TheContextIsThePosture()
        {
            Assert.AreEqual(InputContexts.War, InputContexts.Current);
            PlayerStance.Set(Stance.Peace);
            Assert.AreEqual(InputContexts.Peace, InputContexts.Current);
        }

        [Test]
        public void AnOpenEditor_BeatsThePosture()
        {
            PlayerStance.Set(Stance.Peace);
            InputContexts.SetActiveEditorOverride("Tile");

            Assert.AreEqual("editor/Tile", InputContexts.Current,
                "While an editor is open the postures do not apply at all — the editor owns " +
                "the keyboard and the mouse.");

            InputContexts.SetActiveEditorOverride(null);
            Assert.AreEqual(InputContexts.Peace, InputContexts.Current,
                "Closing the editor hands the context back to the posture the player was in.");
        }

        [Test]
        public void NoGameplayAction_IsLiveInsideAnEditor()
        {
            InputContexts.SetActiveEditorOverride("Buildings");

            var leaked = InputActionCatalog.All
                .Where(d => d.Map == InputActionCatalog.MapGameplay)
                .Where(d => InputContextPolicy.IsLive(d))
                .Select(d => d.Id)
                .ToList();

            Assert.IsEmpty(leaked,
                "A gameplay verb answering inside an editor is the whole failure this axis " +
                "exists to prevent — an author painting tiles would be casting spells:\n" +
                string.Join("\n", leaked));
        }

        [Test]
        public void TheEditorToggles_StayLiveInsideAnEditor()
        {
            InputContexts.SetActiveEditorOverride("Tile");

            var toggle = InputActionCatalog.Find(InputActionCatalog.MapEditors, "ToggleBuildings");
            Assert.IsNotNull(toggle);
            Assert.IsTrue(InputContextPolicy.IsLive(toggle),
                "An editor toggle that stopped working inside an editor would be a one-way " +
                "door: the author could open the Tile editor and never reach any other.");
        }

        // ── Shared verbs ─────────────────────────────────────────────────────

        [Test]
        public void EverySharedVerb_IsLiveInEveryEditor()
        {
            var shared = InputActionCatalog.All.Where(d => d.IsSharedEditorVerb).ToList();
            Assert.IsNotEmpty(shared, "The shared verbs are the point of the EditorShared map.");

            var editors = EditorNamesInCatalog();
            Assert.IsNotEmpty(editors);

            var missing = new List<string>();
            foreach (var editor in editors)
            {
                InputContexts.SetActiveEditorOverride(editor);
                foreach (var d in shared)
                    if (!InputContextPolicy.IsLive(d))
                        missing.Add($"{d.Id} is not live in {editor}");
            }

            Assert.IsEmpty(missing,
                "Selecting, zooming, scrolling, undo, save and close must behave identically " +
                "in every editor — that is what makes them shared:\n" + string.Join("\n", missing));
        }

        [Test]
        public void NoSharedVerb_IsLiveWhilePlaying()
        {
            var live = InputActionCatalog.All
                .Where(d => d.IsSharedEditorVerb)
                .Where(d => InputContextPolicy.IsLive(d, InputContexts.War)
                         || InputContextPolicy.IsLive(d, InputContexts.Peace))
                .Select(d => d.Id)
                .ToList();

            Assert.IsEmpty(live,
                "Ctrl+Z during play must not reach an editor that merely exists in the scene:\n" +
                string.Join("\n", live));
        }

        // ── One editor's own tools ───────────────────────────────────────────

        [Test]
        public void AnEditorTool_IsLiveOnlyInItsOwnEditor()
        {
            var tools = InputActionCatalog.All
                .Where(d => !string.IsNullOrEmpty(d.OwnerEditor))
                .ToList();
            Assert.IsNotEmpty(tools);

            var leaked = new List<string>();
            foreach (var editor in EditorNamesInCatalog())
            {
                InputContexts.SetActiveEditorOverride(editor);
                foreach (var d in tools)
                {
                    bool live = InputContextPolicy.IsLive(d);
                    bool owns = d.OwnerEditor == editor;
                    if (live != owns)
                        leaked.Add($"{d.Id} (owner {d.OwnerEditor}) live={live} in {editor}");
                }
            }

            Assert.IsEmpty(leaked,
                "An editor owns the whole board while it is open, so two editors may put " +
                "different tools on the same key — which is only safe because a tool answers " +
                "in ITS editor and nowhere else:\n" + string.Join("\n", leaked));
        }

        /// <summary>
        /// Two editors really do share keys, and that is deliberate. Pinning it stops somebody
        /// "fixing" the overlap and losing the property that each editor gets a full keyboard.
        /// </summary>
        [Test]
        public void TwoEditorsSharingAKey_IsNotAConflict()
        {
            var byPath = InputConflictScanner.BindingsByPath(_svc.Asset);

            var shared = byPath
                .Where(kv => kv.Value.Count(d => !string.IsNullOrEmpty(d.OwnerEditor)) > 1)
                .ToList();

            Assert.IsNotEmpty(shared,
                "Expected at least one key carrying a tool in two different editors — the " +
                "Tile brush and the Buildings collider brush are both on B.");

            foreach (var kv in shared)
            {
                var owners = kv.Value.Where(d => !string.IsNullOrEmpty(d.OwnerEditor)).ToList();
                for (int i = 0; i < owners.Count; i++)
                for (int j = i + 1; j < owners.Count; j++)
                    Assert.AreNotEqual(owners[i].OwnerEditor, owners[j].OwnerEditor,
                        $"{kv.Key}: {owners[i].Id} and {owners[j].Id} are BOTH owned by " +
                        $"{owners[i].OwnerEditor}, so they really would fire together.");
            }
        }

        [Test]
        public void EveryToolOwner_NamesAMapThatCarriesIt()
        {
            var wrong = new List<string>();
            foreach (var d in InputActionCatalog.All)
            {
                if (string.IsNullOrEmpty(d.OwnerEditor)) continue;
                var map = _svc.Asset.FindActionMap(d.Map, throwIfNotFound: false);
                if (map == null) { wrong.Add($"{d.Id}: no map '{d.Map}'"); continue; }
                if (map.FindAction(d.Action, throwIfNotFound: false) == null)
                    wrong.Add($"{d.Id}: map '{d.Map}' has no such action");

                // The map name is a SLUG ("Editor.Tile"); the owner is the editor's real
                // EditorName ("Tile Editor"), spaces and all. They are deliberately not the
                // same string, so comparing them proves nothing — what matters is that the
                // owner names a real editor, which EditorReachabilityTests asserts against
                // the shipped EditorName declarations.
            }

            Assert.IsEmpty(wrong, string.Join("\n", wrong));
        }

        // ── The Peace guarantee survives the rework ──────────────────────────

        [Test]
        public void PeaceStillRefusesDamage_UnderTheContextModel()
        {
            var accepted = InputActionCatalog.All
                .Where(d => d.ReachesDamage)
                .Where(d => InputContextPolicy.IsLive(d, InputContexts.Peace))
                .Select(d => d.Id)
                .ToList();

            Assert.IsEmpty(accepted,
                "Renaming the axis must not have loosened the one rule that is not " +
                "configurable:\n" + string.Join("\n", accepted));
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        /// <summary>
        /// The editors the catalog knows about, from the tool owners.
        ///
        /// <para>NOTE WHAT THIS CANNOT PROVE. Deriving the editor list from the owners means
        /// every assertion below compares the owners against themselves — which is why the
        /// first version of this fixture passed while every owner was wrong ("Tile" against a
        /// real EditorName of "Tile Editor") and every per-editor tool was dead.
        /// <c>EditorReachabilityTests</c> is the half that reads the OTHER side, from the
        /// shipped <c>EditorName</c> declarations; these tests are about the mask logic given
        /// a set of owners, and nothing more.</para>
        /// </summary>
        private static List<string> EditorNamesInCatalog()
        {
            var names = InputActionCatalog.All
                .Where(d => !string.IsNullOrEmpty(d.OwnerEditor))
                .Select(d => d.OwnerEditor)
                .Distinct()
                .OrderBy(n => n, System.StringComparer.Ordinal)
                .ToList();
            return names;
        }
    }
}
