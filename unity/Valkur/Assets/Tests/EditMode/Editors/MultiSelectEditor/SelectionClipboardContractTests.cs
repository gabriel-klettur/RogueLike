using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Valkur.Core.Input;

namespace Valkur.Tests.EditMode.Editors.MultiSelectEditor
{
    /// <summary>
    /// The cross-domain clipboard: its two input actions, and the contract every domain has to
    /// answer for a copy to survive its source.
    ///
    /// <para>None of this can be checked by pressing Ctrl+C in a fixture — the gesture needs
    /// live editors, live content and a pointer. What IS checkable is the DECLARATION, and the
    /// declaration is where this family of feature fails: an action in the asset with no
    /// descriptor, or a descriptor whose owner does not match an editor's exact
    /// <c>EditorName</c>, is silent and kills every tool of that editor.</para>
    /// </summary>
    [TestFixture]
    public class SelectionClipboardContractTests
    {
        private const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic
                                       | BindingFlags.Instance | BindingFlags.Static;

        private static Type DomainType =>
            Type.GetType("Valkur.Gameplay.Editors.MultiSelect.ISelectionDomain, Valkur.Gameplay");

        private static Type[] Domains() => DomainType.Assembly.GetTypes()
            .Where(t => !t.IsInterface && !t.IsAbstract && DomainType.IsAssignableFrom(t))
            .ToArray();

        // ── The two actions ──────────────────────────────────────────────────

        [Test]
        public void CopyAndPaste_AreDeclared_AsThisEditorsOwnCtrlTools()
        {
            foreach (string action in new[] { "Copy", "Paste" })
            {
                var d = InputActionCatalog.Find(InputActionCatalog.MapSelectionEditor, action);
                Assert.NotNull(d,
                    $"'{action}' has no descriptor. InputActionCatalog is a CLOSED table — an " +
                    "action in the asset with none is a red test, and one with a descriptor " +
                    "nobody declared simply never fires.");
                Assert.IsTrue(d.RequiresCtrl,
                    $"'{action}' must require Ctrl, or it would steal the bare C and V keys " +
                    "from every tool this editor might later want.");
                Assert.AreEqual("Seleccion", d.OwnerEditor,
                    "The owner must be the editor's EXACT EditorName. A mismatch is silent: " +
                    "InputContexts.Current builds the context id from that string and " +
                    "InputContextPolicy.IsLive compares the two, which shipped wrong once and " +
                    "killed all 35 per-editor tools at a stroke.");
            }
        }

        [Test]
        public void TheSelectionMapName_IsASlug_NotTheEditorName()
        {
            // The map is Editor.Selection and the owner is Seleccion, deliberately. They are
            // different strings for different jobs, so comparing one against the other would
            // prove nothing — the trap EditorReachabilityTests was written to escape.
            Assert.AreNotEqual("Seleccion", InputActionCatalog.MapSelectionEditor);
            Assert.IsTrue(InputActionCatalog.MapSelectionEditor.StartsWith("Editor.", StringComparison.Ordinal),
                "Per-editor maps are namespaced Editor.<Slug>, like Editor.Tile and Editor.Buildings.");
        }

        [Test]
        public void ThreeEditorsBindCtrlC_AndThatIsNotAConflict()
        {
            // Tile, Buildings and Selection each own a clipboard. They coexist because each is
            // live only inside its own context — the property that gives every editor a whole
            // keyboard, and the one an over-eager conflict "fix" would take away.
            var owners = InputActionCatalog.All
                .Where(d => d.Action == "Copy" && !string.IsNullOrEmpty(d.OwnerEditor))
                .Select(d => d.OwnerEditor)
                .ToList();

            CollectionAssert.Contains(owners, "Seleccion");
            Assert.Greater(owners.Count, 1,
                "More than one editor owns a Copy, which is the arrangement being pinned.");
            CollectionAssert.AllItemsAreUnique(owners,
                "Two descriptors naming the same owner for one action would make one of them dead.");
        }

        // ── The domain contract ──────────────────────────────────────────────

        [Test]
        public void EveryDomain_ImplementsTheClipboardPair()
        {
            foreach (var t in Domains())
            {
                Assert.NotNull(t.GetMethod("CaptureForClipboard", Any),
                    $"{t.Name} cannot snapshot for the clipboard.");
                Assert.NotNull(t.GetMethod("SpawnFromClipboard", Any),
                    $"{t.Name} cannot rebuild from a clipboard token.");
                Assert.NotNull(t.GetMethod("CollectGhostSprites", Any),
                    $"{t.Name} cannot contribute to the paste ghost.");
            }
            Assert.IsNotEmpty(Domains());
        }

        [Test]
        public void CaptureForClipboard_IsSeparateFromDelete_AndFromDuplicate()
        {
            // Three near-neighbours that must not be collapsed into one:
            //   Delete's token is restorable ONCE and in place — a building's is the
            //     deactivated object itself, which cannot be pasted twice or elsewhere.
            //   Duplicate copies from a LIVE object, so it cannot outlive its source.
            //   CaptureForClipboard is by value and outlives everything.
            foreach (var t in Domains())
            {
                var capture   = t.GetMethod("CaptureForClipboard", Any);
                var delete    = t.GetMethod("Delete", Any);
                var duplicate = t.GetMethod("Duplicate", Any);
                Assert.AreNotSame(capture, delete,    $"{t.Name} aliased CaptureForClipboard onto Delete.");
                Assert.AreNotSame(capture, duplicate, $"{t.Name} aliased CaptureForClipboard onto Duplicate.");
            }
        }

        [Test]
        public void EveryDomain_CanDescribeOneInstance_AndSurvivesBeingAskedAboutNull()
        {
            foreach (var t in Domains())
            {
                var d = Activator.CreateInstance(t, nonPublic: true);
                var m = t.GetMethod("Describe", Any);
                Assert.NotNull(m, $"{t.Name} has no Describe — its rows in the selected list " +
                                  "would all read the same and the list could not tell two " +
                                  "stacked things apart, which is the only reason it exists.");

                // Null is the normal case for a member destroyed between the pick and the
                // repaint; a list row that threw there would take the whole panel down.
                string s = (string)m.Invoke(d, new object[] { null });
                Assert.IsNotEmpty(s, $"{t.Name}.Describe(null) must still name the KIND of thing.");
            }
        }

        // ── The way out of a copy ────────────────────────────────────────────

        /// <summary>
        /// A copy has no world side effect, so there is nothing for the undo stack to reverse —
        /// what an author wants back is the POINTER, free of a ghost promising a paste. That is
        /// <c>ClearClipboard</c>, and it shipped with no caller at all: a correct helper nobody
        /// invokes is the authored-and-inert shape this codebase records a dozen times, and the
        /// only way to empty the clipboard was to close the editor.
        /// </summary>
        [Test]
        public void ClearClipboard_IsReachableFromThePanel()
        {
            string ui = System.IO.File.ReadAllText(System.IO.Path.Combine(
                Application.dataPath, "_Project", "Scripts", "Gameplay", "Editors",
                "MultiSelect", "MultiSelectRuntimeEditor.UI.cs"));

            StringAssert.Contains("ClearClipboard(", ui,
                "The panel must wire a control to ClearClipboard. Without one the helper exists " +
                "and nothing can call it, which is how it shipped.");
        }

        [Test]
        public void ClearingTheClipboard_EmptiesItAndDropsTheGhost()
        {
            var editorType = Type.GetType(
                "Valkur.Gameplay.Editors.MultiSelect.MultiSelectRuntimeEditor, Valkur.Gameplay");
            var host = new GameObject("ClipboardHost");
            try
            {
                var ed = host.AddComponent(editorType);

                // Seed one entry directly: building a real clipboard needs live editors, and the
                // property under test is the emptying, not the capture.
                var field = editorType.GetField("_clipboard", Any);
                var list  = (System.Collections.IList)field.GetValue(ed);
                var entryType = field.FieldType.GetGenericArguments()[0];
                list.Add(Activator.CreateInstance(entryType));

                Assert.IsTrue((bool)editorType.GetProperty("HasClipboard", Any).GetValue(ed));

                editorType.GetMethod("ClearClipboard", Any).Invoke(ed, new object[] { false });

                Assert.IsFalse((bool)editorType.GetProperty("HasClipboard", Any).GetValue(ed),
                    "Clearing must actually empty it — the label and the ghost both read this.");
                Assert.AreEqual(0, (int)editorType.GetProperty("ClipboardCount", Any).GetValue(ed));
            }
            finally { UnityEngine.Object.DestroyImmediate(host); }
        }

        [Test]
        public void TheGhost_ResetsItsStaticSprite_BecauseDomainReloadIsOff()
        {
            var ghost = Type.GetType("Valkur.Gameplay.Editors.MultiSelect.SelectionGhost, Valkur.Gameplay");
            Assert.NotNull(ghost);

            var reset = ghost.GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
                .FirstOrDefault(m => m.GetCustomAttributes(typeof(RuntimeInitializeOnLoadMethodAttribute), false).Any());
            Assert.NotNull(reset,
                "The ghost caches a generated Sprite in a static. Domain Reload is OFF, so it " +
                "survives Stop and comes back as a destroyed Unity object on the next Play.");
        }
    }
}
