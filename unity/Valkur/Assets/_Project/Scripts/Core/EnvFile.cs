using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Valkur.Core
{
    /// <summary>
    /// Reads secrets from the process environment, falling back to a <c>.env</c> file at the
    /// repository root.
    ///
    /// <para>WHY A FILE AT ALL. Unity's Editor does not inherit a shell's exported
    /// variables in any reliable way — it is launched from Hub, from an IDE, or from a
    /// desktop shortcut, and which of those carries your environment is not something a
    /// project can depend on. A <c>.env</c> beside the repository is how every other tool in
    /// this project's toolchain is configured, and it is in <c>.gitignore</c>.</para>
    ///
    /// <para>RULES THIS CLASS ENFORCES, because a leaked key is not recoverable by editing
    /// code:</para>
    /// <list type="bullet">
    ///   <item>A value is NEVER logged, not even truncated. Diagnostics say whether a name
    ///   resolved, never to what.</item>
    ///   <item>Values are never written anywhere — not to <c>PlayerPrefs</c>, not to a
    ///   ScriptableObject, not to a save file. They live in memory for the session.</item>
    ///   <item>The file is read from the REPOSITORY, which never ships: a built player has
    ///   no <c>.env</c> beside it and <see cref="TryGet"/> simply answers false, so a
    ///   feature gated on a key degrades instead of failing.</item>
    /// </list>
    ///
    /// <para>Domain Reload is OFF, so the cache is cleared from a
    /// <c>SubsystemRegistration</c> hook — otherwise an edited <c>.env</c> would not be
    /// picked up until the Editor restarted.</para>
    /// </summary>
    public static class EnvFile
    {
        /// <summary>Parsed contents of the <c>.env</c>, or null until first use.</summary>
        private static Dictionary<string, string> _fileValues;

        /// <summary>
        /// The value of <paramref name="name"/>, from the process environment first and the
        /// <c>.env</c> file second. False when it is absent or blank everywhere.
        ///
        /// The environment wins so a CI runner or a shell that really did export the
        /// variable is never overridden by a stale file.
        /// </summary>
        public static bool TryGet(string name, out string value)
        {
            value = null;
            if (string.IsNullOrWhiteSpace(name)) return false;

            string fromEnvironment = SafeEnvironmentVariable(name);
            if (!string.IsNullOrWhiteSpace(fromEnvironment))
            {
                value = fromEnvironment.Trim();
                return true;
            }

            EnsureFileLoaded();
            if (_fileValues.TryGetValue(name, out string fromFile) && !string.IsNullOrWhiteSpace(fromFile))
            {
                value = fromFile;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Whether <paramref name="name"/> resolves to something. Safe to log — it says a
        /// name was found, never what it was found to be.
        /// </summary>
        public static bool Has(string name) => TryGet(name, out _);

        /// <summary>
        /// Absolute path of the <c>.env</c> this reads, whether or not it exists.
        /// <c>Application.dataPath</c> ends at <c>&lt;repo&gt;/unity/Valkur/Assets</c>, so
        /// the repository root is three levels up.
        /// </summary>
        public static string ResolvePath()
        {
            try
            {
                var dir = new DirectoryInfo(Application.dataPath);
                for (int i = 0; i < 3 && dir?.Parent != null; i++) dir = dir.Parent;
                return Path.Combine(dir?.FullName ?? Application.dataPath, ".env").Replace('\\', '/');
            }
            catch (Exception)
            {
                // Application.dataPath throws off the main thread in some Unity versions.
                return null;
            }
        }

        // ── Parsing ─────────────────────────────────────────────────────────

        private static void EnsureFileLoaded()
        {
            if (_fileValues != null) return;
            _fileValues = new Dictionary<string, string>(StringComparer.Ordinal);

            string path = ResolvePath();
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;

            try
            {
                // utf-8-sig: a BOM from a Windows editor would otherwise become part of the
                // FIRST variable's name, so exactly one key silently fails to resolve.
                foreach (string raw in File.ReadAllLines(path, new System.Text.UTF8Encoding(false)))
                    ParseLine(raw.TrimStart('﻿'));
            }
            catch (Exception ex)
            {
                // The message may name the path; it can never contain a value, because
                // nothing has been parsed yet at the point a read can fail.
                Debug.LogWarning($"[EnvFile] Could not read '{path}': {ex.GetType().Name}");
            }
        }

        private static void ParseLine(string raw)
        {
            string line = raw.Trim();
            if (line.Length == 0 || line[0] == '#') return;

            int equals = line.IndexOf('=');
            if (equals <= 0) return;

            string key = line.Substring(0, equals).Trim();
            if (key.StartsWith("export ", StringComparison.Ordinal))
                key = key.Substring("export ".Length).Trim();
            if (key.Length == 0) return;

            string value = line.Substring(equals + 1).Trim();

            // Strip one matched pair of surrounding quotes. A key pasted from a dashboard
            // often arrives quoted, and sending the quotes along produces a 401 that looks
            // like a wrong key rather than a formatting slip.
            if (value.Length >= 2 &&
                ((value[0] == '"' && value[value.Length - 1] == '"') ||
                 (value[0] == '\'' && value[value.Length - 1] == '\'')))
                value = value.Substring(1, value.Length - 2);

            _fileValues[key] = value;
        }

        private static string SafeEnvironmentVariable(string name)
        {
            try { return Environment.GetEnvironmentVariable(name); }
            catch (Exception) { return null; }
        }

        // ── Test seam ───────────────────────────────────────────────────────

        /// <summary>
        /// Replaces the parsed file contents. Tests only — production always reads the file.
        /// Passing null restores lazy loading from disk.
        /// </summary>
        internal static void OverrideFileValuesForTests(Dictionary<string, string> values)
        {
            _fileValues = values;
        }

        // ── Domain-Reload reset ─────────────────────────────────────────────

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticState()
        {
            // Assigning a fresh reference rather than clearing in place: the ratchet in
            // DomainReloadStaticResetTests reads raw IL and only recognises stsfld or
            // field.Clear(), and null here also restores lazy re-reading of an edited file.
            _fileValues = null;
        }
    }
}
