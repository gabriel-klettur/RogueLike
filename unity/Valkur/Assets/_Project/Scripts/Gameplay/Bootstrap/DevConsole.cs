using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using Valkur.Core;
using Valkur.Core.Input;
using Valkur.Gameplay.Spells;
using Valkur.Gameplay.World;

namespace Valkur.Gameplay
{
    /// <summary>
    /// In-game developer console. Toggle open/close with backtick (~) or F4.
    /// Draws a compact IMGUI overlay at the bottom of the screen; no Canvas setup required.
    /// 
    /// Supported commands:
    ///   help                  - list available commands
    ///   godmode               - toggle player invincibility
    ///   heal                  - restore player to full HP and MP
    ///   tp  &lt;x&gt; &lt;y&gt;          - teleport player to world position
    ///   time &lt;0..1&gt;           - set day/night cycle time
    ///   killall               - kill all active enemies
    ///   give &lt;item_id&gt; [qty]  - add item(s) to player inventory
    ///   spawn &lt;monster_key&gt;   - spawn monster near player
    ///   clear                 - clear the log
    /// </summary>
    public partial class DevConsole : SingletonMonoBehaviour<DevConsole>
    {
        private const float CONSOLE_WIDTH = 640f;
        private const float CONSOLE_HEIGHT = 280f;
        private const int LOG_MAX_LINES = 80;

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

        // ------------------------------------------------------------------
        // Lifecycle
        // ------------------------------------------------------------------

        protected override void OnSingletonAwake()
        {
            _toggleAction = EditorHotkeyBindings.Resolve(
                EditorHotkeyBindings.Hotkey.ToggleDevConsole, out _ownsToggleAction);
        }

        protected override void OnDestroy()
        {
            if (_ownsToggleAction) { _toggleAction?.Disable(); _toggleAction?.Dispose(); }
            base.OnDestroy();
        }

        private void Update()
        {
            if (_toggleAction != null && _toggleAction.WasPerformedThisFrame())
            {
                _open = !_open;
                if (_open) _focusInput = true;
            }
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

            GUI.Box(new Rect(x - 4f, y - 4f, CONSOLE_WIDTH + 8f, CONSOLE_HEIGHT + 8f), "", _boxStyle);

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
            if (_focusInput)
            {
                GUI.FocusControl("ConsoleInput");
                _focusInput = false;
            }
            GUI.SetNextControlName("ConsoleInput");
            _inputBuffer = GUI.TextField(new Rect(x, inputY, CONSOLE_WIDTH - 60f, 24f), _inputBuffer, _inputStyle);

            if (GUI.Button(new Rect(x + CONSOLE_WIDTH - 56f, inputY, 56f, 24f), "Submit") ||
                (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Return &&
                 GUI.GetNameOfFocusedControl() == "ConsoleInput"))
            {
                if (!string.IsNullOrWhiteSpace(_inputBuffer))
                {
                    ExecuteCommand(_inputBuffer.Trim());
                    _inputBuffer = "";
                    _logScroll.y = float.MaxValue;
                    _focusInput = true;
                }
            }
        }

        // ------------------------------------------------------------------
        // Command Execution
        // ------------------------------------------------------------------

        private void ExecuteCommand(string raw)
        {
            Log($"> {raw}");
            var parts = raw.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return;

            switch (parts[0].ToLowerInvariant())
            {
                case "help":     CmdHelp(); break;
                case "clear":    _log.Clear(); break;
                case "godmode":  CmdGodMode(); break;
                case "heal":     CmdHeal(); break;
                case "tp":       CmdTeleport(parts); break;
                case "time":     CmdSetTime(parts); break;
                case "killall":  CmdKillAll(); break;
                case "give":     CmdGive(parts); break;
                case "spawn":    CmdSpawn(parts); break;
                case "spell":    CmdSpell(parts); break;
                case "spells":   CmdSpellList(); break;
                case "spellinfo":CmdSpellInfo(parts); break;
                default:
                    Log($"Unknown command: '{parts[0]}'. Type 'help' for a list.");
                    break;
            }
        }

        private void CmdHelp()
        {
            Log("Commands:");
            Log("  godmode             - toggle invincibility");
            Log("  heal                - restore HP/MP");
            Log("  tp <x> <y>          - teleport");
            Log("  time <0..1>         - set day/night time");
            Log("  killall             - kill all enemies");
            Log("  give <item_id> [n]  - add item to inventory");
            Log("  spawn <monster_key> - spawn monster nearby");
            Log("  spell <spell_key>   - cast a spell");
            Log("  spells              - list all registered spells");
            Log("  spellinfo <key>     - show spell details");
            Log("  clear               - clear log");
        }

    }
}
