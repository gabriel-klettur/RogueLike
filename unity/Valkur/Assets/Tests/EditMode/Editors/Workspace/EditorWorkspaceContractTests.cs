using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Valkur.Core;
using Valkur.Core.Editors;
using Valkur.Infrastructure.Persistence.EditorWorkspaces;
using Valkur.Gameplay.Editors.Workspace;
using Valkur.UIKit;

namespace Valkur.Tests.EditMode.Editors.Workspace
{
    /// <summary>
    /// The contract that stops the editor workspace layer rotting.
    ///
    /// This project has exactly one kind of convention that has held over time — the sort a
    /// test can fail (<c>FSMBuiltInTransitionRegistryTests</c>, <c>AssetConventionsTests</c>,
    /// <c>DomainReloadStaticResetTests</c>). Documented conventions that nothing enforces
    /// drift: the runtime editors' canonical UX pattern was written down and still ended up
    /// with three editors carrying no chrome at all.
    ///
    /// So these tests pin the promises, not the implementation:
    ///   • a round trip returns the layout,
    ///   • a workspace from a bigger display cannot strand a panel off-screen,
    ///   • an unknown schema version is discarded whole,
    ///   • an unresolved selection leaves the editor neutral AND says nothing to the console,
    ///   • panel identity is namespaced by editor, so two editors may name a panel the same.
    /// </summary>
    [TestFixture]
    public sealed class EditorWorkspaceContractTests
    {
        private string _tempRoot;
        private JsonEditorWorkspaceStore _store;

        [SetUp]
        public void SetUp()
        {
            // Never the real folder: an EditMode test writing into the player's own
            // persistentDataPath is what produced the twin-save incident.
            _tempRoot = Path.Combine(Path.GetTempPath(),
                "ValkurWorkspaceTests_" + System.Guid.NewGuid().ToString("N"));
            _store = new JsonEditorWorkspaceStore(_tempRoot);
        }

        [TearDown]
        public void TearDown()
        {
            DraggablePanel.StateSink = null;
            ServiceLocator.Clear();
            try { if (Directory.Exists(_tempRoot)) Directory.Delete(_tempRoot, true); }
            catch { /* a temp folder that will not delete is not a test failure */ }
        }

        // ── Round trip ──────────────────────────────────────────────────────────

        [Test]
        public void RoundTrip_PreservesPanelGeometry()
        {
            var ws = new EditorWorkspace { editorName = "Buildings" };
            ws.UpsertPanel(new EditorPanelState
            {
                panelId          = "Buildings/PropertiesPanel",
                anchoredPosition = new Vector2(-312.5f, 118.25f),
                size             = new Vector2(300f, 460f),
                minimized        = true,
                open             = false,
                siblingIndex     = 3,
            });

            _store.Save(ws);
            var loaded = _store.Load("Buildings");

            Assert.IsNotNull(loaded, "A saved workspace must load back.");
            var p = loaded.FindPanel("Buildings/PropertiesPanel");
            Assert.IsNotNull(p, "The panel record must survive the round trip.");

            // One canvas pixel of tolerance: the acceptance criterion is that the author
            // cannot SEE the panel move, not that the float is bit-identical.
            Assert.AreEqual(-312.5f, p.anchoredPosition.x, 1f);
            Assert.AreEqual(118.25f, p.anchoredPosition.y, 1f);
            Assert.AreEqual(300f, p.size.x, 1f);
            Assert.AreEqual(460f, p.size.y, 1f);
            Assert.IsTrue(p.minimized);
            Assert.IsFalse(p.open, "A panel left closed must come back closed.");
            Assert.AreEqual(3, p.siblingIndex);
        }

        [Test]
        public void RoundTrip_PreservesSessionBagAndSelection()
        {
            var ws = new EditorWorkspace { editorName = "Particles" };
            ws.SetString("mode", "Spawn");
            ws.SetInt("brushSize", 4);
            ws.SetFloat("zoom", 7.25f);
            ws.SetBool("showGrid", true);
            ws.selection.Set("emitter", "abc123", "slot_2", "Bosque");

            _store.Save(ws);
            var loaded = _store.Load("Particles");

            Assert.AreEqual("Spawn", loaded.GetString("mode", "?"));
            Assert.AreEqual(4, loaded.GetInt("brushSize", -1));
            Assert.AreEqual(7.25f, loaded.GetFloat("zoom", -1f), 0.0001f);
            Assert.IsTrue(loaded.GetBool("showGrid", false));
            Assert.AreEqual("emitter", loaded.selection.type);
            Assert.AreEqual("abc123", loaded.selection.id);
        }

        [Test]
        public void SessionBag_FallsBackWhenKeyIsAbsentOrUnparseable()
        {
            var ws = new EditorWorkspace { editorName = "Items" };
            ws.SetString("brushSize", "not-a-number");

            // Restore must tolerate every value being absent or stale — a workspace written
            // by an older build, or naming a category since deleted, is the normal case.
            Assert.AreEqual(9, ws.GetInt("brushSize", 9), "Unparseable value must fall back.");
            Assert.AreEqual(2, ws.GetInt("neverWritten", 2), "Absent key must fall back.");
            Assert.AreEqual("dflt", ws.GetString("neverWritten", "dflt"));
        }

        // ── Schema ──────────────────────────────────────────────────────────────

        [Test]
        public void UnknownSchemaVersion_IsDiscardedWhole()
        {
            var ws = new EditorWorkspace { editorName = "Tile" };
            ws.SetString("mode", "Brush");
            _store.Save(ws);

            // Rewrite the file claiming a version this build does not know.
            string path = Path.Combine(_tempRoot, "tile.json");
            Assert.IsTrue(File.Exists(path), "Save must produce a sanitized file name.");
            File.WriteAllText(path, File.ReadAllText(path).Replace(
                "\"schemaVersion\": " + EditorWorkspace.CURRENT_SCHEMA_VERSION,
                "\"schemaVersion\": " + (EditorWorkspace.CURRENT_SCHEMA_VERSION + 99)));

            Assert.IsNull(_store.Load("Tile"),
                "Half a remembered layout is worse than none — an unknown version must be " +
                "discarded whole, never read partially.");
        }

        [Test]
        public void MalformedDocument_LoadsAsNullWithoutThrowing()
        {
            Directory.CreateDirectory(_tempRoot);
            File.WriteAllText(Path.Combine(_tempRoot, "fsm.json"), "{ this is not json");

            LogAssert.ignoreFailingMessages = true;   // the store warns once, by design
            Assert.DoesNotThrow(() => _store.Load("FSM"));
            Assert.IsNull(_store.Load("FSM"));
        }

        [Test]
        public void EditorNamesWithPunctuation_ProduceOneStableFileEach()
        {
            // "Time & Weather" and "Dungeon NodeGraph" are real registered editor names.
            Assert.AreEqual("time___weather", JsonEditorWorkspaceStore.Sanitize("Time & Weather"));
            Assert.AreEqual("dungeon_nodegraph", JsonEditorWorkspaceStore.Sanitize("Dungeon NodeGraph"));

            // Case cannot be the only difference, or two editors address two files on a
            // case-sensitive filesystem and one file on Windows.
            Assert.AreEqual(JsonEditorWorkspaceStore.Sanitize("items"),
                            JsonEditorWorkspaceStore.Sanitize("Items"));
        }

        // ── Layout rescue ───────────────────────────────────────────────────────

        [Test]
        public void PanelCapturedOnLargerDisplay_IsNotStrandedOffScreen()
        {
            var captured = new Vector2(2560f, 1440f);
            var live     = new Vector2(1366f, 768f);

            // Docked bottom-right on the big display: unreachable on the small one.
            var state = new EditorPanelState
            {
                panelId          = "Map/PropertiesPanel",
                anchoredPosition = new Vector2(1100f, -600f),
                size             = new Vector2(300f, 500f),
                open             = true,
            };

            var rescued = EditorWorkspaceService.RescueOffScreen(state, captured, live);

            Assert.IsFalse(rescued.HasGeometry,
                "A panel that would land outside the live canvas must give up its geometry " +
                "and take the dock its builder gave it — clamping alone cannot save it.");
            Assert.IsTrue(rescued.open, "Rescue must not silently reopen or close the panel.");
            Assert.AreEqual("Map/PropertiesPanel", rescued.panelId);
        }

        [Test]
        public void PanelTallerThanTheLiveCanvas_GivesUpItsGeometry()
        {
            var state = new EditorPanelState
            {
                panelId          = "Tile/TilesPanel",
                anchoredPosition = Vector2.zero,     // centred, so not "outside"
                size             = new Vector2(300f, 900f),
                open             = true,
            };

            var rescued = EditorWorkspaceService.RescueOffScreen(
                state, new Vector2(2560f, 1440f), new Vector2(1366f, 768f));

            Assert.IsFalse(rescued.HasGeometry,
                "A panel taller than the display is unusable even when its position is legal.");
        }

        [Test]
        public void PanelThatStillFits_KeepsItsExactGeometry()
        {
            var state = new EditorPanelState
            {
                panelId          = "Items/TablePanel",
                anchoredPosition = new Vector2(-400f, 120f),
                size             = new Vector2(300f, 460f),
                open             = true,
            };

            var kept = EditorWorkspaceService.RescueOffScreen(
                state, new Vector2(1366f, 768f), new Vector2(1366f, 768f));

            Assert.AreSame(state, kept, "A legal layout must be passed through untouched.");
        }

        // ── Selection policy ────────────────────────────────────────────────────

        [Test]
        public void Selection_FromAnotherMapSlot_DoesNotApply()
        {
            var sel = new EditorSelectionRecord();
            sel.Set("building", "b-1", "slot_2", "Bosque");

            Assert.IsFalse(sel.AppliesTo("slot_5", "Bosque"),
                "A selection is discarded up front when the slot differs — cheaper than " +
                "resolving, and it dodges an id reused across slots.");
            Assert.IsFalse(sel.AppliesTo("slot_2", "Desierto"), "Zone must gate it too.");
            Assert.IsTrue(sel.AppliesTo("slot_2", "bosque"),
                "Zone names compare OrdinalIgnoreCase everywhere else in the project.");
        }

        [Test]
        public void Selection_WithoutContext_AppliesAnywhere()
        {
            var sel = new EditorSelectionRecord();
            sel.Set("spell", "fireball");

            Assert.IsTrue(sel.AppliesTo("slot_9", "Anything"),
                "An editor whose selection is not slot-scoped (Spells, Items) opts out by " +
                "storing no context — not by a special case in the service.");
        }

        [Test]
        public void UnresolvedSelection_WritesNothingToTheConsole()
        {
            var ws = new EditorWorkspace { editorName = "Entities" };
            ws.selection.Set("monster", "deleted-placement", "slot_1", "Bosque");
            _store.Save(ws);

            var loaded = _store.Load("Entities");
            bool applies = loaded.selection.AppliesTo("slot_4", "Otra");

            Assert.IsFalse(applies);

            // The cardinal rule: an editor opening after a slot change is the EXPECTED
            // case, not an anomaly. A warning here would train the reader to scroll past
            // the console, which this project has already paid for four times over.
            LogAssert.NoUnexpectedReceived();
        }

        // ── Panel identity ──────────────────────────────────────────────────────

        [Test]
        public void PanelId_IsNamespacedByOwningEditor()
        {
            var buildings = MakePanel("PropertiesPanel", owner: "Buildings");
            var map       = MakePanel("PropertiesPanel", owner: "Map");

            Assert.AreEqual("Buildings/PropertiesPanel", buildings.WorkspacePanelId);
            Assert.AreEqual("Map/PropertiesPanel",       map.WorkspacePanelId);
            Assert.AreNotEqual(buildings.WorkspacePanelId, map.WorkspacePanelId,
                "Buildings (F10) and Map (F11) really do both build a 'PropertiesPanel'. " +
                "Before the namespace they shared one remembered-closed bit — closing " +
                "Properties in one closed it in the other.");

            Object.DestroyImmediate(buildings.gameObject);
            Object.DestroyImmediate(map.gameObject);
        }

        [Test]
        public void PanelId_WithoutOwner_IsTheBareKey()
        {
            var panel = MakePanel("LonelyPanel", owner: null);

            Assert.AreEqual("LonelyPanel", panel.WorkspacePanelId,
                "A panel outside any managed editor must key exactly as it always has, so " +
                "adopting the layer changes nothing for HUD widgets and modals.");

            Object.DestroyImmediate(panel.gameObject);
        }

        [Test]
        public void StateSink_DefaultsToPlayerPrefsAndIsRestorableToIt()
        {
            DraggablePanel.StateSink = null;
            Assert.IsInstanceOf<PlayerPrefsPanelStateSink>(DraggablePanel.StateSink,
                "The default backend must stay the historical one.");

            var fake = new RecordingSink();
            DraggablePanel.StateSink = fake;
            Assert.AreSame(fake, DraggablePanel.StateSink);

            DraggablePanel.StateSink = null;
            Assert.IsInstanceOf<PlayerPrefsPanelStateSink>(DraggablePanel.StateSink,
                "Assigning null must restore the default — that is what teardown relies on.");
        }

        [Test]
        public void ClosedBit_GoesThroughTheInstalledSink()
        {
            var sink = new RecordingSink();
            DraggablePanel.StateSink = sink;

            var panel = MakePanel("SinkPanel", owner: "Spells");
            sink.Closed["Spells/SinkPanel"] = true;

            Assert.IsTrue(panel.WasClosedLastSession,
                "Visibility must have exactly one owner. Reading PlayerPrefs directly here " +
                "while the workspace held the same bit is the two-owners bug this layer " +
                "exists to remove.");

            Object.DestroyImmediate(panel.gameObject);
        }

        // ── Helpers ─────────────────────────────────────────────────────────────

        private static DraggablePanel MakePanel(string name, string owner)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var panel = go.AddComponent<DraggablePanel>();
            if (!string.IsNullOrEmpty(owner)) panel.Owner = owner;
            return panel;
        }

        private sealed class RecordingSink : IPanelStateSink
        {
            public readonly System.Collections.Generic.Dictionary<string, bool> Closed =
                new System.Collections.Generic.Dictionary<string, bool>();

            public bool IsClosed(string key) => Closed.TryGetValue(key, out var v) && v;
            public void SetClosed(string key, bool closed) => Closed[key] = closed;
            public void Forget(string key) => Closed.Remove(key);
        }
    }
}
