using System;
using System.Collections.Generic;
using UnityEngine;
using Valkur.Data;

namespace Valkur.Gameplay.Save
{
    /// <summary>
    /// Chained schema migration for save files.
    /// Mirrors Python's Alembic migrations: a registry of (from -> to, upgrade) steps that
    /// run sequentially until the save reaches the current schema.
    ///
    /// Usage inside a bootstrap type (static ctor):
    ///   SaveMigrationChain.Register("1.0", "1.1", data =&gt; { /* upgrade */ });
    ///   SaveMigrationChain.Register("1.1", "1.2", data =&gt; { /* upgrade */ });
    /// </summary>
    public static class SaveMigrationChain
    {
        public readonly struct Step
        {
            public readonly string From;
            public readonly string To;
            public readonly Action<GameSaveData> Upgrade;
            public Step(string from, string to, Action<GameSaveData> upgrade)
            { From = from; To = to; Upgrade = upgrade; }
        }

        private static readonly List<Step> _steps = new List<Step>();

        /// <summary>Registers a single-step migration (from -&gt; to).</summary>
        public static void Register(string from, string to, Action<GameSaveData> upgrade)
        {
            if (string.IsNullOrEmpty(from) || string.IsNullOrEmpty(to) || upgrade == null) return;
            if (from == to) return;
            foreach (var s in _steps)
                if (s.From == from && s.To == to) return; // already registered
            _steps.Add(new Step(from, to, upgrade));
        }

        /// <summary>Clears the registry. Primarily for tests.</summary>
        public static void Clear() => _steps.Clear();

        public static IReadOnlyList<Step> AllSteps => _steps;

        /// <summary>
        /// Runs chained migrations until <paramref name="data"/>.schemaVersion equals <paramref name="target"/>.
        /// Returns the number of migration steps applied. If no path exists, logs a warning and returns 0.
        /// </summary>
        public static int MigrateTo(GameSaveData data, string target)
        {
            if (data == null || string.IsNullOrEmpty(target)) return 0;
            string cur = string.IsNullOrEmpty(data.schemaVersion) ? "1.0" : data.schemaVersion;
            int applied = 0;
            int safety = 32;
            while (cur != target && safety-- > 0)
            {
                var next = FindNext(cur);
                if (next.HasValue)
                {
                    try { next.Value.Upgrade(data); }
                    catch (Exception ex) { Debug.LogError($"[SaveMigrationChain] step {next.Value.From}->{next.Value.To} failed: {ex.Message}"); return applied; }
                    data.schemaVersion = next.Value.To;
                    cur = next.Value.To;
                    applied++;
                }
                else
                {
                    Debug.LogWarning($"[SaveMigrationChain] no migration path from '{cur}' (target '{target}').");
                    break;
                }
            }
            return applied;
        }

        private static Step? FindNext(string from)
        {
            foreach (var s in _steps)
                if (s.From == from) return s;
            return null;
        }
    }
}
