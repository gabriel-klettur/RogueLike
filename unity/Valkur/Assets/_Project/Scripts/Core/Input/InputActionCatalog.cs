using System;
using System.Collections.Generic;

namespace Valkur.Core.Input
{
    /// <summary>What KIND of verb an action is. Drives grouping and the key-cap tint on the
    /// drawn keyboard, and nothing about behaviour.</summary>
    public enum InputActionCategory
    {
        Movement,
        Traversal,
        Combat,
        Spell,
        Interaction,
        Interface,
        Editor,
        System,
    }

    /// <summary>
    /// Everything the binding layer needs to know about one action that is NOT already in the
    /// <c>.inputactions</c> asset. The asset owns the PATHS; this owns the meaning.
    /// </summary>
    public sealed class InputActionDescriptor
    {
        /// <summary>Action map name in the asset — "Gameplay", "UI" or "Editors".</summary>
        public string Map { get; }

        /// <summary>Action name in the asset.</summary>
        public string Action { get; }

        /// <summary><c>Map/Action</c>. The key everything else in this layer uses.</summary>
        public string Id { get; }

        public string DisplayName { get; }
        public InputActionCategory Category { get; }

        /// <summary>
        /// Contexts the action is live in, as SHIPPED. A player may narrow or widen it in the
        /// Controls editor within what <see cref="InputContextPolicy"/> allows — which is why
        /// <see cref="ReachesDamage"/> is a separate, non-negotiable fact rather than just
        /// "not in the Peace bit".
        /// </summary>
        public InputContextMask DefaultContexts { get; }

        /// <summary>
        /// For an action that belongs to ONE editor, that editor's
        /// <c>GameEditorManager.IGameEditor.EditorName</c>. Empty means the action is not
        /// editor-specific — which, combined with <see cref="InputContextMask.Editors"/>,
        /// is how the shared verbs (undo, redo, save, close, delete, select, drag-select,
        /// zoom, pan) are declared ONCE and live in all sixteen editors.
        ///
        /// <para>The split is the user-facing rule: some things are common to every editor
        /// and must behave identically everywhere, and everything else is that editor's own
        /// tool and belongs to nobody else.</para>
        /// </summary>
        public string OwnerEditor { get; }

        /// <summary>
        /// True when firing this action can put a point of damage on something. It is what
        /// makes Peace a SAFE POSTURE rather than a convention: <see cref="InputContextPolicy"/>
        /// refuses to give any such action a Peace binding, whatever the player asks for.
        ///
        /// <para>The reason this matters is recorded on <see cref="Valkur.Core.PlayerStance"/>:
        /// nothing in the damage path reads a faction, every NPC carries a <c>Health</c>, and
        /// left click both locks a target and casts — so clicking a vendor to talk to her used
        /// to throw a fireball at her. A whitelist enforced at ASSIGNMENT time is the only
        /// version of that guarantee a player cannot configure their way out of.</para>
        /// </summary>
        public bool ReachesDamage { get; }

        /// <summary>
        /// False for actions whose path is structural rather than a preference — the Ctrl/Alt
        /// modifier probes, the pointer position, the UI navigation composite. Rebinding them
        /// breaks a mechanism rather than expressing a taste, so the editor shows them and
        /// refuses to capture on them.
        /// </summary>
        public bool Rebindable { get; }

        /// <summary>
        /// For a spell action, the <c>spellKey</c> its binding casts (e.g. "darkball"). Empty
        /// for everything else. Lives here so <c>EnumerateSpellBindings</c> has one table to
        /// read instead of a hardcoded list beside the action properties.
        /// </summary>
        public string PayloadKey { get; }

        public InputActionDescriptor(
            string map, string action, string displayName,
            InputActionCategory category, InputContextMask defaultContexts,
            bool reachesDamage, bool rebindable = true, string payloadKey = "",
            string ownerEditor = "")
        {
            Map            = map;
            Action         = action;
            Id             = map + "/" + action;
            DisplayName    = displayName;
            Category       = category;
            DefaultContexts = defaultContexts;
            ReachesDamage  = reachesDamage;
            Rebindable     = rebindable;
            PayloadKey     = payloadKey ?? "";
            OwnerEditor    = ownerEditor ?? "";
        }

        public bool IsSpell => !string.IsNullOrEmpty(PayloadKey);

        /// <summary>
        /// True for a verb every editor shares, false for one editor's own tool and false for
        /// everything else.
        ///
        /// <para>Keyed off the MAP, not off the mask. The first version tested
        /// "<see cref="InputContextMask.Editors"/> and no owner", which silently swept in the
        /// UI actions and the F-key toggles — both of which are
        /// <see cref="InputContextMask.Everywhere"/>, and Everywhere contains Editors. The map
        /// is the unambiguous fact: <c>EditorShared</c> holds exactly the shared verbs.</para>
        /// </summary>
        public bool IsSharedEditorVerb =>
            string.Equals(Map, InputActionCatalog.MapEditorShared, StringComparison.Ordinal)
            && string.IsNullOrEmpty(OwnerEditor);

        public override string ToString() => Id;
    }

    /// <summary>
    /// The meaning half of the binding layer: one descriptor per bindable action, keyed by
    /// <c>Map/Action</c>.
    ///
    /// <para>WHY IT IS NOT DERIVED FROM THE ASSET. An <c>InputActionAsset</c> knows an action's
    /// name and its paths and nothing else — not whether firing it can kill a vendor, not
    /// whether it belongs on a key cap tinted as a spell, not which spell it casts. Every one
    /// of those facts used to live as a literal at the callsite that consumed it: the spell
    /// key strings and their legacy <see cref="UnityEngine.KeyCode"/> pairs were a hardcoded
    /// list inside <c>InputService.GameplayActions</c>, and "is this combat" was the identity
    /// of the method a read happened to sit in. Both are now data, in one place, which is what
    /// lets a Controls editor show the truth and a test assert on it.</para>
    ///
    /// <para>It is deliberately a CLOSED table rather than a scan: an action present in the
    /// asset and absent here is a real gap — nobody decided what it means — and
    /// <c>InputActionCatalogTests</c> fails on it rather than letting it default into
    /// something plausible. That is the same reason <c>StatKind</c> is closed and
    /// <c>PlayerStatsWiringTests</c> walks it.</para>
    /// </summary>
    public static class InputActionCatalog
    {
        public const string MapGameplay = "Gameplay";
        public const string MapUI       = "UI";
        public const string MapEditors  = "Editors";
        /// <summary>The verbs every runtime editor shares.</summary>
        public const string MapEditorShared = "EditorShared";

        /// <summary>One map per editor that has tools of its own. The name carries the
        /// editor's <c>EditorName</c>, which is also what the context id carries, so the two
        /// cannot drift apart.</summary>
        public const string MapTileEditor      = "Editor.Tile";
        public const string MapBuildingsEditor = "Editor.Buildings";
        public const string MapMapEditor       = "Editor.Map";
        public const string MapBossEditor      = "Editor.Boss";

        [SelfHealingStatic("Immutable table built once in the static constructor from constants. Holds no Unity object and is never mutated after init, so it cannot carry a destroyed reference or a stale registration across a Play session.")]
        private static readonly InputActionDescriptor[] _all;
        [SelfHealingStatic("Immutable table built once in the static constructor from constants. Holds no Unity object and is never mutated after init, so it cannot carry a destroyed reference or a stale registration across a Play session.")]
        private static readonly Dictionary<string, InputActionDescriptor> _byId;

        public static IReadOnlyList<InputActionDescriptor> All => _all;

        static InputActionCatalog()
        {
            _all  = BuildTable();
            _byId = new Dictionary<string, InputActionDescriptor>(_all.Length, StringComparer.OrdinalIgnoreCase);
            foreach (var d in _all) _byId[d.Id] = d;
        }

        public static InputActionDescriptor Find(string id) =>
            id != null && _byId.TryGetValue(id, out var d) ? d : null;

        public static InputActionDescriptor Find(string map, string action) =>
            Find(map + "/" + action);

        public static IEnumerable<InputActionDescriptor> InMap(string map)
        {
            foreach (var d in _all)
                if (string.Equals(d.Map, map, StringComparison.OrdinalIgnoreCase))
                    yield return d;
        }

        public static IEnumerable<InputActionDescriptor> Spells()
        {
            foreach (var d in _all)
                if (d.IsSpell) yield return d;
        }

        // ── The table ────────────────────────────────────────────────────────

        private static InputActionDescriptor[] BuildTable()
        {
            const InputContextMask both  = InputContextMask.Gameplay;
            const InputContextMask war   = InputContextMask.War;

            var list = new List<InputActionDescriptor>(70);

            // ── Gameplay: movement and aim ───────────────────────────────────
            // Move and Look are live in every stance and are not a preference the stance
            // layer may touch: a stance that could take away walking or aiming is a soft lock,
            // and Peace exists so the player can WALK UP TO a vendor.
            list.Add(G("Move", "Mover", InputActionCategory.Movement, both, false));
            list.Add(G("Look", "Apuntar", InputActionCategory.Movement, both, false, rebindable: false));

            // The dash is NOT combat. It is extracted into PollTraversal and runs on both
            // sides of the stance gate, because nothing auto-switches and a Peace stance that
            // also removed the dash would leave a player who got jumped with no recovery.
            list.Add(G("Dash", "Esquiva", InputActionCategory.Traversal, both, false));

            // ── Gameplay: the war surface ────────────────────────────────────
            list.Add(G("PrimaryAttack",   "Ataque primario",  InputActionCategory.Combat, war, true));
            list.Add(G("SecondaryAttack", "Ataque secundario",InputActionCategory.Combat, war, true));
            list.Add(G("MiddleClick",     "Canalizar rayo",   InputActionCategory.Combat, war, true));

            // ── Gameplay: everyday life ──────────────────────────────────────
            list.Add(G("Interact",     "Interactuar",      InputActionCategory.Interaction, both, false));
            list.Add(G("Inventory",    "Inventario",       InputActionCategory.Interface,   both, false));
            list.Add(G("DropItem",     "Soltar objeto",    InputActionCategory.Interface,   both, false));
            list.Add(G("Pause",        "Pausa",            InputActionCategory.System,      both, false));
            list.Add(G("ToggleStance", "Cambiar postura",  InputActionCategory.System,      both, false));

            // ── Gameplay: the 24 spell slots ─────────────────────────────────
            // Every one reaches the damage path through SpellCaster, INCLUDING the ones that
            // heal or ward: the executor dispatch is shared and a spell's type is data, so a
            // slot whitelisted for Peace today becomes a damage slot the moment its
            // SpellDefinition is retuned. The slot is the unit of trust, not the spell.
            AddSpell(list, "SpellDarkball",          "darkball",            "Bola oscura");
            AddSpell(list, "SpellIceball",           "iceball",             "Bola de hielo");
            AddSpell(list, "SpellLightball",         "lightball",           "Bola de luz");
            AddSpell(list, "SpellPuddleLava",        "puddle_lava",         "Charco de lava");
            AddSpell(list, "SpellMineBasic",         "mine_basic",          "Mina");
            AddSpell(list, "SpellBoomerang",         "boomerang",           "Bumeran");
            AddSpell(list, "SpellChainLightning",    "chain_lightning",     "Rayo en cadena");
            AddSpell(list, "SpellVortexPull",        "vortex_pull",         "Vortice de atraccion");
            AddSpell(list, "SpellVortexPush",        "vortex_push",         "Vortice de empuje");
            AddSpell(list, "SpellFlameBreath",       "flame_breath",        "Aliento de fuego");
            AddSpell(list, "SpellTeleport",          "teleport",            "Teleporte");
            AddSpell(list, "SpellSlash",             "slash",               "Tajo");
            AddSpell(list, "SpellLightning",         "lightning",           "Relampago");
            AddSpell(list, "SpellSphereMagicShield", "sphere_magic_shield", "Escudo esferico");
            AddSpell(list, "SpellSmoke",             "smoke",               "Humo");
            AddSpell(list, "SpellSmokeEmitter",      "smoke_emitter",       "Emisor de humo");
            AddSpell(list, "SpellArcaneFlame",       "arcane_flame",        "Llama arcana");
            AddSpell(list, "SpellFireworkLaunch",    "firework_launch",     "Fuego artificial");
            AddSpell(list, "SpellHealingAura",       "healing_aura",        "Aura curativa");
            AddSpell(list, "SpellMeteorShower",      "meteor_shower",       "Lluvia de meteoros");
            AddSpell(list, "SpellHealingTotem",      "healing_totem",       "Totem curativo");
            AddSpell(list, "SpellSummonBarbol",      "summon_barbol",       "Invocar barbol");
            AddSpell(list, "SpellWallIce",           "wall_ice",            "Muro de hielo");
            AddSpell(list, "SpellWeaponToggle",      "weapon_toggle",       "Guardar / sacar arma");

            // ── UI ───────────────────────────────────────────────────────────
            // The UI map is always enabled and is what menus, the EventSystem and every panel
            // read. It is shown in the Controls editor so a conflict against it is VISIBLE —
            // space is Dash and Submit at once, WASD is Move and Navigate at once — and it is
            // not rebindable, because a player who moves Submit off Enter can no longer
            // confirm the dialog asking them to confirm it.
            list.Add(U("Point",       "Puntero"));
            list.Add(U("Click",       "Click de interfaz"));
            list.Add(U("RightClick",  "Click der. de interfaz"));
            list.Add(U("MiddleClick", "Click central de interfaz"));
            list.Add(U("ScrollWheel", "Rueda de interfaz"));
            list.Add(U("Navigate",    "Navegar"));
            list.Add(U("Submit",      "Confirmar"));
            list.Add(U("Cancel",      "Cancelar"));

            // ── Editors ──────────────────────────────────────────────────────
            // Author surface. Live in both stances: an editor is not a combat verb, and being
            // unable to open the tile editor because the player happens to be in Peace would
            // be a bug rather than a safety property.
            list.Add(Ed("ToggleParticles",    "Editor de particulas"));
            list.Add(Ed("ToggleCombatRanges", "Rangos de combate"));
            list.Add(Ed("ToggleTimeWeather",  "Editor de tiempo y clima"));
            list.Add(Ed("ToggleSpawner",      "Editor de spawners"));
            list.Add(Ed("ToggleLighting",     "Editor de luces"));
            list.Add(Ed("ToggleSpells",       "Editor de hechizos"));
            list.Add(Ed("ToggleEntities",     "Editor de entidades"));
            list.Add(Ed("ToggleInventory",    "Editor de inventario"));
            list.Add(Ed("ToggleItems",        "Editor de objetos"));
            list.Add(Ed("ToggleTile",         "Editor de tiles"));
            list.Add(Ed("ToggleDebugHUD",     "HUD de depuracion"));
            list.Add(Ed("ToggleBuildings",    "Editor de edificios"));
            list.Add(Ed("ToggleMap",          "Editor de mapa"));
            list.Add(Ed("ToggleFSM",          "Editor de FSM"));
            list.Add(Ed("QuickSave",          "Guardado rapido"));
            list.Add(Ed("QuickLoad",          "Carga rapida"));
            list.Add(Ed("ToggleDevConsole",   "Consola"));
            list.Add(Ed("OpenGeneralEditor",  "Editor general"));

            // The two modifier probes are read as HELD STATE by ten editors, never as a
            // gesture. Rebinding one moves every Ctrl-drag and Ctrl+S in the project at once,
            // which is a mechanism and not a preference.
            list.Add(Ed("CtrlModifier", "Modificador Ctrl", rebindable: false));
            list.Add(Ed("AltModifier",  "Modificador Alt",  rebindable: false));

            // ── Shared editor verbs ──────────────────────────────────────────
            // Declared ONCE and live in every editor context, which is the whole point: some
            // things must behave identically in all sixteen editors — selecting, zooming,
            // scrolling, undo, save, close — and everything else is that editor's own tool.
            // Before this they were 85 raw KeyboardInputManager / MouseInputManager calls
            // spread over 48 files, so "the same everywhere" was a convention maintained by
            // hand, and an author could not change any of them.
            list.Add(Sh("Undo",   "Deshacer"));
            list.Add(Sh("Redo",   "Rehacer"));
            list.Add(Sh("Save",   "Guardar"));
            list.Add(Sh("Close",  "Cerrar editor"));
            list.Add(Sh("Delete", "Borrar seleccion"));
            // One Select, not a separate drag-select: they are the same button, told apart by
            // whether the pointer moved. Two actions on one control would be a conflict the
            // scanner is right to report and a distinction the player cannot bind separately.
            list.Add(Sh("Select",  "Seleccionar"));
            list.Add(Sh("PanDrag", "Desplazar camara"));
            list.Add(Sh("ZoomIn",  "Acercar"));
            list.Add(Sh("ZoomOut", "Alejar"));
            // The same verb in Particles and Spawners, on the same key, doing the same thing —
            // which is the definition of shared rather than owned. It was two literals.
            list.Add(Sh("ToggleOutlines", "Ver contornos"));

            // ── Per-editor tools ─────────────────────────────────────────────
            // Each of these is live in ITS editor and nowhere else, which is why two editors
            // are free to put different tools on the same key: an open editor owns the whole
            // board. It also means the Tile brush cannot fire while the Buildings editor is
            // open, which used to be true only because both were gated on `_active` by hand.
            //
            // The Tile block replaces EIGHT InputActions that TileEditorInputHandler built in
            // code, outside ValkurInputActions — the same defect as InventoryUI's tab, and it
            // hid a real bug: its `_redoAction` was bound to `<Keyboard>/z`, the SAME path as
            // undo, so the InputSystem half of redo had been firing on Ctrl+Z for the life of
            // the file and only the legacy half ever did the right thing.
            list.Add(Tool(MapTileEditor, "ToolBrush",      "Pincel",        "Tile Editor"));
            list.Add(Tool(MapTileEditor, "ToolEraser",     "Borrador",      "Tile Editor"));
            list.Add(Tool(MapTileEditor, "ToolFill",       "Relleno",       "Tile Editor"));
            list.Add(Tool(MapTileEditor, "ToolEyedropper", "Cuentagotas",   "Tile Editor"));
            list.Add(Tool(MapTileEditor, "ToolSelect",     "Seleccion",     "Tile Editor"));
            list.Add(Tool(MapTileEditor, "ToolAutoTile",   "Auto-tile",     "Tile Editor"));
            list.Add(Tool(MapTileEditor, "Copy",           "Copiar",        "Tile Editor"));
            list.Add(Tool(MapTileEditor, "Cut",            "Cortar",        "Tile Editor"));
            list.Add(Tool(MapTileEditor, "Paste",          "Pegar",         "Tile Editor"));

            list.Add(Tool(MapBuildingsEditor, "ResetActive",         "Restaurar edificio",  "Buildings Editor"));
            list.Add(Tool(MapBuildingsEditor, "ResizeMode",          "Modo redimensionar",  "Buildings Editor"));
            list.Add(Tool(MapBuildingsEditor, "ToggleCollBrush",     "Pincel de colisiones","Buildings Editor"));
            list.Add(Tool(MapBuildingsEditor, "BrushPaintSolid",     "Pintar solido",       "Buildings Editor"));
            list.Add(Tool(MapBuildingsEditor, "BrushPaintWalk",      "Pintar transitable",  "Buildings Editor"));
            list.Add(Tool(MapBuildingsEditor, "BrushSmaller",        "Pincel mas pequeno",  "Buildings Editor"));
            list.Add(Tool(MapBuildingsEditor, "BrushBigger",         "Pincel mas grande",   "Buildings Editor"));
            list.Add(Tool(MapBuildingsEditor, "ToggleColliderScope", "Alcance CG / CU",     "Buildings Editor"));

            // The perf probes' bisection keys. They are only read while the probe overlay is
            // SHOWING (Shift+F8 on Tile, a menu button on Buildings), which is the only reason
            // they have survived on F2-F7 — the Editors map has toggles on those same keys and
            // those stay live inside an editor by design, so while the overlay is up F3 fires
            // both the sprite bisector and ToggleSpawner. Declaring them changes no key: it
            // makes the collision visible to InputConflictScanner and lets an author move it,
            // instead of leaving a double-fire nobody can see or reach.
            list.Add(Tool(MapTileEditor, "ProbeExtraCameras",  "Probe: camaras extra", "Tile Editor"));
            list.Add(Tool(MapTileEditor, "ProbeSprites",       "Probe: sprites",       "Tile Editor"));
            list.Add(Tool(MapTileEditor, "ProbeLights",        "Probe: luces",         "Tile Editor"));
            list.Add(Tool(MapTileEditor, "ProbeVolumes",       "Probe: volumenes",     "Tile Editor"));
            list.Add(Tool(MapTileEditor, "ProbePostFx",        "Probe: post-proceso",  "Tile Editor"));
            list.Add(Tool(MapTileEditor, "ProbeExtraTilemaps", "Probe: tilemaps extra","Tile Editor"));
            list.Add(Tool(MapTileEditor, "ToggleProbe",        "Ver probe (con Shift)","Tile Editor"));

            list.Add(Tool(MapBuildingsEditor, "ProbeExtraCameras", "Probe: camaras extra", "Buildings Editor"));
            list.Add(Tool(MapBuildingsEditor, "ProbeSprites",      "Probe: sprites",       "Buildings Editor"));
            list.Add(Tool(MapBuildingsEditor, "ProbeLights",       "Probe: luces",         "Buildings Editor"));
            list.Add(Tool(MapBuildingsEditor, "ProbeVolumes",      "Probe: volumenes",     "Buildings Editor"));
            list.Add(Tool(MapBuildingsEditor, "ProbePostFx",       "Probe: post-proceso",  "Buildings Editor"));
            list.Add(Tool(MapBuildingsEditor, "ProbeColliders",    "Probe: colisiones",    "Buildings Editor"));

            list.Add(Tool(MapMapEditor, "NewSlot",   "Nueva ranura",     "Map Editor"));
            list.Add(Tool(MapMapEditor, "Duplicate", "Duplicar ranura",  "Map Editor"));
            list.Add(Tool(MapMapEditor, "Rename",    "Renombrar ranura", "Map Editor"));
            list.Add(Tool(MapMapEditor, "EditSlot",  "Editar ranura",    "Map Editor"));

            list.Add(Tool(MapBossEditor, "TapCue", "Marcar pulso", "Boss Editor"));

            return list.ToArray();
        }

        private static InputActionDescriptor G(
            string action, string label, InputActionCategory category,
            InputContextMask contexts, bool reachesDamage, bool rebindable = true) =>
            new InputActionDescriptor(MapGameplay, action, label, category, contexts,
                reachesDamage, rebindable);

        private static void AddSpell(List<InputActionDescriptor> list,
            string action, string spellKey, string label) =>
            list.Add(new InputActionDescriptor(MapGameplay, action, label,
                InputActionCategory.Spell, InputContextMask.War,
                reachesDamage: true, rebindable: true, payloadKey: spellKey));

        private static InputActionDescriptor U(string action, string label) =>
            new InputActionDescriptor(MapUI, action, label, InputActionCategory.Interface,
                InputContextMask.Everywhere, reachesDamage: false, rebindable: false);

        /// <summary>A verb shared by every editor: <see cref="InputContextMask.Editors"/>
        /// with no owner.</summary>
        private static InputActionDescriptor Sh(string action, string label) =>
            new InputActionDescriptor(MapEditorShared, action, label, InputActionCategory.Editor,
                InputContextMask.Editors, reachesDamage: false, rebindable: true);

        /// <summary>
        /// One editor's own tool: <see cref="InputContextMask.Editors"/> plus the owner, so it
        /// is live in that editor and nowhere else.
        ///
        /// <para><paramref name="ownerEditor"/> must be the editor's EXACT
        /// <c>GameEditorManager.IGameEditor.EditorName</c> — "Tile Editor", not "Tile". That
        /// string is what <see cref="InputContexts.Current"/> puts in the context id, and
        /// <see cref="InputContextPolicy.IsLive"/> compares the two. Getting it wrong is
        /// silent: every tool of that editor simply never fires, and a test that derives its
        /// editor list FROM these owners will happily pass while it happens.
        /// <c>EditorReachabilityTests</c> asserts each owner against the shipped
        /// EditorNames.</para>
        /// </summary>
        public static InputActionDescriptor Tool(string map, string action, string label,
                                                 string ownerEditor) =>
            new InputActionDescriptor(map, action, label, InputActionCategory.Editor,
                InputContextMask.Editors, reachesDamage: false, rebindable: true,
                payloadKey: "", ownerEditor: ownerEditor);

        private static InputActionDescriptor Ed(string action, string label, bool rebindable = true) =>
            new InputActionDescriptor(MapEditors, action, label, InputActionCategory.Editor,
                InputContextMask.Everywhere, reachesDamage: false, rebindable);
    }
}
