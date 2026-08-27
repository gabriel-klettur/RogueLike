using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using TMPro;
using Valkur.Core;
using Valkur.Core.Input;
using Valkur.Data;
using Valkur.Gameplay.Editors;
using Valkur.UIKit;

namespace Valkur.Gameplay.Entities
{
    /// <summary>
    /// Runtime in-game Entities Editor (F5).
    ///
    /// UI/UX layer mirrors the professional menu-bar + draggable-panel
    /// architecture used by the Buildings (F10), FSM (F12) and Tile (F8)
    /// editors. The Python source of truth is <c>roguelike_editors/entities</c>
    /// (panels: tool_bar, picker, add_remove, properties, tutorial).
    ///
    /// The full authoring loop is wired: property rows edit and save live definitions
    /// (<c>CommitDefinitionEdit</c> / <c>SaveEditedDefinitions</c>), Create / Duplicate / Rename
    /// manage the catalog itself (<c>EntitiesRuntimeEditor.CatalogAuthoring.cs</c>), and every
    /// placement — click, drag, or reloaded from a previous session — persists to
    /// <c>StreamingAssets/Entities/entities_instances.json</c>
    /// (<c>EntitiesRuntimeEditor.Persistence.cs</c>), so a monster placed here survives a Stop.
    /// </summary>
    public partial class EntitiesRuntimeEditor : SingletonMonoBehaviour<EntitiesRuntimeEditor>,
                                                 GameEditorManager.IGameEditor,
                                                 IAllowsPlayerMovement
    {
        [SerializeField, Tooltip("Monster catalog asset (drives Hostiles / Neutrals / Specials picker)")]
        private MonsterCatalog _monsterCatalog;

        /// <summary>
        /// Injects the catalog from the bootstrap. Exists because the ONLY assignment used
        /// to be a <c>SerializedObject</c>/<c>FindProperty</c> write inside
        /// <c>#if UNITY_EDITOR</c>, while the editor itself was created unconditionally — so
        /// in a built player the F5 picker rendered zero hostiles and every spawn reported
        /// "Spawn failed: monster catalog not assigned." A plain setter works in both.
        /// </summary>
        internal void SetMonsterCatalog(MonsterCatalog catalog)
        {
            if (catalog == null) return;
            _monsterCatalog = catalog;
            if (_active) RefreshPicker();
        }

        /// <summary>
        /// Last-resort lookup for a catalog nobody injected — the same shape
        /// <c>ResolveItemCatalogFallback</c> gives the Items editor. Called on the way into
        /// the picker rather than at Awake, because the bootstrap registers the catalog
        /// during its own Start and this component's Awake can run first.
        /// </summary>
        private void ResolveMonsterCatalogFallback()
        {
            if (_monsterCatalog != null) return;
            if (ServiceLocator.TryGet<MonsterCatalog>(out var catalog) && catalog != null)
                _monsterCatalog = catalog;
        }

        [SerializeField, Tooltip("Spell catalog asset — validates MonsterDefinition.autoCastList " +
                 "entries against real spell keys and powers the Auto-Cast dropdown, so a mistyped " +
                 "key is refused at author time instead of shipping a monster that silently never " +
                 "casts (EntitySetup.ConfigureMonsterAutoCast skips unresolved keys with a warning).")]
        private SpellCatalog _spellCatalog;

        /// <summary>
        /// Injects the spell catalog, mirroring <see cref="SetMonsterCatalog"/>. No bootstrap
        /// caller exists yet — <c>GameplaySceneSetup</c> wires <c>SpellCatalog</c> into
        /// <c>EntitySetup</c>/<c>SpellsRuntimeEditor</c> only — so
        /// <see cref="ResolveSpellCatalogFallback"/> is what actually populates the field today.
        /// Kept as a real entry point so wiring it from the bootstrap later is a one-line change,
        /// not a new seam, and so tests can inject a fixture catalog directly.
        /// </summary>
        internal void SetSpellCatalog(SpellCatalog catalog)
        {
            if (catalog == null) return;
            _spellCatalog = catalog;
        }

        /// <summary>
        /// Editor-only last-resort lookup, mirroring <c>ResolveItemCatalogFallback</c>'s
        /// Resources-then-AssetDatabase order — except <c>SpellCatalog</c> has no Resources copy
        /// (unlike <c>ItemCatalog</c>), so only the AssetDatabase half applies. In a built player
        /// with no bootstrap wiring the Auto-Cast section just reports "spell catalog not
        /// available" instead of throwing.
        /// </summary>
        private void ResolveSpellCatalogFallback()
        {
            if (_spellCatalog != null) return;
#if UNITY_EDITOR
            _spellCatalog = UnityEditor.AssetDatabase.LoadAssetAtPath<SpellCatalog>(
                "Assets/_Project/Data/Catalogs/SpellCatalog.asset");
#endif
        }

        // ── State ────────────────────────────────────────────────────────────────

        private bool _active;
        private InputAction _toggleAction;
        private bool _ownsToggleAction;

        private enum EditorMode { Select, Spawn, Delete, AddOnSystem }
        private EditorMode _mode = EditorMode.Select;
        private string     _selectedKey;
#pragma warning disable CS0414 // assigned-but-not-yet-read; reserved for Phase 2 player/monster property dispatch
        private bool       _selectedIsPlayer;
#pragma warning restore CS0414

        private enum EntityCategory { Hostiles, Neutrals, Specials, Players }
        private EntityCategory _category = EntityCategory.Hostiles;

        private string _searchFilter = "";
        private readonly UndoStack _undo = new UndoStack(64);

        /// <summary>
        /// Live text of the Add/Remove panel's "New / Rename Key" field. One input feeds two
        /// verbs: <see cref="OnConfirmAddOnSystem"/> reads it as the key for a brand-new
        /// definition, <see cref="RenameSelectedDefinition"/> reads it as the new key/name for
        /// whichever definition is selected. See <c>EntitiesRuntimeEditor.CatalogAuthoring.cs</c>.
        /// </summary>
        private string _pendingKeyInput = "";

        /// <summary>
        /// Set the moment any property row commits. Only used to keep the Save button
        /// honest — the edit itself already reached the in-memory definition and every
        /// live monster; Save is what flushes it to the `.asset` on disk.
        /// </summary>
        private bool _pendingAssetWrites;

        /// <summary>
        /// Flushes definitions edited through the properties panel to disk.
        ///
        /// Editor-only by nature: <c>AssetDatabase</c> does not exist in a player, and
        /// a built game has no `.asset` files to rewrite. In a build the button says so
        /// rather than silently doing nothing.
        /// </summary>
        private void SaveEditedDefinitions()
        {
#if UNITY_EDITOR
            if (!_pendingAssetWrites)
            {
                SetStatus("Nothing to save — no property has been edited.");
                return;
            }
            UnityEditor.AssetDatabase.SaveAssets();
            _pendingAssetWrites = false;
            SetStatus("Saved monster definitions to disk.");
#else
            SetStatus("Save is Editor-only — a built game has no .asset files to write.");
#endif
        }

        // Middle-mouse camera pan — shared controller used by every runtime editor.
        private readonly EditorCameraPanController _cameraPan = new EditorCameraPanController();

        // ── UI ───────────────────────────────────────────────────────────────────

        private Canvas        _canvas;
        private GameObject    _root;
        private GameObject    _tutorial;
        private EntitiesEditorUIBuilder.UIRefs _ui;

        // Open-dropdown tracking (mirrors BuildingsRuntimeEditor.UI pattern).
        private readonly HashSet<string> _openDropdowns = new HashSet<string>();

        // ── IGameEditor ─────────────────────────────────────────────────────────

        public string EditorName => "Entities Editor";
        public bool   IsActive   => _active;

        // ── Lifecycle ───────────────────────────────────────────────────────────

        protected override void OnSingletonAwake()
        {
            // F5 binding routed through InputService.Editors when bootstrapped (play mode);
            // EditMode tests fall back to a fresh ad-hoc InputAction so reflection-based
            // binding-path checks in FKeyBindingParityTests still see <Keyboard>/f5.
            _toggleAction = EditorHotkeyBindings.Resolve(
                EditorHotkeyBindings.Hotkey.ToggleEntities, out _ownsToggleAction);
        }

        private void Start()
        {
            BuildUI();
            _root.SetActive(false);
            if (GameEditorManager.HasInstance) GameEditorManager.Instance.Register(this);

            // Independent of whether F5 is ever opened — this singleton already exists
            // regardless, so it is the natural single owner of both halves of the F5
            // placement round trip. See EntitiesRuntimeEditor.Persistence.cs.
            //
            // Deferred to the first Update() rather than called here: Unity guarantees every
            // object's Awake() runs before any object's Start(), but NOT that Start() itself
            // runs in a useful order across objects — SpawnerInstanceLoader sidesteps the same
            // hazard by having GameplaySceneSetup call LoadInstances() explicitly instead of
            // relying on its own Start(). This editor cannot get that treatment (GameplaySceneSetup
            // is Bootstrap, outside this change's scope), so it waits for every object's Start()
            // in the scene to have already run — including whatever populates ZoneManager's zone
            // list — before resolving a single placement. Calling LoadPlacedEntities() here
            // instead would resolve every record's zone against zero registered zones on the
            // very first frame, silently reclassifying every placement as unresolved.
            //
            // Gated on Play Mode: EditMode tests invoke Start() directly via reflection without
            // entering Play Mode (EntitiesRuntimeEditorTests.CreateEditorWithUI), and an
            // unguarded call would touch the real StreamingAssets/Entities file through the
            // default repository — creating an empty folder as a side effect purely from running
            // the test suite. Same class of EditMode pollution
            // SpawnerEditorManager.SaveInstancesToJson guards against. LoadPlacedEntities itself
            // stays guard-free so a test can still call it directly with an injected repository
            // regardless of Play Mode.
            if (Application.isPlaying) _pendingEntityLoad = true;
        }

        /// <summary>Set in <see cref="Start"/> when running in Play Mode; consumed on the very
        /// first <see cref="Update"/> tick to defer <c>LoadPlacedEntities</c> past every other
        /// object's own <c>Start()</c> for the frame.</summary>
        private bool _pendingEntityLoad;

        protected override void OnDestroy()
        {
            // Stopping Play Mode without closing F5 first still has to persist whatever is
            // pending — this is what makes "place a monster, hit Stop" keep it.
            FlushEntityPlacementAutosave();
            if (_ownsToggleAction) _toggleAction?.Dispose();
            if (GameEditorManager.HasInstance) GameEditorManager.Instance.Unregister(this);
            base.OnDestroy();
        }

        private void Update()
        {
            // Consumed exactly once, on the first Update() tick after entering Play Mode — see
            // the comment on Start() for why this cannot simply run there.
            if (_pendingEntityLoad)
            {
                _pendingEntityLoad = false;
                LoadPlacedEntities();
            }

            // Bare F5 only. ToggleEntities and QuickSave are both bound to
            // <Keyboard>/f5 with no modifier or interaction on either binding;
            // SaveLoadInputHandler gates its half on Ctrl, so without the same guard
            // here a quick-save also toggled this editor open or closed — which runs
            // Deactivate() and drops the current selection. Matches SpawnerEditorManager.
            if (EditorHotkeyBindings.WasPerformedThisFrame(EditorHotkeyBindings.Hotkey.ToggleEntities) &&
                !EditorHotkeyBindings.IsPressed(EditorHotkeyBindings.Hotkey.CtrlModifier))
            {
                if (GameEditorManager.HasInstance) GameEditorManager.Instance.ToggleExclusive(this);
                else                               ToggleActive();
            }

            // Ticked unconditionally — a placement must survive the author closing the
            // editor and walking away, not only a Save click while the panel is visible.
            TickEntityPlacementAutosave();

            if (!_active) return;

            // Middle-mouse pan runs unconditionally so dragging the camera works
            // even while a picker drag or entity drag is in progress.
            _cameraPan.Tick();

            UpdatePickerDrag();
            // Suppress click-spawn while a drag is active so releasing over the
            // map only triggers the drag-spawn path (HandleMapInteraction would
            // otherwise fire Spawn/Delete on the same release frame).
            if (_pickerDragging) return;

            // Selection (LMB) and move-drag (RMB) take priority — they consume
            // the click when they hit an NPC so the spawn/delete handler below
            // doesn't double-fire on the same frame.
            if (UpdateEntitySelectionAndDrag()) return;

            HandleMapInteraction();
        }

        public void Activate()
        {
            _active = true;
            _root.SetActive(true);
            _mode = EditorMode.Select;
            EnsureSelectionFx();
            OpenDefaultDropdowns();
            RefreshCategoryTabs();
            RefreshPicker();
            RefreshModeButtons();
            SetStatus("Entities Editor active. F5 to close.");
            Debug.Log("[EntitiesEditor] Activated (F5)");
        }

        public void Deactivate()
        {
            // Closing F5 always flushes a pending placement/deletion rather than leaving it
            // to the debounce — the author's next action might be Stop, not another edit.
            FlushEntityPlacementAutosave();

            _active = false;
            _root.SetActive(false);
            _selectedKey = null;
            _bossDefByKey = null; // invalidate cache so next activation rescans
            CancelPickerDrag();
            // Drop world-side selection + outlines so the next Activate starts clean.
            _entityDragging = false;
            SetActiveEntity(null);
            // Reattach the camera follow target if MMB pan had detached it.
            _cameraPan.Reset();
            Valkur.Gameplay.CameraSetup.Instance?.ReattachFollow();
            if (GameEditorManager.HasInstance) GameEditorManager.Instance.NotifyDeactivated(this);
            Debug.Log("[EntitiesEditor] Deactivated (F5)");
        }

        private void ToggleActive() { if (_active) Deactivate(); else Activate(); }

        // ── UI Construction ─────────────────────────────────────────────────────

        private void BuildUI()
        {
            _canvas = EditorUIHelpers.CreateEditorCanvas("EntitiesEditorCanvas", 106);
            _canvas.transform.SetParent(transform, false);

            _root = new GameObject("Root", typeof(RectTransform));
            _root.transform.SetParent(_canvas.transform, false);
            EditorUIHelpers.StretchFill(_root);

            _ui = EntitiesEditorUIBuilder.BuildAll(
                _root.transform,
                onDropdownToggle: ToggleDropdown,
                onUndo:           () => { _undo.Undo();  SetStatus("Undo"); },
                onRedo:           () => { _undo.Redo();  SetStatus("Redo"); },
                onSave:           SaveEditedDefinitions,
                onReload:         () => { RefreshPicker(); SetStatus("Reload: catalog refreshed"); },
                onCatHostiles:    () => SelectCategory(EntityCategory.Hostiles),
                onCatNeutrals:    () => SelectCategory(EntityCategory.Neutrals),
                onCatSpecials:    () => SelectCategory(EntityCategory.Specials),
                onCatPlayers:     () => SelectCategory(EntityCategory.Players),
                onSearchChanged:  v => { _searchFilter = v ?? ""; RefreshPicker(); },
                onAdd:            () => SetMode(EditorMode.Spawn),
                onRemove:         () => SetMode(EditorMode.Delete),
                onAddOnSystem:    () => SetMode(EditorMode.AddOnSystem),
                onConfirm:        OnConfirmAddOnSystem,
                onNewKeyChanged:  v => _pendingKeyInput = v ?? "",
                onDuplicate:      () => DuplicateSelectedDefinition(),
                onRename:         () => RenameSelectedDefinition(_pendingKeyInput),
                onToggleTutorial: ToggleTutorial);

            // Tutorial overlay (F5-aware hotkey list)
            _tutorial = TutorialOverlay.Build(_root.transform, "ENTITIES HOTKEYS", new[]
            {
                ("F5",     "Toggle Entities Editor"),
                ("LMB",    "Select NPC (yellow outline; same-key peers turn orange)"),
                ("RMB",    "Drag-and-drop selected NPC on the map"),
                ("Click",  "Spawn / Delete on map (mode-aware)"),
                ("Drag",   "Drag picker slot → map to spawn"),
                ("Type",   "Filter picker by name"),
                ("Enter",  "Commit a stat field (applies to live NPCs immediately)"),
                ("New Key + Add on System → Confirm", "Create a new MonsterDefinition"),
                ("New Key + Duplicate",  "Clone the selected monster under a new key"),
                ("New Key + Rename",     "Re-key / rename the selected monster"),
                ("Save",   "Write edited definitions to disk"),
                ("Ctrl+Z", "Undo"),
                ("Ctrl+Y", "Redo"),
                ("MMB",    "Pan camera (drag)"),
                ("Esc",    "Close all editors"),
            });
            _tutorial.SetActive(false);
        }

        // ── Dropdown management ────────────────────────────────────────────────

        private void OpenDefaultDropdowns()
        {
            _openDropdowns.Clear();
            // Open the working set on activation, matching Python entities_editor:
            // tools, categories+picker (browsing), add/remove (mode switching), props.
            SetDropdownOpen("tools",      true);
            SetDropdownOpen("categories", true);
            SetDropdownOpen("picker",     true);
            SetDropdownOpen("addremove",  true);
            SetDropdownOpen("props",      true);
            RefreshMenuBtnHighlights();
        }

        private void ToggleDropdown(string name)
        {
            bool willOpen = !_openDropdowns.Contains(name);
            SetDropdownOpen(name, willOpen);
            RefreshMenuBtnHighlights();
        }

        private void SetDropdownOpen(string name, bool open)
        {
            var go = GetDropdown(name);
            if (go == null) return;

            if (open) _openDropdowns.Add(name);
            else      _openDropdowns.Remove(name);
            go.SetActive(open);
        }

        private GameObject GetDropdown(string name) => name switch
        {
            "tools"      => _ui.ToolsDropdown,
            "categories" => _ui.CategoriesDropdown,
            "picker"     => _ui.PickerDropdown,
            "addremove"  => _ui.AddRemoveDropdown,
            "props"      => _ui.PropsDropdown,
            _            => null
        };

        private void RefreshMenuBtnHighlights()
        {
            EntitiesEditorUIBuilder.ApplyMenuBtnStyle(_ui.ToolsMenuBtnImg,      _ui.ToolsMenuBtnTmp,      _openDropdowns.Contains("tools"));
            EntitiesEditorUIBuilder.ApplyMenuBtnStyle(_ui.CategoriesMenuBtnImg, _ui.CategoriesMenuBtnTmp, _openDropdowns.Contains("categories"));
            EntitiesEditorUIBuilder.ApplyMenuBtnStyle(_ui.PickerMenuBtnImg,     _ui.PickerMenuBtnTmp,     _openDropdowns.Contains("picker"));
            EntitiesEditorUIBuilder.ApplyMenuBtnStyle(_ui.AddRemoveMenuBtnImg,  _ui.AddRemoveMenuBtnTmp,  _openDropdowns.Contains("addremove"));
            EntitiesEditorUIBuilder.ApplyMenuBtnStyle(_ui.PropsMenuBtnImg,      _ui.PropsMenuBtnTmp,      _openDropdowns.Contains("props"));
        }

        private void ToggleTutorial()
        {
            if (_tutorial == null) return;
            _tutorial.SetActive(!_tutorial.activeSelf);
        }

        // ── Status helper ──────────────────────────────────────────────────────

        private void SetStatus(string msg)
        {
            if (_ui.StatusText != null) _ui.StatusText.text = msg;
        }

    }
}
