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

        /// <summary>
        /// True for an action whose context mask is a MECHANISM rather than a preference, so
        /// <see cref="InputContextPolicy"/> refuses every change to it and the Controls editor
        /// draws no context chips for it.
        ///
        /// <para>Each one is a soft lock if it can be switched off. Walking and aiming.
        /// The dash — nothing auto-switches out of Peace, so a player who loses it there has
        /// no recovery from being jumped. The stance toggle, which is the only way OUT of a
        /// posture: a control that can be disabled from inside the mode it escapes is a trap
        /// with the door locked behind it. The General Editor key, which since the F-row was
        /// retired is the only way into ANY editor — including this one, so silencing it
        /// would make itself unrecoverable. And every UI verb, because a player who silences
        /// Submit can no longer confirm the dialog asking them to confirm it.</para>
        ///
        /// <para>It is a separate fact from <see cref="Rebindable"/>: the first four may be
        /// moved to any key the player likes, they simply may not be taken away. Every
        /// non-rebindable action IS context-locked, though, and
        /// <c>InputActionCatalogTests</c> asserts that — a path nobody may move is not a
        /// preference in the other axis either.</para>
        /// </summary>
        public bool ContextLocked { get; }

        /// <summary>
        /// A non-empty tag shared by actions that are DESIGNED to answer the same control at
        /// the same time. Two actions in one group on one path are not a clash.
        ///
        /// <para>It exists because exactly one such pair is real and deliberate: Escape both
        /// closes the open editor (<c>EditorShared/Close</c>) and opens the launcher
        /// (<c>Editors/OpenGeneralEditor</c>), which is the documented one-press UX. Without a
        /// declared group the context scanner is right to call that a double fire, and it
        /// would paint Escape red in all sixteen editor tabs — a permanent false positive is
        /// how a warning stops being read. Declaring it is data, so a THIRD action arriving on
        /// Escape still lights up.</para>
        /// </summary>
        public string CoexistGroup { get; }

        /// <summary>
        /// True for an action that only fires while CTRL is held.
        ///
        /// <para>Five do: undo, redo and save in every editor, plus quick save and quick load.
        /// The Ctrl half deliberately lives in C# rather than in the binding — ten editors read
        /// <c>IsCtrlHeld()</c> as a STATE for gestures that are not shortcuts at all (Ctrl-drag,
        /// Ctrl-click), so folding it into a composite would make the modifier unreadable for
        /// those. The cost of that choice is that no scan over the asset can see it, which is
        /// how the shipped project ended up with <c>Editor.Tile/ToolSelect</c> and
        /// <c>EditorShared/Save</c> both on <c>s</c>, and <c>Editors/QuickSave</c> and
        /// <c>Editor.Tile/ProbeVolumes</c> both on <c>f5</c>, with no audit able to say whether
        /// either was a double fire. Declaring it here gives the scanner the missing half, and
        /// <see cref="EditorInput"/> reads THIS rather than a list of its own so the two cannot
        /// drift.</para>
        /// </summary>
        public bool RequiresCtrl { get; }

        public InputActionDescriptor(
            string map, string action, string displayName,
            InputActionCategory category, InputContextMask defaultContexts,
            bool reachesDamage, bool rebindable = true, string payloadKey = "",
            string ownerEditor = "", bool contextLocked = false, string coexistGroup = "",
            bool requiresCtrl = false)
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
            ContextLocked  = contextLocked;
            CoexistGroup   = coexistGroup ?? "";
            RequiresCtrl   = requiresCtrl;
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

        /// <summary>The cross-domain Selection tool. The map name is a SLUG and deliberately
        /// does NOT match the editor's own <c>EditorName</c> ("Seleccion"), which is what the
        /// descriptors below carry as their owner — comparing the two would prove nothing.</summary>
        public const string MapSelectionEditor = "Editor.Selection";
        public const string MapMapEditor       = "Editor.Map";
        public const string MapEntitiesEditor  = "Editor.Entities";
        public const string MapBossEditor      = "Editor.Boss";

        /// <summary>Escape closes the open editor AND opens the launcher, by design. See
        /// <see cref="InputActionDescriptor.CoexistGroup"/>.</summary>
        public const string CoexistEscapeChain = "escape-chain";

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

            var list = new List<InputActionDescriptor>(160);

            // ── Gameplay: movement and aim ───────────────────────────────────
            // Move and Look are live in every stance and are not a preference the stance
            // layer may touch: a stance that could take away walking or aiming is a soft lock,
            // and Peace exists so the player can WALK UP TO a vendor.
            list.Add(G("Move", "Mover", InputActionCategory.Movement, both, false, contextLocked: true));
            list.Add(G("Look", "Apuntar", InputActionCategory.Movement, both, false, rebindable: false, contextLocked: true));

            // The dash is NOT combat. It is extracted into PollTraversal and runs on both
            // sides of the stance gate, because nothing auto-switches and a Peace stance that
            // also removed the dash would leave a player who got jumped with no recovery.
            list.Add(G("Dash", "Esquiva", InputActionCategory.Traversal, both, false, contextLocked: true));

            // ── Gameplay: the war surface ────────────────────────────────────
            list.Add(G("PrimaryAttack",   "Ataque primario",  InputActionCategory.Combat, war, true));
            list.Add(G("SecondaryAttack", "Ataque secundario",InputActionCategory.Combat, war, true));
            list.Add(G("MiddleClick",     "Canalizar rayo",   InputActionCategory.Combat, war, true));

            // ── Gameplay: everyday life ──────────────────────────────────────
            list.Add(G("Interact",     "Interactuar",      InputActionCategory.Interaction, both, false));
            list.Add(G("Inventory",    "Inventario",       InputActionCategory.Interface,   both, false));
            list.Add(G("DropItem",     "Soltar objeto",    InputActionCategory.Interface,   both, false));
            list.Add(G("Pause",        "Pausa",            InputActionCategory.System,      both, false));
            // The stance toggle is the only way OUT of a posture, so it is stance-locked: a
            // control that can be switched off from inside the mode it escapes is a soft lock.
            list.Add(G("ToggleStance", "Cambiar postura",  InputActionCategory.System,      both, false,
                       contextLocked: true));
            // The world map reaches no damage path, so it is live in both postures and the
            // player may narrow or silence it from the Controls editor like Pause.
            list.Add(G("OpenWorldMap", "Mapa del mundo",   InputActionCategory.Interface,   both, false));
            // The music transport. Shipped UNBOUND, with an empty binding slot each (the editor
            // toggles' shape): the keyboard has no key nobody uses, and taking one from the spell
            // slots would be a worse trade than a key the player picks in the Controls editor.
            // Read by MusicPlayerHUD whether its panel is open or not.
            list.Add(G("MusicPlayPause", "Música: pausa / seguir", InputActionCategory.Interface, both, false));
            list.Add(G("MusicNext",      "Música: siguiente",      InputActionCategory.Interface, both, false));
            list.Add(G("MusicPrevious",  "Música: anterior",       InputActionCategory.Interface, both, false));

            // ── Gameplay: the War keyboard — every grimoire spell on a key ───
            // Every one reaches the damage path through SpellCaster, INCLUDING the ones that
            // heal or ward: the executor dispatch is shared and a spell's type is data, so a
            // slot whitelisted for Peace today becomes a damage slot the moment its
            // SpellDefinition is retuned. The slot is the unit of trust, not the spell.
            //
            // The LAYOUT is a design, and its source is tools/input/build_war_keyboard.py —
            // which writes the bindings into ValkurInputActions and prints these lines. One
            // row, one job; Shift (left) is always "the same idea, heavier or rarer":
            //   digits  damage at range (bare = a school's staple, Shift = its stronger spell)
            //   Q R T   movement          Y U   martial melee        O [ ]   summons, rare
            //   F..L    protection, heal  ; ' \ the ki charges       Z../    area control
            // The three on the mouse (fireball, slash, laser_beam) are PlayerController's and
            // carry no slot, so no second key can compete with a click — and neither does
            // `dash`, which is the Dash action (Space, right Shift, both Ctrls).

            // Digits: damage at range
            AddSpell(list, "SpellDarkball",        "darkball",          "Bola de oscuridad");     // 1
            AddSpell(list, "SpellVoidLance",       "void_lance",        "Lanza del vacio");       // Shift+1
            AddSpell(list, "SpellIceball",         "iceball",           "Bola de hielo");         // 2
            AddSpell(list, "SpellIceLance",        "ice_lance",         "Lanza de hielo");        // Shift+2
            AddSpell(list, "SpellLightball",       "lightball",         "Bola de luz");           // 3
            AddSpell(list, "SpellRadiantBurst",    "radiant_burst",     "Estallido radiante");    // Shift+3
            AddSpell(list, "SpellChargedBolt",     "charged_bolt",      "Dardo cargado");         // 4
            AddSpell(list, "SpellFlameBreath",     "flame_breath",      "Aliento igneo");         // Shift+4
            AddSpell(list, "SpellLightning",       "lightning",         "Relampago");             // 5
            AddSpell(list, "SpellChainLightning",  "chain_lightning",   "Rayo en cadena");        // Shift+5
            AddSpell(list, "SpellSeekingShard",    "seeking_shard",     "Esquirla rastreadora");  // 6
            AddSpell(list, "SpellStaticField",     "static_field",      "Campo estatico");        // Shift+6
            AddSpell(list, "SpellBoomerang",       "boomerang",         "Bumeran");               // 7
            AddSpell(list, "SpellThornBurst",      "thorn_burst",       "Estallido de espinas");  // Shift+7
            AddSpell(list, "SpellScatterVolley",   "scatter_volley",    "Andanada");              // 8
            AddSpell(list, "SpellMeteorShower",    "meteor_shower",     "Lluvia de meteoros");    // Shift+8
            AddSpell(list, "SpellLaserBeamRed",    "laser_beam_red",    "Laser rojo");            // 9
            AddSpell(list, "SpellLaserBeamYellow", "laser_beam_yellow", "Laser amarillo");        // Shift+9
            AddSpell(list, "SpellLightningBeam",   "lightning_beam",    "Haz de rayos");          // 0
            AddSpell(list, "SpellLaserBeamBlue",   "laser_beam_blue",   "Laser azul");            // Shift+0
            AddSpell(list, "SpellLaserBeamWhite",  "laser_beam_white",  "Laser blanco");          // -
            AddSpell(list, "SpellLaserBeamBlack",  "laser_beam_black",  "Laser del vacio");       // Shift+-
            AddSpell(list, "SpellLaserBeamGreen",  "laser_beam_green",  "Laser verde");           // =

            // Top row: movement, martial melee, summons
            AddSpell(list, "SpellTeleport",        "teleport",          "Teleportacion");         // Q
            AddSpell(list, "SpellShadowStep",      "shadow_step",       "Paso sombrio");          // Shift+Q
            // No slot for `dash`: it is the Dash action above (Space, right Shift, both Ctrls).
            AddSpell(list, "SpellLeapSlam",        "leap_slam",         "Salto aplastante");      // R
            AddSpell(list, "SpellGlacialStep",     "glacial_step",      "Paso glacial");          // T
            AddSpell(list, "SpellSlashStab",       "slash_stab",        "Estocada");              // Y
            AddSpell(list, "SpellSlashCleave",     "slash_cleave",      "Hendidura");             // Shift+Y
            AddSpell(list, "SpellSlashCombo",      "slash_combo",       "Combo de tajos");        // U
            AddSpell(list, "SpellSummonBarbol",    "summon_barbol",     "Invocar barbol");        // O
            AddSpell(list, "SpellSummonWolf",      "summon_wolf",       "Invocar lobo");          // Shift+O
            AddSpell(list, "SpellRaiseThrall",     "raise_thrall",      "Alzar siervo");          // [
            AddSpell(list, "SpellFireworkLaunch",  "firework_launch",   "Fuego artificial");      // Shift+[
            AddSpell(list, "SpellChargeKiVoid",    "charge_ki_void",    "Ki del vacio");          // ]

            // Home row: protection and healing, then the ki charges
            AddSpell(list, "SpellSphereMagicShield", "sphere_magic_shield", "Esfera de escudo");  // F
            AddSpell(list, "SpellGuardianLight",   "guardian_light",    "Luz guardiana");         // Shift+F
            AddSpell(list, "SpellHealingAura",     "healing_aura",      "Aura de curacion");      // G
            AddSpell(list, "SpellHealingTotem",    "healing_totem",     "Totem sanador");         // Shift+G
            AddSpell(list, "SpellBlessing",        "blessing",          "Bendicion");             // H
            AddSpell(list, "SpellSanctuary",       "sanctuary",         "Santuario");             // Shift+H
            AddSpell(list, "SpellBarkskin",        "barkskin",          "Piel de corteza");       // J
            AddSpell(list, "SpellFrozenWard",      "frozen_ward",       "Egida helada");          // Shift+J
            AddSpell(list, "SpellArcaneBarrier",   "arcane_barrier",    "Barrera arcana");        // K
            AddSpell(list, "SpellWallIce",         "wall_ice",          "Muro de hielo");         // Shift+K
            AddSpell(list, "SpellWarCry",          "war_cry",           "Grito de guerra");       // L
            AddSpell(list, "SpellChargeKiSpirit",  "charge_ki_spirit",  "Ki espiritual");         // ;
            AddSpell(list, "SpellChargeKiAzure",   "charge_ki_azure",   "Ki azur");               // Shift+;
            AddSpell(list, "SpellChargeKiVerdant", "charge_ki_verdant", "Ki verde");              // '
            AddSpell(list, "SpellChargeKiCrimson", "charge_ki_crimson", "Ki carmesi");            // Shift+'
            AddSpell(list, "SpellChargeKiSolar",   "charge_ki_solar",   "Ki solar");              // \
            AddSpell(list, "SpellChargeKiViolet",  "charge_ki_violet",  "Ki violeta");            // Shift+\

            // Bottom row: area control, and the weapon on B
            AddSpell(list, "SpellFrostNova",       "frost_nova",        "Nova de escarcha");      // Z
            AddSpell(list, "SpellBlizzard",        "blizzard",          "Ventisca");              // Shift+Z
            AddSpell(list, "SpellEntangle",        "entangle",          "Enmaranar");             // X
            AddSpell(list, "SpellSporeCloud",      "spore_cloud",       "Nube de esporas");       // Shift+X
            AddSpell(list, "SpellRootWhip",        "root_whip",         "Latigo de raices");      // C
            AddSpell(list, "SpellThunderclap",     "thunderclap",       "Trueno");                // Shift+C
            AddSpell(list, "SpellSmoke",           "smoke",             "Humo");                  // V
            AddSpell(list, "SpellSmokeEmitter",    "smoke_emitter",     "Emisor de humo");        // Shift+V
            AddSpell(list, "SpellWeaponToggle",    "weapon_toggle",     "Guardar / sacar arma");  // B
            // A SECOND weapon-draw verb, because a WeaponLoadout spell names exactly one
            // loadout key and the valkyrie carries two weapon sets. It sits on Shift+B, under
            // the first: a character with no greatsword casts it and the executor refuses.
            AddSpell(list, "SpellWeaponToggleGreatsword", "weapon_toggle_greatsword", "Sacar mandoble"); // Shift+B
            AddSpell(list, "SpellArcaneFlame",     "arcane_flame",      "Llama arcana");          // M
            AddSpell(list, "SpellCinderTrail",     "cinder_trail",      "Rastro de brasas");      // Shift+M
            AddSpell(list, "SpellVortexPull",      "vortex_pull",       "Vortice atrayente");     // ,
            AddSpell(list, "SpellVortexPush",      "vortex_push",       "Vortice repulsor");      // Shift+,
            AddSpell(list, "SpellMineBasic",       "mine_basic",        "Mina");                  // .
            AddSpell(list, "SpellPuddleLava",      "puddle_lava",       "Charco de lava");        // Shift+.
            AddSpell(list, "SpellCurseOfFrailty",  "curse_of_frailty",  "Maldicion de fragilidad"); // /

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
            // Ctrl+F5 / Ctrl+F9 — SaveLoadInputHandler tests the modifier itself, which is why
            // bare F5 reaches the Tile editor's sprite probe without saving the game.
            list.Add(Ed("QuickSave",          "Guardado rapido", requiresCtrl: true));
            list.Add(Ed("QuickLoad",          "Carga rapida",    requiresCtrl: true));
            list.Add(Ed("ToggleDevConsole",   "Consola"));
            // Escape is deliberately answered TWICE inside an editor — it closes the editor and
            // opens the launcher, which is the documented one-press UX (see
            // GeneralEditorManager.Update). The group says so in data, so the context scanner
            // stops reporting a false clash in all sixteen editor tabs while still lighting up
            // if a THIRD action ever lands on Escape.
            list.Add(Ed("OpenGeneralEditor",  "Editor general", coexistGroup: CoexistEscapeChain,
                        contextLocked: true));

            // The two modifier probes are read as HELD STATE by ten editors, never as a
            // gesture. Rebinding one moves every Ctrl-drag and Ctrl+S in the project at once,
            // which is a mechanism and not a preference.
            list.Add(Ed("CtrlModifier", "Modificador Ctrl", rebindable: false, contextLocked: true));
            list.Add(Ed("AltModifier",  "Modificador Alt",  rebindable: false, contextLocked: true));

            // ── Shared editor verbs ──────────────────────────────────────────
            // Declared ONCE and live in every editor context, which is the whole point: some
            // things must behave identically in all sixteen editors — selecting, zooming,
            // scrolling, undo, save, close — and everything else is that editor's own tool.
            // Before this they were 85 raw KeyboardInputManager / MouseInputManager calls
            // spread over 48 files, so "the same everywhere" was a convention maintained by
            // hand, and an author could not change any of them.
            list.Add(Sh("Undo",   "Deshacer", requiresCtrl: true));
            list.Add(Sh("Redo",   "Rehacer",  requiresCtrl: true));
            list.Add(Sh("Save",   "Guardar",  requiresCtrl: true));
            list.Add(Sh("Close",  "Cerrar editor", coexistGroup: CoexistEscapeChain));
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
            list.Add(Tool(MapTileEditor, "Copy",           "Copiar",        "Tile Editor", requiresCtrl: true));
            list.Add(Tool(MapTileEditor, "Cut",            "Cortar",        "Tile Editor", requiresCtrl: true));
            list.Add(Tool(MapTileEditor, "Paste",          "Pegar",         "Tile Editor", requiresCtrl: true));

            list.Add(Tool(MapBuildingsEditor, "ResetActive",         "Restaurar edificio",  "Buildings Editor"));
            list.Add(Tool(MapBuildingsEditor, "ResizeMode",          "Modo redimensionar",  "Buildings Editor"));
            list.Add(Tool(MapBuildingsEditor, "ToggleCollBrush",     "Pincel de colisiones","Buildings Editor"));
            list.Add(Tool(MapBuildingsEditor, "BrushPaintSolid",     "Pintar solido",       "Buildings Editor"));
            list.Add(Tool(MapBuildingsEditor, "BrushPaintWalk",      "Pintar transitable",  "Buildings Editor"));
            list.Add(Tool(MapBuildingsEditor, "BrushSmaller",        "Pincel mas pequeno",  "Buildings Editor"));
            list.Add(Tool(MapBuildingsEditor, "BrushBigger",         "Pincel mas grande",   "Buildings Editor"));
            list.Add(Tool(MapBuildingsEditor, "ToggleColliderScope", "Alcance CG / CU",     "Buildings Editor"));
            // Ctrl+C / Ctrl+V on a placed building. Declared as this editor's OWN Ctrl tools
            // rather than as shared verbs, because "copy" means nothing in fourteen of the
            // seventeen editors and a shared verb is one every editor has to answer. The Tile
            // editor's clipboard already established the per-editor shape.
            list.Add(Tool(MapBuildingsEditor, "Copy",                "Copiar edificio",     "Buildings Editor", requiresCtrl: true));
            list.Add(Tool(MapBuildingsEditor, "Paste",               "Pegar edificio",      "Buildings Editor", requiresCtrl: true));

            // ── Selection tool ──────────────────────────────────────────────────
            //
            // The owner is "Seleccion" — the editor's EXACT EditorName, accents and all.
            // Getting that string wrong is silent and kills every tool of the editor: it is
            // what InputContexts.Current puts in the context id and what InputContextPolicy
            // compares against, and it shipped wrong once for all 35 per-editor tools.
            //
            // The Tile and Buildings editors already established that a clipboard is a
            // PER-EDITOR Ctrl tool rather than a shared verb, because "copy" means nothing in
            // fourteen of the seventeen editors and a shared verb is one every editor has to
            // answer. Three editors now bind Ctrl+C on the same key, which is not a conflict:
            // each is live only inside its own context, and that is the property
            // InputContextLayerTests exists to protect.
            list.Add(Tool(MapSelectionEditor, "Copy",  "Copiar seleccion", "Seleccion", requiresCtrl: true));
            list.Add(Tool(MapSelectionEditor, "Paste", "Pegar seleccion",  "Seleccion", requiresCtrl: true));

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

            // The Entities editor had NO tools at all -- the only one of the fourteen -- so
            // placing a monster exactly, or aligning two of them, was a matter of dragging by
            // hand. Owner is the EditorName verbatim ("Entities Editor"): InputContexts.Current
            // puts that string in the context id and InputContextPolicy.IsLive compares the
            // two, so a mismatch here kills every tool of the editor in silence.
            list.Add(Tool(MapEntitiesEditor, "NudgeUp",    "Mover arriba",    "Entities Editor"));
            list.Add(Tool(MapEntitiesEditor, "NudgeDown",  "Mover abajo",     "Entities Editor"));
            list.Add(Tool(MapEntitiesEditor, "NudgeLeft",  "Mover izquierda", "Entities Editor"));
            list.Add(Tool(MapEntitiesEditor, "NudgeRight", "Mover derecha",   "Entities Editor"));
            list.Add(Tool(MapEntitiesEditor, "ToggleSnap", "Ajustar a rejilla", "Entities Editor"));

            return list.ToArray();
        }

        private static InputActionDescriptor G(
            string action, string label, InputActionCategory category,
            InputContextMask contexts, bool reachesDamage, bool rebindable = true,
            bool contextLocked = false) =>
            new InputActionDescriptor(MapGameplay, action, label, category, contexts,
                reachesDamage, rebindable, payloadKey: "", ownerEditor: "",
                contextLocked: contextLocked);

        private static void AddSpell(List<InputActionDescriptor> list,
            string action, string spellKey, string label) =>
            list.Add(new InputActionDescriptor(MapGameplay, action, label,
                InputActionCategory.Spell, InputContextMask.War,
                reachesDamage: true, rebindable: true, payloadKey: spellKey));

        private static InputActionDescriptor U(string action, string label) =>
            new InputActionDescriptor(MapUI, action, label, InputActionCategory.Interface,
                InputContextMask.Everywhere, reachesDamage: false, rebindable: false,
                payloadKey: "", ownerEditor: "", contextLocked: true);

        /// <summary>A verb shared by every editor: <see cref="InputContextMask.Editors"/>
        /// with no owner.</summary>
        private static InputActionDescriptor Sh(string action, string label,
                                                string coexistGroup = "",
                                                bool requiresCtrl = false) =>
            new InputActionDescriptor(MapEditorShared, action, label, InputActionCategory.Editor,
                InputContextMask.Editors, reachesDamage: false, rebindable: true,
                payloadKey: "", ownerEditor: "", contextLocked: false,
                coexistGroup: coexistGroup, requiresCtrl: requiresCtrl);

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
        /// <param name="requiresCtrl">True for a tool that only fires while Ctrl is held — the
        /// clipboard verbs, and nothing else so far. It is the SAME fact
        /// <see cref="InputActionDescriptor.RequiresCtrl"/> carries for the shared verbs, read
        /// by <see cref="EditorInput.Tool"/> and by <see cref="InputConflictScanner"/>, so a
        /// Ctrl tool and a bare tool sharing one key can never be called a double fire.</param>
        public static InputActionDescriptor Tool(string map, string action, string label,
                                                 string ownerEditor, bool requiresCtrl = false) =>
            new InputActionDescriptor(map, action, label, InputActionCategory.Editor,
                InputContextMask.Editors, reachesDamage: false, rebindable: true,
                payloadKey: "", ownerEditor: ownerEditor, contextLocked: false,
                coexistGroup: "", requiresCtrl: requiresCtrl);

        private static InputActionDescriptor Ed(string action, string label,
                                                bool rebindable = true, string coexistGroup = "",
                                                bool contextLocked = false,
                                                bool requiresCtrl = false) =>
            new InputActionDescriptor(MapEditors, action, label, InputActionCategory.Editor,
                InputContextMask.Everywhere, reachesDamage: false, rebindable,
                payloadKey: "", ownerEditor: "", contextLocked: contextLocked,
                coexistGroup: coexistGroup, requiresCtrl: requiresCtrl);
    }
}
