using System;
using System.Collections.Generic;
using UnityEngine;

namespace Valkur.Infrastructure.Migrations
{
    /// <summary>
    /// Generic, document-typed schema migration chain. Generalizes the pattern
    /// already used by <c>SaveMigrationChain</c> for <c>GameSaveData</c> so any
    /// versioned persistence type can opt in: chunk records, world descriptors,
    /// catalog overlays, mod state, etc.
    ///
    /// Usage:
    /// <code>
    /// var chain = new MigrationChain&lt;ZonePersistenceFile&gt;("1.2");
    /// chain.Register("1.0", "1.1", doc =&gt; { /* upgrade */ });
    /// chain.Register("1.1", "1.2", doc =&gt; { /* upgrade */ });
    /// chain.Migrate(loadedDoc);
    /// </code>
    ///
    /// Failure semantics: a thrown step logs the failure and stops. Callers
    /// should treat that as "unmigratable" and surface to the user (or fall
    /// back to a backup file).
    /// </summary>
    public sealed class MigrationChain<T> where T : class, IVersioned
    {
        public readonly struct Step
        {
            public readonly string From;
            public readonly string To;
            public readonly Action<T> Upgrade;
            public Step(string from, string to, Action<T> upgrade)
            { From = from; To = to; Upgrade = upgrade; }
        }

        private readonly List<Step> _steps = new List<Step>();
        private readonly string _typeName;

        public string CurrentVersion { get; }
        public IReadOnlyList<Step> AllSteps => _steps;

        public MigrationChain(string currentVersion)
        {
            if (string.IsNullOrEmpty(currentVersion))
                throw new ArgumentException("currentVersion must be set", nameof(currentVersion));
            CurrentVersion = currentVersion;
            _typeName = typeof(T).Name;
        }

        public MigrationChain<T> Register(string from, string to, Action<T> upgrade)
        {
            if (string.IsNullOrEmpty(from) || string.IsNullOrEmpty(to) || upgrade == null) return this;
            if (from == to) return this;
            foreach (var s in _steps)
                if (s.From == from && s.To == to) return this; // already registered
            _steps.Add(new Step(from, to, upgrade));
            return this;
        }

        public void Clear() => _steps.Clear();

        /// <summary>
        /// Migrates <paramref name="doc"/> to <see cref="CurrentVersion"/> by
        /// walking registered steps. Returns the number of steps applied.
        /// If <paramref name="doc"/> is already at the current version,
        /// returns 0 without touching it. Forces the version tag if the
        /// chain cannot reach the target (with a warning).
        /// </summary>
        public int Migrate(T doc)
        {
            if (doc == null) return 0;
            string from = string.IsNullOrEmpty(doc.SchemaVersion)
                ? FindLowestFromVersion()
                : doc.SchemaVersion;
            if (from == CurrentVersion) return 0;

            string cur = from;
            int applied = 0;
            int safety = 64;
            while (cur != CurrentVersion && safety-- > 0)
            {
                var next = FindNext(cur);
                if (!next.HasValue)
                {
                    Debug.LogWarning(
                        $"[MigrationChain<{_typeName}>] No migration path from '{cur}' to '{CurrentVersion}'. " +
                        $"Forcing version tag to '{CurrentVersion}' to avoid permanent stuck state.");
                    doc.SchemaVersion = CurrentVersion;
                    return applied;
                }

                try { next.Value.Upgrade(doc); }
                catch (Exception ex)
                {
                    Debug.LogError(
                        $"[MigrationChain<{_typeName}>] Step {next.Value.From} -> {next.Value.To} failed: {ex.Message}");
                    return applied;
                }
                doc.SchemaVersion = next.Value.To;
                cur = next.Value.To;
                applied++;
            }

            if (applied > 0)
                Debug.Log($"[MigrationChain<{_typeName}>] Migrated '{from}' -> '{CurrentVersion}' in {applied} step(s).");
            return applied;
        }

        private Step? FindNext(string from)
        {
            for (int i = 0; i < _steps.Count; i++)
                if (_steps[i].From == from) return _steps[i];
            return null;
        }

        private string FindLowestFromVersion()
        {
            // Best effort: pick whichever 'From' has no predecessor as the chain root.
            // If there are no steps, fall back to current (no migration runs).
            if (_steps.Count == 0) return CurrentVersion;
            var froms = new HashSet<string>();
            var tos   = new HashSet<string>();
            foreach (var s in _steps) { froms.Add(s.From); tos.Add(s.To); }
            foreach (var f in froms)
                if (!tos.Contains(f)) return f;
            return _steps[0].From;
        }
    }
}
