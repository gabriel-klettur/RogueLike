using System;
using System.Collections.Generic;
using System.Text;
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
        private const float CONSOLE_WIDTH = 640f;
        private const float CONSOLE_HEIGHT = 280f;
        private const int LOG_MAX_LINES = 80;

        // ── Command history ────────────────────────────────────────────────────
        private readonly List<string> _commandHistory = new List<string>();
        private int _historyCursor = -1;
        private const int MAX_HISTORY = 50;

        private bool _open;
        private string _inputBuffer = "";
        private readonly List<string> _log = new List<string>();
        private Vector2 _logScroll;
        private bool _focusInput;

        private InputAction _toggleAction;
        private bool _ownsToggleAction;
        private GUIStyle _boxStyle;
        private GUIStyle _labelStyle;
        private GUIStyle _inputStyle;
        private bool _stylesBuilt;
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

            RegisterDefaults();
        }

        protected override void OnDestroy()
        {
            if (_ownsToggleAction) { _toggleAction?.Disable(); _toggleAction?.Dispose(); }
            base.OnDestroy();
        }

        private void Update()
        {
            if (EditorHotkeyBindings.WasPerformedThisFrame(EditorHotkeyBindings.Hotkey.ToggleDevConsole))
            {
                bool wasOpen = _open;
                _open = !_open;
                if (_open)
                {
                    _focusInput = true;
                    if (!wasOpen) OnOpened?.Invoke();
                }
                else
                {
                    OnClosed?.Invoke();
                }
            }

            if (!_open) return;

            // Arrow history navigation — handled in Update so IMGUI event timing
            // does not steal the key from the text field on the wrong frame.
            if (KeyboardInputManager.WasArrowUpPressedThisFrame())
            {
                if (_commandHistory.Count > 0)
                {
                    _historyCursor = Mathf.Min(_historyCursor + 1, _commandHistory.Count - 1);
                    _inputBuffer = _commandHistory[_commandHistory.Count - 1 - _historyCursor];
                    _focusInput = true;
                }
            }
            else if (KeyboardInputManager.WasArrowDownPressedThisFrame())
            {
                if (_historyCursor > 0)
                {
                    _historyCursor--;
                    _inputBuffer = _commandHistory[_commandHistory.Count - 1 - _historyCursor];
                }
                else
                {
                    _historyCursor = -1;
                    _inputBuffer = "";
                }
                _focusInput = true;
            }

            // Tab autocomplete
            if (KeyboardInputManager.WasTabPressedThisFrame() &&
                !string.IsNullOrWhiteSpace(_inputBuffer))
            {
                _inputBuffer = TryAutocomplete(_inputBuffer);
                _focusInput = true;
            }

            // Enter submits — executed directly here in Update() because IMGUI's
            // TextField was eating the Return event in OnGUI even with the
            // pending-flag indirection (Repaint pass never fired the consumer
            // when the user expected). Same pattern Up/Down/Tab use above:
            // detect via KeyboardInputManager (which polls both InputSystem
            // backends) and mutate state immediately, before OnGUI runs.
            // Enter submits — primary path, runs before any IMGUI control sees
            // the key. OnGUI has a fallback path (intercept on KeyDown event)
            // for cases where the InputSystem helper missed the press.
            if (KeyboardInputManager.WasEnterPressedThisFrame())
            {
                SubmitInputBuffer();
            }
        }

        // Frame-guard to keep SubmitInputBuffer idempotent within a frame —
        // both Update() and OnGUI's KeyDown intercept can call it on the same
        // press; without this guard we'd execute the command twice.
        private int _lastSubmitFrame = -1;

        private void SubmitInputBuffer()
        {
            if (_lastSubmitFrame == Time.frameCount) return;
            _lastSubmitFrame = Time.frameCount;

            if (!string.IsNullOrWhiteSpace(_inputBuffer))
            {
                ExecuteCommand(_inputBuffer.Trim());
            }
            _inputBuffer = "";
            _logScroll.y = float.MaxValue;
            _focusInput = true;
        }

        // ------------------------------------------------------------------
        // IMGUI
        // ------------------------------------------------------------------

        private void OnGUI()
        {
            if (!_open) return;

            EnsureStyles();

            float x = (Screen.width - CONSOLE_WIDTH) * 0.5f;
            float y = Screen.height - CONSOLE_HEIGHT - 8f;
            var consoleRect = new Rect(x - 4f, y - 4f, CONSOLE_WIDTH + 8f, CONSOLE_HEIGHT + 8f);

            // Modal click-outside: if the user clicks outside the console box,
            // close it. Must run BEFORE the GUI controls so it doesn't consume
            // events the input field / submit button rely on.
            if (Event.current.type == EventType.MouseDown &&
                !consoleRect.Contains(Event.current.mousePosition))
            {
                _open = false;
                OnClosed?.Invoke();
                Event.current.Use();
                return;
            }

            GUI.Box(consoleRect, "", _boxStyle);

            // Log area
            float logH = CONSOLE_HEIGHT - 32f;
            _logScroll = GUI.BeginScrollView(
                new Rect(x, y, CONSOLE_WIDTH, logH),
                _logScroll,
                new Rect(0f, 0f, CONSOLE_WIDTH - 16f, Mathf.Max(logH, _log.Count * 16f)));

            var sb = new StringBuilder();
            for (int i = 0; i < _log.Count; i++)
                sb.AppendLine(_log[i]);

            GUI.Label(new Rect(4f, 4f, CONSOLE_WIDTH - 20f, Mathf.Max(logH, _log.Count * 16f)), sb.ToString(), _labelStyle);
            GUI.EndScrollView();

            // Input field
            float inputY = y + logH + 4f;

            // Fallback Enter intercept: if the InputSystem helper in Update()
            // missed the press (Editor InputSystem hiccup, focus race, etc.),
            // the IMGUI KeyDown event is still delivered here. Consume it
            // BEFORE the TextField is drawn so the TextField doesn't process
            // it first and swallow the event. SubmitInputBuffer is idempotent
            // per frame, so calling it from both paths is safe.
            bool enterFromImgui = false;
            if (Event.current.type == EventType.KeyDown &&
                (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter))
            {
                enterFromImgui = true;
                Event.current.Use();
            }

            GUI.SetNextControlName("ConsoleInput");
            _inputBuffer = GUI.TextField(new Rect(x, inputY, CONSOLE_WIDTH - 60f, 24f), _inputBuffer, _inputStyle);

            if (enterFromImgui) SubmitInputBuffer();

            // IMGUI focus must be requested AFTER the named control is laid out —
            // calling GUI.FocusControl before the TextField is registered is a
            // no-op. Keep the flag set until focus actually lands so the first
            // OnGUI pass after open (which runs through Layout/Repaint events)
            // is guaranteed to land focus by the next pass at the latest.
            if (_focusInput)
            {
                GUI.FocusControl("ConsoleInput");
                if (Event.current.type == EventType.Repaint &&
                    GUI.GetNameOfFocusedControl() == "ConsoleInput")
                {
                    _focusInput = false;
                }
            }

            // Submit button — Enter-key submission is handled in Update() above
            // (see SubmitInputBuffer). This branch only covers a mouse click.
            if (GUI.Button(new Rect(x + CONSOLE_WIDTH - 56f, inputY, 56f, 24f), "Submit"))
            {
                SubmitInputBuffer();
            }
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
            Log($"> {raw}");
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
        // Tab autocomplete
        // ------------------------------------------------------------------

        private string TryAutocomplete(string input)
        {
            var tokens = input.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 0) return input;

            // Completing the command name itself (first token, no space after it yet).
            bool completingCommand = tokens.Length == 1 && !input.EndsWith(" ");
            if (completingCommand)
            {
                string prefix = tokens[0].TrimStart('/');
                var matches = new List<string>();
                foreach (var kv in _commands)
                    if (kv.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                        matches.Add(kv.Value.Name); // use canonical name
                matches.Sort(StringComparer.OrdinalIgnoreCase);
                // Deduplicate (aliases map to the same Name).
                var deduped = new List<string>();
                for (int i = 0; i < matches.Count; i++)
                    if (deduped.Count == 0 || deduped[deduped.Count - 1] != matches[i])
                        deduped.Add(matches[i]);

                if (deduped.Count == 1) return deduped[0];
                if (deduped.Count > 1)
                {
                    Log($"  Matches: {string.Join(", ", deduped)}");
                    // Return the longest common prefix.
                    return LongestCommonPrefix(deduped);
                }
                return input;
            }

            // Completing an argument — delegate to the resolved command's Completer.
            string cmdName = tokens[0].TrimStart('/');
            if (!TryResolve(cmdName, out var cmd) || cmd.Completer == null) return input;

            var argMatches = cmd.Completer(tokens);
            if (argMatches == null || argMatches.Length == 0) return input;
            if (argMatches.Length == 1)
            {
                // Replace the last token with the match.
                var rebuilt = new StringBuilder();
                for (int i = 0; i < tokens.Length - 1; i++)
                    rebuilt.Append(tokens[i]).Append(' ');
                rebuilt.Append(argMatches[0]);
                return rebuilt.ToString();
            }
            Log($"  Matches: {string.Join(", ", argMatches)}");
            return input;
        }

        private static string LongestCommonPrefix(List<string> words)
        {
            if (words.Count == 0) return "";
            string first = words[0];
            int len = first.Length;
            for (int i = 1; i < words.Count; i++)
            {
                len = Mathf.Min(len, words[i].Length);
                for (int c = 0; c < len; c++)
                    if (char.ToLower(first[c]) != char.ToLower(words[i][c]))
                    { len = c; break; }
            }
            return first.Substring(0, len);
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
                Handler = _ => _log.Clear()
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

            // Weather levels, wind field and lightning — see DevConsole.Commands.Weather.cs.
            RegisterWeatherCommands();

            // The seven energy-charge auras — see DevConsole.Commands.Charge.cs.
            RegisterChargeCommands();

            // Stats, talents, grimoire — see DevConsole.Commands.Progression.cs.
            RegisterProgressionCommands();

            // Which provider answers NPC chat — see DevConsole.Commands.Chat.cs.
            RegisterChatCommands();
            RegisterFaceCommands();

            // Doorway authoring — see DevConsole.Commands.Doors.cs. Registered LAST and in a
            // category of its own on purpose: CmdHelp only emits a category header when the
            // category changes while walking declaration order, so a new command dropped into
            // an existing category anywhere but its original block prints a duplicate header.
            RegisterDoorCommands();
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
