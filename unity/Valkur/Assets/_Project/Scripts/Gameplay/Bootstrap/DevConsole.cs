using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using Valkur.Core;
using Valkur.Core.Input;

namespace Valkur.Gameplay
{
    /// <summary>
    /// In-game developer console. Toggle open/close with backtick (~).
    /// Draws a compact IMGUI overlay at the bottom of the screen; no Canvas setup required.
    ///
    /// Commands are registered via <see cref="ConsoleCommand"/> records in the internal
    /// <see cref="RegisterCommand"/> registry (see DevConsole.Registry.cs). Use Tab for
    /// autocomplete and Up/Down arrows to navigate command history.
    /// </summary>
    public partial class DevConsole : SingletonMonoBehaviour<DevConsole>
    {
        // ── Public open/close events (consumed by ChatInputGate) ──────────────
        /// <summary>Fired when the console transitions from closed to open.</summary>
        public event Action OnOpened;
        /// <summary>Fired when the console transitions from open to closed.</summary>
        public event Action OnClosed;

        /// <summary>Whether the dev console is currently visible.</summary>
        public bool IsOpen => _open;

        // ── Command history ────────────────────────────────────────────────────
        private readonly List<string> _commandHistory = new List<string>();
        private int _historyCursor = -1;
        private const int MAX_HISTORY = 50;

        private bool _open;

        private InputAction _toggleAction;
        private bool _ownsToggleAction;
        private bool _godMode;

        // ── Noclip state ───────────────────────────────────────────────────────
        private bool _noclipActive;
        private int _noclipOriginalLayer;

        // ------------------------------------------------------------------
        // Lifecycle
        // ------------------------------------------------------------------

        protected override void OnSingletonAwake()
        {
            _toggleAction = EditorHotkeyBindings.Resolve(
                EditorHotkeyBindings.Hotkey.ToggleDevConsole, out _ownsToggleAction);

            // Before RegisterDefaults, and not gated on the panel being open: an error that
            // fires during boot is exactly the one worth having in the buffer by the time
            // anybody thinks to press ~.
            BeginCapturingEngineLogs();
            RegisterDefaults();
        }

        protected override void OnDestroy()
        {
            StopCapturingEngineLogs();
            if (_ownsToggleAction) { _toggleAction?.Disable(); _toggleAction?.Dispose(); }
            base.OnDestroy();
        }

        private void Update()
        {
            if (EditorHotkeyBindings.WasPerformedThisFrame(EditorHotkeyBindings.Hotkey.ToggleDevConsole))
                SetOpen(!_open);

            if (!_open) return;

            PollPanelKeys();
            RefreshLog(scrollToBottom: false);
            TickLayoutPersistence();
        }

        /// <summary>
        /// The single owner of "is the console up". The panel, the open flag and the two
        /// public events move together here and nowhere else — the IMGUI version flipped
        /// <c>_open</c> in three places (the toggle, the click-outside branch in OnGUI, and
        /// the toggle's own else-arm) and each raised the events slightly differently.
        /// </summary>
        private void SetOpen(bool open)
        {
            if (open == _open) return;
            _open = open;

            if (open)
            {
                ShowPanel();
                OnOpened?.Invoke();
            }
            else
            {
                HidePanel();
                OnClosed?.Invoke();
            }
        }

        // ------------------------------------------------------------------
        // Keyboard
        // ------------------------------------------------------------------

        /// <summary>
        /// Tab, the arrows and Escape, read while the console itself is holding the input
        /// block up.
        ///
        /// <para>This is where a long-standing silent defect was: opening the console raises
        /// <see cref="InputBlocker"/> through ChatInputGate, and Tab / Up / Down are not on
        /// the always-allowed list, so <see cref="KeyboardInputManager"/> refused them for
        /// exactly as long as the panel was up. Autocomplete and history had no way to fire,
        /// and a refused read logs nothing. They go through the explicit
        /// block-ignoring read now, which is safe here for the one reason that matters: this
        /// component is the one that raised the block.</para>
        /// </summary>
        private void PollPanelKeys()
        {
            if (KeyboardInputManager.WasEscapePressedThisFrame())
            {
                // Escape narrows before it closes: dismissing the popup is what the user
                // means when the popup is the thing that is up.
                if (SuggestionsVisible) { HideSuggestions(); return; }
                SetOpen(false);
                return;
            }

            // The search box is a text field too, and every key below belongs to the COMMAND
            // line. Without this, typing a search term and pressing Enter runs whatever is
            // sitting in the other box, and Tab pops an autocomplete list over a search.
            if (SearchFieldFocused) return;

            if (ConsoleKey(Key.UpArrow, KeyCode.UpArrow))
            {
                if (SuggestionsVisible) MoveSuggestion(-1);
                else RecallHistory(1);
            }
            else if (ConsoleKey(Key.DownArrow, KeyCode.DownArrow))
            {
                if (SuggestionsVisible) MoveSuggestion(1);
                else RecallHistory(-1);
            }

            if (ConsoleKey(Key.Tab, KeyCode.Tab))
            {
                if (SuggestionsVisible) AcceptSuggestion();
                else RecomputeSuggestions(InputText);
            }

            // Enter is on the always-allowed list, so it needs no exemption. It is polled
            // here as well as bound to the field's onSubmit because the InputSystem can drop
            // the key event that would have reached the field; SubmitInputBuffer is
            // idempotent per frame, so both paths firing is harmless.
            if (KeyboardInputManager.WasEnterPressedThisFrame())
                SubmitInputBuffer();
        }

        private static bool ConsoleKey(Key newKey, KeyCode legacyKey)
            => KeyboardInputManager.WasKeyPressedThisFrameIgnoringBlock(newKey, legacyKey);

        /// <summary>
        /// Walks the command history. Positive is older, which is what Up means.
        /// </summary>
        private void RecallHistory(int direction)
        {
            if (_commandHistory.Count == 0) return;

            if (direction > 0)
            {
                _historyCursor = Mathf.Min(_historyCursor + 1, _commandHistory.Count - 1);
            }
            else
            {
                _historyCursor--;
                if (_historyCursor < 0)
                {
                    _historyCursor = -1;
                    SetInputText(string.Empty);
                    return;
                }
            }

            SetInputText(_commandHistory[_commandHistory.Count - 1 - _historyCursor]);
        }

        // ------------------------------------------------------------------
        // Input line
        // ------------------------------------------------------------------

        private string InputText => _inputField != null ? _inputField.text : string.Empty;

        /// <summary>
        /// Writes the input line WITHOUT notifying, then dismisses the popup by hand. The
        /// change callback would rebuild the candidate list from text the console just put
        /// there, which pops a list open over a line the user did not type.
        /// </summary>
        private void SetInputText(string text)
        {
            if (_inputField == null) return;
            _inputField.SetTextWithoutNotify(text ?? string.Empty);
            _inputField.caretPosition = _inputField.text.Length;
            HideSuggestions();
            FocusInput();
        }

        // Frame-guard: the Enter poll and the field's own onSubmit can both land on the same
        // press, and without this the command would run twice.
        private int _lastSubmitFrame = -1;

        private void SubmitInputBuffer()
        {
            if (_lastSubmitFrame == Time.frameCount) return;
            _lastSubmitFrame = Time.frameCount;

            string raw = InputText;
            if (!string.IsNullOrWhiteSpace(raw))
                ExecuteCommand(raw.Trim());

            SetInputText(string.Empty);
            RefreshLog(scrollToBottom: true);
        }

        // ------------------------------------------------------------------
        // Command Execution
        // ------------------------------------------------------------------

        /// <summary>
        /// Runs a console command line exactly as if it had been typed.
        ///
        /// Public on purpose: it makes the whole reload surface below reachable from
        /// PlayMode tests and from `mcp__unity__execute_code`, so the same agent that
        /// edits the C# can trigger the verification without anyone touching the Game
        /// view. That is the difference between "restart to check" and "check".
        /// </summary>
        public void Execute(string raw) => ExecuteCommand(raw);

        private void ExecuteCommand(string raw)
        {
            AppendLine($"> {raw}", ConsoleLineKind.Echo);
            PushHistory(raw);

            var parts = raw.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return;

            // Strip leading slash so "/godmode" and "godmode" both resolve.
            string cmdName = parts[0].TrimStart('/');

            if (TryResolve(cmdName, out var cmd))
                cmd.Handler?.Invoke(parts);
            else
                Log($"Unknown command: '{parts[0]}'. Type 'help' for a list.");
        }

        // ------------------------------------------------------------------
        // History helpers
        // ------------------------------------------------------------------

        private void PushHistory(string cmd)
        {
            if (string.IsNullOrWhiteSpace(cmd)) return;
            // Dedupe: remove if the last entry is identical.
            if (_commandHistory.Count > 0 &&
                string.Equals(_commandHistory[_commandHistory.Count - 1], cmd, StringComparison.Ordinal))
            {
                _historyCursor = -1;
                return;
            }
            _commandHistory.Add(cmd);
            while (_commandHistory.Count > MAX_HISTORY)
                _commandHistory.RemoveAt(0);
            _historyCursor = -1;
        }

        // ------------------------------------------------------------------
        // Default registration (called from OnSingletonAwake)
        // ------------------------------------------------------------------

        private void RegisterDefaults()
        {
            // ── core ──────────────────────────────────────────────────────────
            RegisterCommand(new ConsoleCommand {
                Name = "help", Aliases = new[] { "?" },
                Usage = "help [cmd]", Help = "list commands or show detail for one command",
                Category = "core",
                Handler = args => {
                    if (args.Length >= 2)
                    {
                        string target = args[1].TrimStart('/');
                        if (TryResolve(target, out var found))
                        {
                            Log($"  {found.Usage}");
                            Log($"    {found.Help}");
                            if (found.Aliases != null && found.Aliases.Length > 0)
                                Log($"    aliases: {string.Join(", ", found.Aliases)}");
                        }
                        else
                            Log($"No command '{args[1]}' found.");
                    }
                    else
                        CmdHelp();
                }
            });
            RegisterCommand(new ConsoleCommand {
                Name = "clear",
                Usage = "clear", Help = "clear the console log",
                Category = "core",
                Handler = _ => ClearLog()
            });

            // ── cheats ────────────────────────────────────────────────────────
            RegisterCommand(new ConsoleCommand {
                Name = "godmode", Aliases = new[] { "god" },
                Usage = "godmode", Help = "toggle player invincibility",
                Category = "cheats",
                Handler = _ => CmdGodMode()
            });
            RegisterCommand(new ConsoleCommand {
                Name = "heal",
                Usage = "heal", Help = "restore player to full HP and MP",
                Category = "cheats",
                Handler = _ => CmdHeal()
            });
            RegisterCommand(new ConsoleCommand {
                Name = "vida", Aliases = new[] { "/vida" },
                Usage = "vida", Help = "restore player to full HP and MP (alias of heal)",
                Category = "cheats",
                Handler = _ => CmdHeal()
            });
            RegisterCommand(new ConsoleCommand {
                Name = "mana", Aliases = new[] { "/mana" },
                Usage = "mana", Help = "restore player to full mana",
                Category = "cheats",
                Handler = _ => CmdMana()
            });
            RegisterCommand(new ConsoleCommand {
                Name = "resurrect", Aliases = new[] { "/resurrect" },
                Usage = "resurrect", Help = "revive player at full HP (closes death screen if open)",
                Category = "cheats",
                Handler = _ => CmdResurrect()
            });
            RegisterCommand(new ConsoleCommand {
                Name = "givememoney", Aliases = new[] { "/givememoney" },
                Usage = "givememoney [amount]", Help = "add coins to player wallet (default 1000)",
                Category = "cheats",
                Handler = args => CmdGiveMeMoney(args)
            });
            RegisterCommand(new ConsoleCommand {
                Name = "kill", Aliases = new[] { "/kill" },
                Usage = "kill [all]", Help = "kill player (no arg) or all enemies (arg=all)",
                Category = "cheats",
                Handler = args => CmdKill(args)
            });
            RegisterCommand(new ConsoleCommand {
                Name = "suicide", Aliases = new[] { "/suicide" },
                Usage = "suicide", Help = "kill player (alias of 'kill' with no args)",
                Category = "cheats",
                Handler = _ => CmdKill(new[] { "suicide" })
            });
            RegisterCommand(new ConsoleCommand {
                Name = "killall",
                Usage = "killall", Help = "kill all active enemies",
                Category = "cheats",
                Handler = _ => CmdKillAll()
            });
            RegisterCommand(new ConsoleCommand {
                Name = "layer",
                Usage = "layer <0..8>",
                Help = "set the player's CurrentVisualLayer (0=Ground … 8=OverheadDetails). " +
                       "Diagnostic for the per-layer collisions pipeline (M1.5 / M2).",
                Category = "cheats",
                Handler = args => CmdLayer(args)
            });
            RegisterCommand(new ConsoleCommand {
                Name = "verbose",
                Usage = "verbose [category] [on|off]",
                Help = "list or toggle high-volume dev logging (world, settings, " +
                       "collision, bootstrap, all). Off by default; the choice persists.",
                Category = "cheats",
                Handler = args => CmdVerbose(args)
            });
            RegisterCommand(new ConsoleCommand {
                Name = "layerdiag",
                Usage = "layerdiag",
                Help = "dump the per-visual-layer collision pipeline state " +
                       "(player layer, includeLayers, matrix, baker state, sub-tilemaps).",
                Category = "cheats",
                Handler = _ => CmdLayerDiag()
            });
            RegisterCommand(new ConsoleCommand {
                Name = "noclip", Aliases = new[] { "/noclip" },
                Usage = "noclip [on|off]", Help = "toggle collision with the world layer",
                Category = "cheats",
                Handler = args => CmdNoclip(args)
            });
            RegisterCommand(new ConsoleCommand {
                Name = "restockvendorfood", Aliases = new[] { "/restockvendorfood" },
                Usage = "restockvendorfood <vendor_name|current> [qty]",
                Help = "restock consumable food items of a vendor (default qty 100)",
                Category = "cheats",
                Handler = args => CmdRestockVendorFood(args)
            });

            // ── world ─────────────────────────────────────────────────────────
            RegisterCommand(new ConsoleCommand {
                Name = "tp", Aliases = new[] { "teleport", "/teleport" },
                Usage = "tp  |  tp <x> <y>  |  tp <world> <x> <y>  |  tp <world>",
                Help = "no args → warp to mouse cursor; else coords or a world/zone slug",
                Category = "world",
                Handler = args => CmdTeleport(args),
                Completer = args => WorldSlugCompleter(args)
            });
            RegisterCommand(new ConsoleCommand {
                Name = "time",
                Usage = "time <0..1>", Help = "set day/night cycle time (0=midnight, 0.5=noon)",
                Category = "world",
                Handler = args => CmdSetTime(args)
            });
            RegisterCommand(new ConsoleCommand {
                Name = "world",
                Usage = "world <slug>", Help = "swap to a different world by descriptor slug",
                Category = "world",
                Handler = args => CmdWorld(args),
                Completer = args => WorldSlugCompleter(args)
            });
            RegisterCommand(new ConsoleCommand {
                Name = "worlds",
                Usage = "worlds", Help = "list available world descriptors",
                Category = "world",
                Handler = _ => CmdWorldList()
            });

            // ── inventory ─────────────────────────────────────────────────────
            RegisterCommand(new ConsoleCommand {
                Name = "give",
                Usage = "give <item_id> [qty]", Help = "add item(s) to player inventory",
                Category = "inventory",
                Handler = args => CmdGive(args),
                Completer = args => ItemIdCompleter(args)
            });
            RegisterCommand(new ConsoleCommand {
                Name = "add",
                Usage = "add <item_id> [qty]", Help = "add item(s) to player inventory",
                Category = "inventory",
                Handler = args => CmdGive(args),
                Completer = args => ItemIdCompleter(args)
            });
            RegisterCommand(new ConsoleCommand {
                Name = "remove",
                Usage = "remove <item_id> [qty]", Help = "remove item(s) from player inventory",
                Category = "inventory",
                Handler = args => CmdRemove(args),
                Completer = args => ItemIdCompleter(args)
            });
            RegisterCommand(new ConsoleCommand {
                Name = "edit",
                Usage = "edit <item_id> <prop> <value>",
                Help = "[stub] ItemDefinition SO fields are immutable at runtime — use override system in a future update",
                Category = "inventory",
                Handler = args => CmdEditItem(args)
            });
            RegisterCommand(new ConsoleCommand {
                Name = "list", Aliases = new[] { "lsinv", "listitems" },
                Usage = "list", Help = "list all items in player inventory",
                Category = "inventory",
                Handler = _ => CmdListInventory()
            });

            // ── spells ────────────────────────────────────────────────────────
            RegisterCommand(new ConsoleCommand {
                Name = "spell",
                Usage = "spell <spell_key>", Help = "cast a spell from the player's spell book",
                Category = "spells",
                Handler = args => CmdSpell(args)
            });
            RegisterCommand(new ConsoleCommand {
                Name = "spells",
                Usage = "spells", Help = "list all spells registered to the player",
                Category = "spells",
                Handler = _ => CmdSpellList()
            });
            RegisterCommand(new ConsoleCommand {
                Name = "spellinfo",
                Usage = "spellinfo <key>", Help = "show full details for a spell",
                Category = "spells",
                Handler = args => CmdSpellInfo(args)
            });

            // ── spawning ──────────────────────────────────────────────────────
            RegisterCommand(new ConsoleCommand {
                Name = "spawn",
                Usage = "spawn <monster_key> [qty] [@cursor]",
                Help = "spawn monster(s) near the player, or at the mouse cursor with @cursor",
                Category = "spawning",
                Handler = args => CmdSpawn(args),
                Completer = args => MonsterKeyCompleter(args)
            });
            RegisterCommand(new ConsoleCommand {
                Name = "monsterinfo",
                Usage = "monsterinfo <key>", Help = "show full stats for a monster definition",
                Category = "spawning",
                Handler = args => CmdMonsterInfo(args),
                Completer = args => MonsterKeyCompleter(args)
            });

            // ── system ────────────────────────────────────────────────────────
            RegisterCommand(new ConsoleCommand {
                Name = "pause",
                Usage = "pause", Help = "pause game time (Time.timeScale = 0)",
                Category = "system",
                Handler = _ => CmdPause()
            });
            RegisterCommand(new ConsoleCommand {
                Name = "resume",
                Usage = "resume", Help = "resume game time (Time.timeScale = 1)",
                Category = "system",
                Handler = _ => CmdResume()
            });
            RegisterCommand(new ConsoleCommand {
                Name = "save",
                Usage = "save [name]", Help = "manual save to named slot (default: timestamp)",
                Category = "system",
                Handler = args => CmdSave(args)
            });
            RegisterCommand(new ConsoleCommand {
                Name = "load",
                Usage = "load [name]", Help = "[stub] load a named save — use the Load Game UI for full support",
                Category = "system",
                Handler = args => CmdLoad(args)
            });

            // Re-read authored data into the live scene — see DevConsole.Commands.Reload.cs.
            RegisterReloadCommands();
            RegisterSpawnerCommands();
            RegisterPlacedEntityCommands();
            RegisterAICommands();
            RegisterEditorCommands();

            // Placed-building rendering audit — see DevConsole.Commands.Buildings.cs.
            RegisterBuildingsCommands();

            // Weather levels, wind field and lightning — see DevConsole.Commands.Weather.cs.
            RegisterWeatherCommands();
            RegisterLookCommands();

            // The seven energy-charge auras — see DevConsole.Commands.Charge.cs.
            RegisterChargeCommands();

            // Stats, talents, grimoire — see DevConsole.Commands.Progression.cs.
            RegisterProgressionCommands();

            // The economic cycle and the purse — see DevConsole.Commands.Economy.cs.
            RegisterEconomyCommands();

            // Which provider answers NPC chat — see DevConsole.Commands.Chat.cs.
            RegisterChatCommands();

            // The death flow: phase, altars, the trails, the rescue clock and the litter. Every
            // one of those fails silently, which is why the subsystem needed a probe at all.
            RegisterDeathCommands();

            // The arranque's own probe — see DevConsole.Commands.Boot.cs.
            RegisterBootCommands();

            RegisterFaceCommands();

            // Doorway authoring — see DevConsole.Commands.Doors.cs. Registered LAST and in a
            // category of its own on purpose: CmdHelp only emits a category header when the
            // category changes while walking declaration order, so a new command dropped into
            // an existing category anywhere but its original block prints a duplicate header.
            RegisterDoorCommands();

            // Same rule as the doors above: its own category, registered in one
            // contiguous block, so CmdHelp prints exactly one "quests" header.
            RegisterQuestCommands();

            // The debug HUD (F1) — see DevConsole.Commands.DebugHud.cs. Its own category,
            // registered last, for the same one-header rule as the two blocks above.
            RegisterDebugHudCommands();
            RegisterSpellAreaCommands();
            // Gathering skills and woodcutting — see DevConsole.Commands.Harvest.cs.
            RegisterHarvestCommands();
        }

        // ------------------------------------------------------------------
        // Help command — grouped by Category
        // ------------------------------------------------------------------

        private void CmdHelp()
        {
            Log("=== DevConsole Help ===  (Tab=autocomplete  Up/Down=history)");
            string lastCat = null;
            foreach (var cmd in AllCommands)
            {
                if (cmd.Category != lastCat)
                {
                    Log($"--- {cmd.Category} ---");
                    lastCat = cmd.Category;
                }
                Log($"  {cmd.Usage,-36} {cmd.Help}");
            }
            Log("Type 'help <cmd>' for details on a single command.");
        }

    }
}
