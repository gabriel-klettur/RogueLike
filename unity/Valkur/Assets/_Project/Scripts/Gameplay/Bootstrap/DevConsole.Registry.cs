using System;
using System.Collections.Generic;
using UnityEngine;

namespace Valkur.Gameplay
{
    public partial class DevConsole
    {
        // ── ConsoleCommand ─────────────────────────────────────────────────────

        /// <summary>
        /// Describes a single console command including aliases, documentation,
        /// and an optional tab-completion function.
        /// </summary>
        public sealed class ConsoleCommand
        {
            /// <summary>Primary name (lowercase, no slash).</summary>
            public string Name;

            /// <summary>Optional alternate names (lowercase, no slash).</summary>
            public string[] Aliases;

            /// <summary>One-line usage string shown in help, e.g. "tp &lt;x&gt; &lt;y&gt;".</summary>
            public string Usage;

            /// <summary>Short description shown beside Usage in the help listing.</summary>
            public string Help;

            /// <summary>Logical grouping shown as section header in help output.</summary>
            public string Category;

            /// <summary>
            /// Invoked with the raw token array where args[0] = the command name used (may be
            /// the alias), args[1..] = remaining tokens.
            /// </summary>
            public Action<string[]> Handler;

            /// <summary>
            /// Optional auto-completer. Receives the current token array and returns
            /// candidate completions for the last token. May be null.
            /// </summary>
            public Func<string[], string[]> Completer;
        }

        // ── Registry storage ───────────────────────────────────────────────────

        private readonly Dictionary<string, ConsoleCommand> _commands =
            new Dictionary<string, ConsoleCommand>(StringComparer.OrdinalIgnoreCase);

        private readonly List<ConsoleCommand> _commandsOrdered = new List<ConsoleCommand>();

        // ── Registration API ──────────────────────────────────────────────────

        /// <summary>
        /// Register a command. The command is stored once in the ordered list
        /// (preserving declaration order for help output) and indexed under its
        /// Name and every Alias. Last write wins on conflicts — a warning is
        /// logged so duplicate registrations are visible during development.
        /// </summary>
        public void RegisterCommand(ConsoleCommand cmd)
        {
            if (cmd == null || string.IsNullOrWhiteSpace(cmd.Name)) return;

            // Only add to the ordered list once (by primary name).
            if (!_commands.ContainsKey(cmd.Name))
                _commandsOrdered.Add(cmd);

            Register(cmd.Name, cmd);
            if (cmd.Aliases != null)
            {
                foreach (var alias in cmd.Aliases)
                    if (!string.IsNullOrWhiteSpace(alias))
                        Register(alias, cmd);
            }
        }

        private void Register(string key, ConsoleCommand cmd)
        {
            if (_commands.TryGetValue(key, out var existing) && existing != cmd)
                Debug.LogWarning($"[DevConsole] Command conflict: '{key}' — overwriting '{existing.Name}' with '{cmd.Name}'.");
            _commands[key] = cmd;
        }

        // ── Lookup API ────────────────────────────────────────────────────────

        /// <summary>Returns true and sets <paramref name="cmd"/> when the name (or alias) is registered.</summary>
        public bool TryResolve(string name, out ConsoleCommand cmd) =>
            _commands.TryGetValue(name, out cmd);

        /// <summary>All registered commands in declaration order (primary names only).</summary>
        public IReadOnlyList<ConsoleCommand> AllCommands => _commandsOrdered;
    }
}
