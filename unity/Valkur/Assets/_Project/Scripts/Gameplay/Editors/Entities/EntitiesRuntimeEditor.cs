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
        /// <summary>Which catalogue the selection came from. Read by the Animation panel,
        /// which has to ask a PlayerDefinition or a MonsterDefinition for the asset config —
        /// the two live in different catalogues and the key alone cannot say which.</summary>
        private bool       _selectedIsPlayer;

        /// <summary>
        /// The picker's tabs. <c>All</c> exists because without it the whole catalogue could
        /// never be seen at once, so an entity in an unexpected tab was invisible unless you
        /// guessed which one — and guessing is exactly what a misfiled entity defeats.
        /// </summary>
        private enum EntityCategory { All, Hostiles, Neutrals, Specials, Players }
        private EntityCategory _category = EntityCategory.All;

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

        // Middle-mouse camera pan — shared controller used by every runtime editor.
        private readonly EditorCameraPanController _cameraPan = new EditorCameraPanController();

        // Mouse-wheel zoom, shared with every runtime editor that can pan. An editor that
        // lets the author move the world camera but not close in on it is the odd one out,
        // and eight of the eleven panning editors were exactly that. The controller steps
        // through CameraSetup.ComputeEditorZoomNext, which stays on the PPU ladder
        // SnapOrthoSize maintains — that is why spreading it does not fall foul of the
        // "never write orthographicSize for an effect" rule.
        private readonly EditorCameraZoomController _cameraZoom = new EditorCameraZoomController();

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

            // Placements are NOT loaded here any more. PlacedEntityService does that from the
            // boot sequence, in every build; this editor is only a client of it.
        }

        protected override void OnDestroy()
        {
            // Stopping Play Mode without closing the editor first still has to persist whatever
            // is pending. The service flushes on its own OnDestroy too; destruction order
            // between the two is undefined, and a clean table writes nothing twice.
            FlushPlacedEntities();
            ShutdownAnimationPreview();
            if (_ownsToggleAction) _toggleAction?.Dispose();
            if (GameEditorManager.HasInstance) GameEditorManager.Instance.Unregister(this);
            base.OnDestroy();
        }

        private void Update()
        {
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

            if (!_active) return;

            HandleSharedEditorVerbs();
            HandleEntitiesEditorTools();

            // Middle-mouse pan runs unconditionally so dragging the camera works
            // even while a picker drag or entity drag is in progress.
            _cameraPan.Tick();
            _cameraZoom.Tick();

            TickAnimationPreview();
            TickTimelinePanel();

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
            EnsureSelectionFx();
            OpenDefaultDropdowns();
            RefreshCategoryTabs();
            RefreshPicker();
            // SetMode, not "_mode = Select" plus RefreshModeButtons. The field assignment lit
            // the right button and left the Add/Remove hint on its build-time default, so the
            // editor opened in Select mode reading "Select a mode then click on the map" --
            // measured live. SetMode is the one place that puts the mode, the buttons, the
            // hint and the status line in agreement, which is exactly why it exists.
            SetMode(EditorMode.Select);
            // No key is named here: the F-row toggles were retired on 2026-09-05 and this
            // editor is reached from Escape. Naming a dead key in the FIRST sentence an author
            // reads is the loading-screen-tips defect with a shorter blast radius.
            SetStatus("Entities Editor active — Escape to close.");
        }

        /// <summary>
        /// Undo, Redo and Save from the keyboard.
        ///
        /// <para><b>This editor was the fourteenth of fourteen, and the only one without
        /// it.</b> Thirteen runtime editors read <c>EditorInput.UndoPressed/RedoPressed</c>
        /// and there is no generic dispatcher — each one asks for itself — so Ctrl+Z, Ctrl+Y
        /// and Ctrl+S simply did nothing here. Meanwhile the editor's own overlay, titled
        /// ENTITIES HOTKEYS, listed <c>Ctrl+Z Undo</c> and <c>Ctrl+Y Redo</c>: the tutorial
        /// taught two keys nothing read. It is the same defect as the seven overlays teaching
        /// retired F-keys, wearing the other costume — that guard looks for keys that were
        /// RETIRED, and cannot see a key that was never wired.</para>
        ///
        /// <para>It hurt double because the undo stack was widened the day before to cover
        /// every definition edit, from one gesture out of fifteen. The coverage was there and
        /// the keyboard could not reach it.</para>
        ///
        /// <para>Reads the SHARED map, never a literal KeyCode: a binding built in C# is
        /// invisible to the Controls editor and to the conflict scanner, and moving Undo there
        /// has to move it here too.</para>
        /// </summary>
        private void HandleSharedEditorVerbs()
        {
            // No InputBlocker guard, and no modifier test here: all three of these verbs
            // declare RequiresCtrl in the catalogue, and EditorInput.Live matches the held
            // state against that descriptor -- which is the same field the conflict scanner
            // reads, so a bare keystroke in a text field cannot reach them. Adding a second,
            // local answer to "is this press mine" is how one editor comes to disagree with
            // the other thirteen.
            if (EditorInput.UndoPressed())      { _undo.Undo(); SetStatus("Undo (Ctrl+Z)."); }
            else if (EditorInput.RedoPressed()) { _undo.Redo(); SetStatus("Redo (Ctrl+Y)."); }

            if (EditorInput.SavePressed()) SaveEditedDefinitions();
        }

        public void Deactivate()
        {
            // Closing the editor always flushes a pending placement/deletion rather than leaving
            // to the debounce — the author's next action might be Stop, not another edit.
            FlushPlacedEntities();

            _active = false;
            _root.SetActive(false);
            _selectedKey = null;
            _bossDefByKey = null; // invalidate cache so next activation rescans
            CancelPickerDrag();
            // Drop world-side selection + outlines so the next Activate starts clean.
            _entityDragging = false;
            SetActiveEntity(null);
            // The preview camera renders every frame it is enabled, so closing the editor has
            // to stop it — a panel nobody can see still costs a draw.
            SetAnimationPanelOpen(false);
            SetTimelinePanelOpen(false);
            // Reattach the camera follow target if MMB pan had detached it.
            _cameraPan.Reset();
            Valkur.Gameplay.CameraSetup.Instance?.ReattachFollow();
            if (GameEditorManager.HasInstance) GameEditorManager.Instance.NotifyDeactivated(this);
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
                onReload:         ReloadDefinitionsFromDisk,
                onCatAll:         () => SelectCategory(EntityCategory.All),
                onCatHostiles:    () => SelectCategory(EntityCategory.Hostiles),
                onCatNeutrals:    () => SelectCategory(EntityCategory.Neutrals),
                onCatSpecials:    () => SelectCategory(EntityCategory.Specials),
                onCatPlayers:     () => SelectCategory(EntityCategory.Players),
                onSearchChanged:  v => { _searchFilter = v ?? ""; RefreshPicker(); },
                onAdd:            () => SetMode(EditorMode.Spawn),
                onRemove:         () => SetMode(EditorMode.Delete),
                onAddOnSystem:    () => SetMode(EditorMode.AddOnSystem),
                onConfirm:        OnConfirmAddOnSystem,
                onNewKeyChanged:  v => { _pendingKeyInput = v ?? ""; DisarmRename(); },
                onDuplicate:      () => DuplicateSelectedDefinition(),
                onRename:         OnRenameRequested,
                onToggleTutorial: ToggleTutorial,
                onSectionFold:    OnSectionFoldToggled);

            // Built outside BuildAll: it needs six callbacks no other panel shares, and
            // BuildAll already carries eighteen.
            EntitiesEditorUIBuilder.BuildAnimationPanel(
                _root.transform, ref _ui,
                onStateChanged:   OnAnimationStateChanged,
                onVariantChanged: OnAnimationVariantChanged,
                onLoadoutChanged: OnAnimationLoadoutChanged,
                onDirectionSlot:  OnAnimationDirectionSlot,
                onZoomIn:         () => OnAnimationZoom(1f),
                onZoomOut:        () => OnAnimationZoom(-1f),
                onTogglePlay:     OnAnimationTogglePlay,
                onStepBack:       () => OnAnimationStep(-1),
                onStepForward:    () => OnAnimationStep(1),
                onToggleReverse:  OnAnimationToggleReverse,
                onStripCell:      OnAnimationStripCell,
                onEntitySpeed:    OnAnimationEntitySpeedCommitted,
                onStateSpeed:     OnAnimationStateSpeedCommitted,
                onVariantSpeed:   OnAnimationVariantSpeedCommitted,
                onHoldLastFrame:  OnAnimationHoldToggled,
                onLayoutChanged:  OnAnimationLayoutChanged,
                onToggleMuzzle:   OnToggleMuzzlePlacement,
                onMuzzleScope:    OnMuzzleScopeChanged,
                onMuzzleSpell:    OnMuzzleSpellChanged,
                onMuzzleClear:    OnMuzzleClear,
                onRepeatFrom:     OnAnimationRepeatFromCommitted,
                onRepeatCount:    OnAnimationRepeatCountCommitted);

            if (_ui.PropsFilterInput != null)
                _ui.PropsFilterInput.onValueChanged.AddListener(OnPropsFilterChanged);

            EntitiesEditorUIBuilder.BuildTimelinePanel(
                _root.transform, ref _ui,
                onSeed:            OnTimelineSeed,
                onClear:           OnTimelineClear,
                onApplyToSpell:    ApplyTimelineToSpell,
                onStepClicked:     OnTimelineStepClicked,
                onStepDuration:    OnTimelineStepDuration,
                onReleaseChanged:  OnTimelineReleaseChanged,
                onRecoverChanged:  OnTimelineRecoverChanged,
                onPrepareMode:     OnTimelinePrepareMode,
                onChannelMode:     OnTimelineChannelMode);

            // Tutorial overlay (F5-aware hotkey list)
            _tutorial = TutorialOverlay.Build(_root.transform, "ENTITIES HOTKEYS", new[]
            {
                // The F-row toggles were retired 2026-09-05: every runtime editor is opened
                // from the General Editor on Escape, and the thirteen toggle actions ship
                // UNBOUND. A tutorial that teaches a dead key is the loading-screen tips
                // defect wearing an overlay -- it costs an author the only lesson the panel
                // exists to give, and nothing throws when a key never fires.
                ("Esc",    "Open the General Editor, then Entities"),
                ("LMB",    "Select NPC (yellow outline; same-key peers turn orange)"),
                ("RMB",    "Drag-and-drop selected NPC on the map"),
                ("Click",  "Spawn / Delete on map (mode-aware)"),
                ("Drag",   "Drag picker slot -> map to spawn"),
                ("Type",   "Filter picker by name"),
                ("Enter",  "Commit a stat field (applies to live NPCs immediately)"),
                ("New Key + Add on System -> Confirm", "Create a new MonsterDefinition"),
                ("New Key + Duplicate",  "Clone the selected monster under a new key"),
                ("New Key + Rename",     "Re-key / rename the selected monster"),
                ("Arrows", "Nudge the selected NPC one tile (Shift = a tenth)"),
                ("G",      "Grid snap on / off"),
                // Ctrl+S / Ctrl+Z / Ctrl+Y are listed because this editor now READS them.
                // It did not: it was the only one of the fourteen that never asked
                // EditorInput for the shared verbs, so these three rows taught keys that
                // did nothing -- the retired-F-key defect in the other direction, and one
                // the guard for that cannot see, because these keys were never retired.
                ("Ctrl+S", "Write the definitions THIS editor edited to disk"),
                ("Ctrl+Z", "Undo (covers stats, placements and deletions)"),
                ("Ctrl+Y", "Redo"),
                ("MMB",    "Pan camera (drag)"),
                ("Esc",    "Close this editor"),
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

            // The Animation panel owns an off-screen camera and a RenderTexture, so opening
            // and closing the dropdown is what starts and stops them. Every other panel is
            // pure UI and needs no such hook.
            if (name == "animation") SetAnimationPanelOpen(open);
            if (name == "timeline")  SetTimelinePanelOpen(open);
        }

        /// <summary>
        /// Every panel the menu bar owns. A LIST rather than a repeated literal because three
        /// places walk it — the highlight refresh, the workspace reconcile and the lookup
        /// below — and a name added to two of the three is a panel the menu bar half knows
        /// about.
        /// </summary>
        [Valkur.Core.SelfHealingStatic("Constant panel-name table; written once at class init, never mutated.")]
        private static readonly string[] DropdownNames =
        {
            "tools", "categories", "picker", "addremove", "props", "animation", "timeline"
        };

        private GameObject GetDropdown(string name) => name switch
        {
            "tools"      => _ui.ToolsDropdown,
            "categories" => _ui.CategoriesDropdown,
            "picker"     => _ui.PickerDropdown,
            "addremove"  => _ui.AddRemoveDropdown,
            "props"      => _ui.PropsDropdown,
            "animation"  => _ui.AnimDropdown,
            "timeline"   => _ui.TimelineDropdown,
            _            => null
        };

        private void RefreshMenuBtnHighlights()
        {
            EntitiesEditorUIBuilder.ApplyMenuBtnStyle(_ui.ToolsMenuBtnImg,      _ui.ToolsMenuBtnTmp,      _openDropdowns.Contains("tools"));
            EntitiesEditorUIBuilder.ApplyMenuBtnStyle(_ui.CategoriesMenuBtnImg, _ui.CategoriesMenuBtnTmp, _openDropdowns.Contains("categories"));
            EntitiesEditorUIBuilder.ApplyMenuBtnStyle(_ui.PickerMenuBtnImg,     _ui.PickerMenuBtnTmp,     _openDropdowns.Contains("picker"));
            EntitiesEditorUIBuilder.ApplyMenuBtnStyle(_ui.AddRemoveMenuBtnImg,  _ui.AddRemoveMenuBtnTmp,  _openDropdowns.Contains("addremove"));
            EntitiesEditorUIBuilder.ApplyMenuBtnStyle(_ui.PropsMenuBtnImg,      _ui.PropsMenuBtnTmp,      _openDropdowns.Contains("props"));
            EntitiesEditorUIBuilder.ApplyMenuBtnStyle(_ui.AnimMenuBtnImg,       _ui.AnimMenuBtnTmp,       _openDropdowns.Contains("animation"));
            EntitiesEditorUIBuilder.ApplyMenuBtnStyle(_ui.TimelineMenuBtnImg,   _ui.TimelineMenuBtnTmp,   _openDropdowns.Contains("timeline"));
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
