using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using Valkur.Core;
using Valkur.Core.Input;
using Valkur.Gameplay.Editors.Controls;
using Valkur.Gameplay.TileEditor;
using Valkur.UIKit;

namespace Valkur.Tests.EditMode.Editors.Controls
{
    /// <summary>
    /// The Controls editor itself, not the binding layer under it.
    ///
    /// <para>WHY THIS FIXTURE HAD TO EXIST. The layer was green — 26 tests over the catalog,
    /// the policy, the resolver and the shipped asset — while the panel on top of it could not
    /// perform its primary function in four separate ways: the capture overlay covered the
    /// board it invited you to click, the mouse could not be bound at all, Escape closed the
    /// editor instead of cancelling, and the fourteen actions the project documents as "put a
    /// key back here" had no binding slot to put one in. Every one of those is a UI fact, and
    /// no test asked a UI question. That is the same shape as the spawner coordinate drift:
    /// both halves correct, the composition wrong.</para>
    /// </summary>
    [TestFixture]
    public class ControlsEditorTests
    {
        private const BindingFlags Priv =
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

        private InputService _svc;
        private GameObject _host;
        private ControlsRuntimeEditor _editor;

        [SetUp]
        public void SetUp()
        {
            InputContexts.ResetForTests();
            InputContextPolicy.ResetForTests();
            InputBindingStore.ResetForTests();
            InputBindingResolver.ResetForTests();
            EscapeOwnership.ResetForTests();
            PlayerStance.ResetForTests();

            _svc = InputService.Initialize();
            Assert.IsNotNull(_svc);
        }

        [TearDown]
        public void TearDown()
        {
            if (_host != null) Object.DestroyImmediate(_host);
            _host = null;
            _editor = null;

            // Binding overrides live on the CANONICAL asset, which survives Domain Reload off
            // and every fixture in the session. A test that rebinds and does not clean up
            // leaves the next fixture reading a moved key, and the failure surfaces somewhere
            // with no connection to this file.
            _svc?.Asset?.RemoveAllBindingOverrides();
            InputBindingResolver.ResetForTests();
            InputContextPolicy.ResetForTests();
            InputBindingStore.ResetForTests();
            EscapeOwnership.ResetForTests();
            InputContexts.ResetForTests();
            PlayerStance.ResetForTests();
        }

        private ControlsRuntimeEditor OpenEditor()
        {
            _host = new GameObject("ControlsEditorUnderTest");
            _editor = _host.AddComponent<ControlsRuntimeEditor>();
            _editor.Activate();
            Assert.IsTrue(_editor.IsActive, "The editor refused to open.");
            return _editor;
        }

        private static T Field<T>(object target, string name) =>
            (T)target.GetType().GetField(name, Priv).GetValue(target);

        private static object Invoke(object target, string name, params object[] args) =>
            target.GetType().GetMethod(name, Priv).Invoke(target, args);

        private ControlsEditorUIBuilder.UIRefs Refs => Field<ControlsEditorUIBuilder.UIRefs>(_editor, "_ui");

        /// <summary>
        /// The action rows the author can actually see. Rows are realised once per context and
        /// then shown or hidden — building them costs 213 ms and the shipped code paid it on
        /// every keystroke — so "is it listed" is a question about visibility, not existence.
        /// </summary>
        private List<GameObject> VisibleRows()
        {
            var entries = (System.Collections.IEnumerable)_editor.GetType()
                .GetField("_entries", Priv).GetValue(_editor);

            var rows = new List<GameObject>();
            foreach (var entry in entries)
            {
                var type = entry.GetType();
                var go = (GameObject)type.GetField("Go").GetValue(entry);
                if ((bool)type.GetField("IsSlotRow").GetValue(entry)) continue;
                if (go != null && go.activeSelf) rows.Add(go);
            }
            return rows;
        }

        private InputAction Action(string map, string action) =>
            _svc.Asset.FindActionMap(map, throwIfNotFound: false)
                     ?.FindAction(action, throwIfNotFound: false);

        private static string EffectivePath(InputAction action, int ordinal)
        {
            int seen = 0;
            for (int i = 0; i < action.bindings.Count; i++)
            {
                if (action.bindings[i].isComposite) continue;
                if (seen == ordinal) return action.bindings[i].effectivePath;
                seen++;
            }
            return null;
        }

        // ── The capture affordances ──────────────────────────────────────────

        /// <summary>
        /// The scrim is BEHIND both panels and the prompt is in FRONT of them.
        ///
        /// <para>This is the defect that made "click a drawn key to bind it" impossible. The
        /// scrim shipped as the last sibling — full screen, a raycast target, carrying a Button
        /// that cancels — so every click on a key cap hit the scrim and cancelled the capture
        /// instead of completing it, while the prompt on that same object told the author to
        /// click the board. It was intermittent, which is worse than broken: DraggablePanel
        /// raises whichever panel was last clicked, so an author who had dragged the board once
        /// had lifted it above the scrim and the click DID land.</para>
        /// </summary>
        [Test]
        public void DuringCapture_TheBoardStaysClickableAndThePromptStaysOnTop()
        {
            OpenEditor();
            var refs = Refs;

            Invoke(_editor, "BeginCapture", InputActionCatalog.Find("Gameplay/Interact"), 0);
            Assert.IsTrue(_editor.IsCapturing);

            var root = refs.BoardPanel.transform.parent;
            int scrim  = refs.CaptureScrim.transform.GetSiblingIndex();
            int prompt = refs.CapturePrompt.transform.GetSiblingIndex();
            int board  = refs.BoardPanel.transform.GetSiblingIndex();
            int list   = refs.ListPanel.transform.GetSiblingIndex();

            Assert.Less(scrim, board, "The scrim must draw BEHIND the board, or clicking a key cancels.");
            Assert.Less(scrim, list,  "The scrim must draw BEHIND the action list.");
            Assert.Greater(prompt, board, "The prompt must draw in FRONT of the board.");
            Assert.Greater(prompt, list,  "The prompt must draw in FRONT of the action list.");
            Assert.AreEqual(root.childCount - 1, prompt, "The prompt must be the top-most child.");

            Assert.IsFalse(refs.CapturePrompt.GetComponent<Image>().raycastTarget,
                "The prompt sits above everything, so it must not eat clicks — that is the " +
                "defect the scrim used to have.");
            Assert.IsTrue(refs.CaptureScrim.GetComponent<Image>().raycastTarget,
                "The scrim IS the click-anywhere-to-cancel affordance.");
        }

        /// <summary>
        /// The order is re-asserted on every capture, so a panel the author dragged an hour ago
        /// cannot leave the scrim on top.
        /// </summary>
        [Test]
        public void CaptureOrder_SurvivesAPanelBeingRaised()
        {
            OpenEditor();
            var refs = Refs;

            // What DraggablePanel.OnPointerDown does on any click.
            refs.BoardPanel.transform.SetAsLastSibling();
            refs.ListPanel.transform.SetAsLastSibling();

            Invoke(_editor, "BeginCapture", InputActionCatalog.Find("Gameplay/Interact"), 0);

            Assert.Less(refs.CaptureScrim.transform.GetSiblingIndex(),
                        refs.BoardPanel.transform.GetSiblingIndex());
            Assert.Greater(refs.CapturePrompt.transform.GetSiblingIndex(),
                           refs.ListPanel.transform.GetSiblingIndex());
        }

        /// <summary>
        /// Escape is CLAIMED while capturing, so the General Editor does not act on the same
        /// press. Update order between the two is undefined, so the claim is the only thing
        /// that makes the outcome deterministic — without it, cancelling a rebind also closed
        /// the editor, sometimes.
        /// </summary>
        [Test]
        public void Capturing_ClaimsEscapeAndReleasesIt()
        {
            OpenEditor();
            Assert.IsFalse(EscapeOwnership.IsClaimed);

            Invoke(_editor, "BeginCapture", InputActionCatalog.Find("Gameplay/Interact"), 0);
            Assert.IsTrue(EscapeOwnership.IsClaimed,
                "GeneralEditorManager reads Escape in the same frame and would close this " +
                "editor out from under the capture it is cancelling.");

            _editor.CancelCapture();
            Assert.AreEqual(0, EscapeOwnership.OwnerCount);
        }

        // ── What can be bound ────────────────────────────────────────────────

        /// <summary>
        /// A mouse button can be given to an action. Nothing could before: the poll read only
        /// the keyboard, and the drawn mouse — the other way in — sat under the scrim.
        /// </summary>
        [Test]
        public void AMouseButtonCanBeBound()
        {
            OpenEditor();
            var interact = InputActionCatalog.Find("Gameplay/Interact");

            Invoke(_editor, "BeginCapture", interact, 0);
            Invoke(_editor, "CompleteCaptureWithMouse", MouseControl.Right);

            Assert.AreEqual("<Mouse>/rightButton", EffectivePath(Action("Gameplay", "Interact"), 0));
            Assert.IsFalse(_editor.IsCapturing, "A completed capture must close itself.");
        }

        /// <summary>
        /// A retired editor toggle can be given a key.
        ///
        /// <para>CLAUDE.md, <c>EditorEntryPointTests</c> and the <c>editors</c> console command
        /// all say a player who wants F8 back can put it here. They could not: the fourteen
        /// toggles shipped with NO binding, and <c>ApplyBindingOverride</c> writes into a slot
        /// rather than creating one, so the panel answered "no tiene ningun binding que
        /// reasignar" to the one thing it exists for.</para>
        /// </summary>
        [Test]
        public void ARetiredEditorToggle_CanBeGivenAKey()
        {
            OpenEditor();
            var toggle = InputActionCatalog.Find("Editors/ToggleTile");
            Assert.IsNotNull(toggle);
            Assert.AreEqual("", EffectivePath(Action("Editors", "ToggleTile"), 0),
                "It must ship unbound — with a slot.");

            Invoke(_editor, "BeginCapture", toggle, 0);
            Invoke(_editor, "CompleteCaptureWithPath", "<Keyboard>/f8");

            var action = Action("Editors", "ToggleTile");
            Assert.AreEqual("<Keyboard>/f8", EffectivePath(action, 0));
            action.Enable();
            Assert.AreEqual(1, action.controls.Count,
                "A path with no resolved control is a key that does nothing.");
        }

        /// <summary>
        /// Each control of a multi-control action is rebound SEPARATELY. The shipped code
        /// hardcoded slot 0, so Move showed eight keys and could only ever move the W of WASD —
        /// a rebinding panel that silently applies an eighth of what it says.
        /// </summary>
        [Test]
        public void EachSlotOfAMultiBindingAction_MovesOnItsOwn()
        {
            OpenEditor();
            var move = InputActionCatalog.Find("Gameplay/Move");
            var action = Action("Gameplay", "Move");

            var before = Enumerable.Range(0, 8).Select(i => EffectivePath(action, i)).ToArray();
            Assert.AreEqual("<Keyboard>/a", before[2], "Shipped WASD order: up, down, left, right.");

            Invoke(_editor, "BeginCapture", move, 2);
            Invoke(_editor, "CompleteCaptureWithPath", "<Keyboard>/j");

            var after = Enumerable.Range(0, 8).Select(i => EffectivePath(action, i)).ToArray();
            Assert.AreEqual("<Keyboard>/j", after[2]);
            for (int i = 0; i < 8; i++)
            {
                if (i == 2) continue;
                Assert.AreEqual(before[i], after[i], $"Slot {i} moved and should not have.");
            }
        }

        /// <summary>
        /// A binding can be cleared, and the action stays in the list so the key can be given
        /// back. "Unbound" is an override with an empty path — the InputSystem's own
        /// representation — not a removed binding, which could never be re-filled.
        /// </summary>
        [Test]
        public void ABindingCanBeCleared_AndTheActionStaysReachable()
        {
            OpenEditor();
            var iceball = InputActionCatalog.Find("Gameplay/SpellIceball");

            Invoke(_editor, "ClearBinding", iceball, 0);

            var action = Action("Gameplay", "SpellIceball");
            Assert.AreEqual("", EffectivePath(action, 0));
            action.Enable();
            Assert.AreEqual(0, action.controls.Count);
            Assert.IsTrue(InputContextPolicy.BelongsTo(iceball, InputContexts.War),
                "A cleared action must stay in its context's list, or the key cannot be given back.");
        }

        /// <summary>Any edit marks the profile dirty, which is what the GUARDAR dot reports and
        /// what stops a scene load silently reverting the work.</summary>
        [Test]
        public void AnyEdit_MarksTheProfileDirty()
        {
            OpenEditor();
            Assert.IsFalse(InputBindingStore.IsDirty);

            Invoke(_editor, "BeginCapture", InputActionCatalog.Find("Gameplay/Interact"), 0);
            Invoke(_editor, "CompleteCaptureWithPath", "<Keyboard>/j");

            Assert.IsTrue(InputBindingStore.IsDirty,
                "RuntimeInputBootstrap re-applies the saved profile on every scene load, so an " +
                "unsaved edit that is not flagged is an edit that vanishes at the next door.");
        }

        // ── The list ─────────────────────────────────────────────────────────

        /// <summary>
        /// Every gameplay action is listed in the War tab, including a silenced one — which is
        /// the only reason silencing is allowed to exist. A row that disappears when you switch
        /// it off is a switch with no way back.
        /// </summary>
        [Test]
        public void ASilencedAction_IsStillListed()
        {
            OpenEditor();
            var darkball = InputActionCatalog.Find("Gameplay/SpellDarkball");

            int before = VisibleRows().Count;
            Assert.AreEqual(InputAssignmentVerdict.Allowed,
                InputContextPolicy.SetContexts(darkball, InputContextMask.None));
            Invoke(_editor, "RebuildActionList");

            Assert.AreEqual(before, VisibleRows().Count,
                "Silencing an action must not remove its row.");
        }

        /// <summary>
        /// Damage actions really are ABSENT from Peace rather than greyed. That is a design
        /// decision that predates the silenced state and survives it: a disabled row is a
        /// control the author keeps trying, and Peace's promise is not "combat is off", it is
        /// "combat is not here".
        /// </summary>
        [Test]
        public void ThePeaceTab_OffersNothingThatReachesDamage()
        {
            var offered = InputActionCatalog.All
                .Where(d => d.ReachesDamage)
                .Where(d => InputContextPolicy.BelongsTo(d, InputContexts.Peace))
                .Select(d => d.Id)
                .ToList();

            Assert.IsEmpty(offered, "Peace must not even list a damage action: " + string.Join(" | ", offered));
        }

        /// <summary>The search box finds an action by the KEY it is on, which is half of what a
        /// rebinding surface is for and was the half that was missing.</summary>
        [Test]
        public void Search_FindsAnActionByItsKey()
        {
            OpenEditor();
            Invoke(_editor, "OnSearchChanged", "f5");
            var rows = VisibleRows();

            Assert.IsTrue(rows.Any(r => r.name.Contains("QuickSave")),
                "Searching for a key must find whatever is on it. Rows: " +
                string.Join(" | ", rows.Select(r => r.name)));
        }

        /// <summary>
        /// A keystroke FILTERS; it does not rebuild. Rebuilding the sixty-three rows measures
        /// 213 ms, which the shipped code paid on every character typed into the search box —
        /// the same shape as the Items editor's 3.5 s table, and the same fix.
        /// </summary>
        [Test]
        public void Searching_HidesRowsRatherThanRebuildingThem()
        {
            OpenEditor();
            var before = AllRowObjects();

            Invoke(_editor, "OnSearchChanged", "esquiva");

            CollectionAssert.AreEqual(before, AllRowObjects(),
                "The search box must not destroy and recreate the list; it must hide what does " +
                "not match.");
            Assert.AreEqual(1, VisibleRows().Count,
                "Exactly one action is called Esquiva.");

            Invoke(_editor, "OnSearchChanged", "");
            Assert.Greater(VisibleRows().Count, 1, "Clearing the box must bring the rows back.");
        }

        private List<GameObject> AllRowObjects()
        {
            var entries = (System.Collections.IEnumerable)_editor.GetType()
                .GetField("_entries", Priv).GetValue(_editor);

            var rows = new List<GameObject>();
            foreach (var entry in entries)
                rows.Add((GameObject)entry.GetType().GetField("Go").GetValue(entry));
            return rows;
        }

        // ── Context tabs ─────────────────────────────────────────────────────

        /// <summary>
        /// The strip offers the two postures, ONE shared-editor view, and a tab per editor that
        /// owns tools — not one per registered editor.
        ///
        /// <para>Twelve of the sixteen editors own no tools, so a tab each showed twelve
        /// identical boards: the shared verbs and nothing else. Eighteen tabs of which twelve
        /// are the same tab is a control that hides the four that differ.</para>
        /// </summary>
        [Test]
        public void TheContextStrip_CollapsesTheEditorsThatHaveNoToolsOfTheirOwn()
        {
            OpenEditor();
            var contexts = (List<string>)Invoke(_editor, "BuildContextList");

            CollectionAssert.Contains(contexts, InputContexts.War);
            CollectionAssert.Contains(contexts, InputContexts.Peace);
            CollectionAssert.Contains(contexts, InputContexts.EditorsAny);

            var owners = InputActionCatalog.All
                .Where(d => !string.IsNullOrEmpty(d.OwnerEditor))
                .Select(d => d.OwnerEditor)
                .Distinct()
                .ToList();

            foreach (var owner in owners)
                CollectionAssert.Contains(contexts, InputContexts.ForEditor(owner),
                    $"{owner} owns tools and must have a tab of its own.");

            Assert.AreEqual(3 + owners.Count, contexts.Count,
                "One tab per posture, one for the shared verbs, one per tool-owning editor: " +
                string.Join(" | ", contexts));
        }

        /// <summary>
        /// The shared-editor view shows the common verbs and NO editor's own tools, which is
        /// what makes it a useful answer rather than an arbitrary editor's board.
        /// </summary>
        [Test]
        public void TheSharedEditorView_ShowsTheCommonVerbsAndNoOwnedTool()
        {
            var shared = InputActionCatalog.All.Where(d => d.IsSharedEditorVerb).ToList();
            Assert.IsNotEmpty(shared);

            foreach (var d in shared)
                Assert.IsTrue(InputContextPolicy.IsLive(d, InputContexts.EditorsAny),
                    $"{d.Id} is a shared verb and must be live in the shared view.");

            var leaked = InputActionCatalog.All
                .Where(d => !string.IsNullOrEmpty(d.OwnerEditor))
                .Where(d => InputContextPolicy.IsLive(d, InputContexts.EditorsAny))
                .Select(d => d.Id)
                .ToList();

            Assert.IsEmpty(leaked, "An owned tool leaked into the shared view: " + string.Join(" | ", leaked));
        }

        // ── The board ────────────────────────────────────────────────────────

        /// <summary>
        /// The drawn board fits the panel that holds it, on both axes. It did not: the ISO
        /// keyboard plus the mouse measured 1066 px against a 1010 px panel, so the mouse was
        /// cut in half and reachable only by scrolling a board that looks like it is all there.
        /// </summary>
        [Test]
        public void TheDrawnBoard_FitsItsPanel()
        {
            OpenEditor();
            var host = Refs.BoardHost;

            Assert.LessOrEqual(host.sizeDelta.x, ControlsEditorUIBuilder.BOARD_W - 16f,
                $"The board needs {host.sizeDelta.x} px and the panel offers " +
                $"{ControlsEditorUIBuilder.BOARD_W}.");

            float boardRoom = ControlsEditorUIBuilder.BOARD_H
                            - TileEditorUIHelpers.PANEL_HDR_H
                            - ControlsEditorUIBuilder.ChromeHeight;
            Assert.LessOrEqual(host.sizeDelta.y, boardRoom,
                "The board must not need a vertical scroll at the default panel size.");
            Assert.Greater(host.sizeDelta.y, boardRoom - 120f,
                "…and the panel must not carry a slab of empty backdrop under it either: at " +
                "500 px tall it showed a quarter of its height as nothing, which reads as a " +
                "panel that failed to load something.");
        }

        /// <summary>
        /// The three surfaces whose children are placed BY HAND carry no layout group and no
        /// ContentSizeFitter.
        ///
        /// <para>This is the defect that made the whole window nonsense, and it hid behind a
        /// comment. <c>UIFactory.MakeScrollView</c> adds both components to the content it
        /// returns — right for a list of rows, fatal for a drawn keyboard — and three call
        /// sites here used it while their own comments claimed there was no layout group on
        /// them. A VerticalLayoutGroup deals absolutely-positioned children out in a single
        /// column and forces their width, so the keyboard, the mouse, the seven context tabs
        /// and the eleven legend swatches were all going to be stacked.</para>
        ///
        /// <para>Nothing caught it because <b>uGUI does not lay out in EditMode</b>: reading
        /// back <c>sizeDelta</c> returns what was written, never what a layout pass would have
        /// made of it, so every structural assertion passed against a board that renders
        /// scrambled. This test asks the one question that survives that — which components
        /// are attached — and a rendered frame is what found it.</para>
        /// </summary>
        [Test]
        public void HandPlacedSurfaces_CarryNoLayoutGroup()
        {
            OpenEditor();

            var surfaces = new (string name, RectTransform rt)[]
            {
                ("BoardHost", Refs.BoardHost),
                ("ContextStrip", Refs.ContextStrip),
                ("LegendStrip", Refs.LegendStrip),
            };

            var offenders = new List<string>();
            foreach (var (name, rt) in surfaces)
            {
                Assert.IsNotNull(rt, name + " is missing.");
                if (rt.GetComponent<LayoutGroup>() != null) offenders.Add(name + " has a LayoutGroup");
                if (rt.GetComponent<ContentSizeFitter>() != null) offenders.Add(name + " has a ContentSizeFitter");

                // Stretch anchors are the other half: with them, a sizeDelta of 1066 makes a
                // rect 1066 px WIDER than its parent — measured at 2072 — so a horizontal
                // scroll has nothing sane to scroll.
                if (rt.anchorMin != rt.anchorMax) offenders.Add(name + " is stretch-anchored");
            }

            Assert.IsEmpty(offenders, string.Join(" | ", offenders));
        }

        /// <summary>
        /// Both panels dock ON screen. <c>ApplyPanelDock</c> negates the offsets it is given,
        /// so the shipped <c>-56f</c> put each panel FIFTY-SIX PIXELS ABOVE THE TOP EDGE and
        /// took its header, its title and its resize grip with it. Every other editor in the
        /// project passes a positive gap.
        /// </summary>
        [Test]
        public void BothPanels_DockOnScreen()
        {
            OpenEditor();

            foreach (var panel in new[] { Refs.BoardPanel, Refs.ListPanel })
            {
                var rt = (RectTransform)panel.transform;
                Assert.LessOrEqual(rt.anchoredPosition.y, 0f,
                    $"{panel.name} is docked above the top edge; its header is off screen.");
                Assert.GreaterOrEqual(rt.anchorMax.y, 1f,
                    $"{panel.name} should hang from the top of the canvas.");
            }
        }

        /// <summary>
        /// Neither panel can be closed. They ARE the editor, and nothing here could bring one
        /// back — the only toolbar lives inside the board panel, so closing that one removes
        /// the keyboard, the mouse, the tabs, the legend and the status line with no route to
        /// any of them. A workspace on this machine was found in exactly that state.
        /// </summary>
        [Test]
        public void NeitherPanel_IsClosable()
        {
            OpenEditor();
            Assert.IsFalse(Refs.BoardDrag.ShowCloseButton);
            Assert.IsFalse(Refs.ListDrag.ShowCloseButton);
        }

        /// <summary>The search box says what it is. <c>UIInputField.AddCommit</c> builds no
        /// placeholder at all, so setting one on the returned field was writing to null and the
        /// row rendered as a bare dark bar.</summary>
        [Test]
        public void TheSearchBox_HasAVisiblePlaceholder()
        {
            OpenEditor();
            var placeholder = Refs.Search.placeholder as TextMeshProUGUI;
            Assert.IsNotNull(placeholder, "The search field has no placeholder component.");
            Assert.IsNotEmpty(placeholder.text);
        }

        /// <summary>Both panels can be resized. Every other panel-based editor can, and the
        /// board is the one surface in the project whose content is genuinely wider than a
        /// small window.</summary>
        [Test]
        public void BothPanels_CanBeResized()
        {
            OpenEditor();
            foreach (var panel in new[] { Refs.BoardPanel, Refs.ListPanel })
            {
                var handle = panel.GetComponentInChildren<PanelResizeHandle>(true);
                Assert.IsNotNull(handle, $"{panel.name} has no resize grip.");
                Assert.AreSame(panel.GetComponent<RectTransform>(), handle.Target);
            }
        }

        /// <summary>
        /// The legend names every tint the board can paint. A nine-colour code with no key is a
        /// code the author reverse-engineers from the keys they already know.
        /// </summary>
        [Test]
        public void TheLegend_NamesEveryTintTheBoardUses()
        {
            OpenEditor();
            var labels = Refs.LegendStrip.GetComponentsInChildren<TextMeshProUGUI>(true)
                             .Select(t => t.text)
                             .ToList();

            foreach (InputActionCategory c in System.Enum.GetValues(typeof(InputActionCategory)))
                CollectionAssert.Contains(labels, ControlsEditorUIBuilder.CategoryLabel(c),
                    $"The legend does not name the {c} tint.");
            CollectionAssert.Contains(labels, "libre");
        }

        // ── Clash rings ──────────────────────────────────────────────────────

        /// <summary>
        /// The War board rings NOTHING red. It used to ring fifteen keys — WASD, the arrows,
        /// all three mouse buttons, space and Escape — because a map-based scan cannot tell a
        /// gameplay verb sharing a key with its UI counterpart from a real double fire, and
        /// that pairing is how the project has always shipped. Fifteen permanent false
        /// positives beside a green "no conflicts" summary is an alarm nobody reads.
        /// </summary>
        [Test]
        public void TheWarBoard_HasNoBlockingClashes()
        {
            var blocking = InputConflictScanner.ClashesInContext(_svc.Asset, InputContexts.War)
                .Where(c => c.Severity == InputClashSeverity.Blocking)
                .Select(c => c.Describe())
                .ToList();

            Assert.IsEmpty(blocking, "War rings a key red: " + string.Join(" | ", blocking));
        }

        /// <summary>
        /// Escape is not a clash in an editor even though three actions answer it: closing the
        /// editor and opening the launcher is the documented one-press UX, declared as a
        /// coexist group so the board does not paint it red in all sixteen editor tabs forever.
        /// </summary>
        [Test]
        public void EscapeIsNotAClash_InsideAnEditor()
        {
            var live = InputConflictScanner.LiveByPath(_svc.Asset, InputContexts.ForEditor("Tile Editor"));
            Assert.IsTrue(live.TryGetValue("<Keyboard>/escape", out var onEscape));
            Assert.GreaterOrEqual(onEscape.Count, 2, "Escape really is answered more than once.");

            Assert.AreNotEqual(InputClashSeverity.Blocking, InputConflictScanner.Classify(onEscape),
                "The escape chain is declared, so it must not read as a double fire.");
        }

        /// <summary>
        /// A Ctrl shortcut and a bare-key tool on the same key are NOT a clash — because
        /// <c>EditorInput</c> refuses each in the other's modifier state. Both halves are the
        /// same fact (<see cref="InputActionDescriptor.RequiresCtrl"/>), so the scanner cannot
        /// claim a separation the readers do not make.
        /// </summary>
        [Test]
        public void ACtrlShortcutAndABareTool_ShareAKeyWithoutClashing()
        {
            var save = InputActionCatalog.Find("EditorShared/Save");
            var select = InputActionCatalog.Find("Editor.Tile/ToolSelect");
            Assert.IsTrue(save.RequiresCtrl);
            Assert.IsFalse(select.RequiresCtrl);

            var live = InputConflictScanner.LiveByPath(_svc.Asset, InputContexts.ForEditor("Tile Editor"));
            Assert.IsTrue(live.TryGetValue("<Keyboard>/s", out var onS));
            CollectionAssert.Contains(onS, save);
            CollectionAssert.Contains(onS, select);

            Assert.AreNotEqual(InputClashSeverity.Blocking, InputConflictScanner.Classify(onS),
                "Ctrl+S saves and bare S picks the select tool; they are two different presses.");
        }

        /// <summary>
        /// The Ctrl actions, named rather than derived. A test that read the flag off the
        /// catalog would pass whatever the catalog said, and this flag is what the scanner uses
        /// to decide a key is NOT double-booked — the direction in which being wrong is silent.
        ///
        /// <para>The last five are one editor's own CLIPBOARD tools rather than shared verbs,
        /// and they are the reason <see cref="InputActionCatalog.Tool"/> takes the flag at all:
        /// <c>EditorInput</c> used to refuse every tool outright while Ctrl was held, so the
        /// Tile editor's Ctrl+C / Ctrl+X / Ctrl+V could not fire at any time.</para>
        /// </summary>
        [TestCase("EditorShared/Undo")]
        [TestCase("EditorShared/Redo")]
        [TestCase("EditorShared/Save")]
        [TestCase("Editors/QuickSave")]
        [TestCase("Editors/QuickLoad")]
        [TestCase("Editor.Tile/Copy")]
        [TestCase("Editor.Tile/Cut")]
        [TestCase("Editor.Tile/Paste")]
        [TestCase("Editor.Buildings/Copy")]
        [TestCase("Editor.Buildings/Paste")]
        public void TheCtrlVerbs_AreDeclaredAsSuch(string id)
        {
            var d = InputActionCatalog.Find(id);
            Assert.IsNotNull(d, id + " is gone from the catalog.");
            Assert.IsTrue(d.RequiresCtrl, $"{id} only fires with Ctrl held; the catalog must say so.");
        }

        /// <summary>Every coexist group has at least two members. A group of one excuses
        /// nothing and is a typo that silently disables a real check.</summary>
        [Test]
        public void EveryCoexistGroup_HasMoreThanOneMember()
        {
            var groups = InputActionCatalog.All
                .Where(d => !string.IsNullOrEmpty(d.CoexistGroup))
                .GroupBy(d => d.CoexistGroup)
                .Where(g => g.Count() < 2)
                .Select(g => g.Key)
                .ToList();

            Assert.IsEmpty(groups, "Coexist group with one member: " + string.Join(" | ", groups));
        }

        // ── Posture chips ────────────────────────────────────────────────────

        /// <summary>
        /// A chip is drawn for every posture, and a refused one is drawn LOCKED rather than as
        /// a toggle that answers with an error.
        ///
        /// <para>The shipped panel drew eight interactive chips on gameplay rows, of which six
        /// reached no reader at all — it reported a change it could not make. It also had no
        /// chip whatsoever on the 24 spell slots, so the promise that a player can silence one
        /// spell had no control anywhere. Both directions are wrong in the same way: the panel
        /// and the behaviour disagreed, and only the panel was visible.</para>
        /// </summary>
        [Test]
        public void PostureChips_AreDrawnLockedRatherThanRefusing()
        {
            OpenEditor();

            // Walking may be moved to any key and may not be taken away, so both chips are
            // shown and neither is interactive.
            var move = ChipsOn("Gameplay/Move");
            CollectionAssert.AreEqual(new[] { "G", "P" }, move.Select(c => c.label).ToList());
            Assert.IsFalse(move.Any(c => c.interactable),
                "Move is context-locked: switching it off is a soft lock.");

            // A damage action may leave War and may never enter Peace.
            var spell = ChipsOn("Gameplay/SpellDarkball");
            CollectionAssert.AreEqual(new[] { "G", "P" }, spell.Select(c => c.label).ToList());
            Assert.IsTrue(spell[0].interactable, "A spell slot must be silenceable in War.");
            Assert.IsFalse(spell[1].interactable, "Peace is a safe posture, not a second layout.");

            // An ordinary verb is free in both.
            var interact = ChipsOn("Gameplay/Interact");
            Assert.IsTrue(interact.All(c => c.interactable));
        }

        private List<(string label, bool interactable)> ChipsOn(string actionId)
        {
            var entries = (System.Collections.IEnumerable)_editor.GetType()
                .GetField("_entries", Priv).GetValue(_editor);

            foreach (var entry in entries)
            {
                var type = entry.GetType();
                if ((string)type.GetField("ActionId").GetValue(entry) != actionId) continue;
                if ((bool)type.GetField("IsSlotRow").GetValue(entry)) continue;

                var go = (GameObject)type.GetField("Go").GetValue(entry);
                return go.GetComponentsInChildren<Button>(true)
                    .Select(b => (b.GetComponentInChildren<TextMeshProUGUI>(true)?.text ?? "", b.interactable))
                    .Where(c => c.Item1 == "G" || c.Item1 == "P")
                    .ToList();
            }
            Assert.Fail(actionId + " is not listed.");
            return null;
        }

        // ── Undo ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Ctrl+Z takes back a rebind and Ctrl+Y puts it front.
        ///
        /// <para>`EditorShared/Undo` is declared live in EVERY editor context — that is what
        /// makes it shared — so this panel ignoring it was a control the layer promised and the
        /// editor silently dropped. It matters more here than in most editors: the mistake this
        /// panel invites is pressing the wrong key during a capture, and without an undo the
        /// only way back is remembering what the key used to be.</para>
        /// </summary>
        [Test]
        public void Undo_TakesBackARebind_AndRedoPutsItFront()
        {
            OpenEditor();
            var interact = InputActionCatalog.Find("Gameplay/Interact");

            Invoke(_editor, "BeginCapture", interact, 0);
            Invoke(_editor, "CompleteCaptureWithPath", "<Keyboard>/j");
            Invoke(_editor, "BeginCapture", interact, 0);
            Invoke(_editor, "CompleteCaptureWithPath", "<Keyboard>/k");
            Assert.AreEqual("<Keyboard>/k", EffectivePath(Action("Gameplay", "Interact"), 0));

            Invoke(_editor, "Undo");
            Assert.AreEqual("<Keyboard>/j", EffectivePath(Action("Gameplay", "Interact"), 0));

            Invoke(_editor, "Redo");
            Assert.AreEqual("<Keyboard>/k", EffectivePath(Action("Gameplay", "Interact"), 0));
        }

        /// <summary>
        /// Undoing back to the start leaves NO override, not an override that happens to name
        /// the shipped key.
        ///
        /// <para>The two look identical on screen and are not the same state: a pinned override
        /// survives a later "reset everything else" and would be written into the saved profile
        /// as a decision the player never made. It is the reason the undo record stores
        /// <c>overridePath</c> rather than <c>effectivePath</c>.</para>
        /// </summary>
        [Test]
        public void UndoingToTheStart_LeavesNoOverrideAtAll()
        {
            OpenEditor();
            var interact = InputActionCatalog.Find("Gameplay/Interact");

            Invoke(_editor, "BeginCapture", interact, 0);
            Invoke(_editor, "CompleteCaptureWithPath", "<Keyboard>/j");
            Invoke(_editor, "Undo");

            var binding = Action("Gameplay", "Interact").bindings[0];
            Assert.IsNull(binding.overridePath,
                "Undo must remove the override, not replace it with one naming the shipped key.");
            Assert.AreEqual("<Keyboard>/e", binding.effectivePath);
        }

        [Test]
        public void Undo_TakesBackAContextMaskChange()
        {
            OpenEditor();
            var darkball = InputActionCatalog.Find("Gameplay/SpellDarkball");

            Invoke(_editor, "ToggleContextBit", darkball, InputContextMask.War);
            Assert.AreEqual(InputContextMask.None, InputContextPolicy.ContextsOf(darkball));

            Invoke(_editor, "Undo");
            Assert.AreEqual(InputContextMask.War, InputContextPolicy.ContextsOf(darkball));
        }

        /// <summary>
        /// Reset clears the history. It drops the file from disk as well as the live state, so
        /// an undo could only ever put back half of what it took — and a half-undo, silently,
        /// is worse than none.
        /// </summary>
        [Test]
        public void Reset_ClearsTheUndoHistory()
        {
            OpenEditor();
            Invoke(_editor, "BeginCapture", InputActionCatalog.Find("Gameplay/Interact"), 0);
            Invoke(_editor, "CompleteCaptureWithPath", "<Keyboard>/j");
            Assert.AreEqual(1, (int)_editor.GetType().GetProperty("UndoDepth", Priv).GetValue(_editor));

            Invoke(_editor, "ResetToDefaults");
            Assert.AreEqual(0, (int)_editor.GetType().GetProperty("UndoDepth", Priv).GetValue(_editor));
            Assert.AreEqual(0, (int)_editor.GetType().GetProperty("RedoDepth", Priv).GetValue(_editor));
        }

        // ── Reset ────────────────────────────────────────────────────────────

        /// <summary>
        /// Reset asks first. It drops every rebind, every context mask and the file on disk,
        /// and it shipped as a single click beside GUARDAR — the arrangement where a mis-click
        /// costs an afternoon and there is nothing to undo with.
        /// </summary>
        [Test]
        public void Reset_AsksBeforeItDestroysAnything()
        {
            OpenEditor();
            Invoke(_editor, "BeginCapture", InputActionCatalog.Find("Gameplay/Interact"), 0);
            Invoke(_editor, "CompleteCaptureWithPath", "<Keyboard>/j");

            Invoke(_editor, "AskReset");
            Assert.IsTrue(_editor.IsConfirmOpen, "Reset must open a confirmation.");
            Assert.AreEqual("<Keyboard>/j", EffectivePath(Action("Gameplay", "Interact"), 0),
                "Nothing may be destroyed before the author confirms.");

            Invoke(_editor, "CloseConfirm");
            Assert.AreEqual("<Keyboard>/j", EffectivePath(Action("Gameplay", "Interact"), 0));

            Invoke(_editor, "ResetToDefaults");
            Assert.AreEqual("<Keyboard>/e", EffectivePath(Action("Gameplay", "Interact"), 0));
        }
    }
}
