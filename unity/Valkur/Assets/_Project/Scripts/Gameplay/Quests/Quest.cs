using System;
using System.Collections.Generic;

namespace Valkur.Gameplay.Quests
{
    /// <summary>
    /// Aggregator that bundles N <see cref="IObjective"/> instances into
    /// one player-facing goal ("Defeat the Wolfpack: kill 5 wolves AND
    /// kill the alpha"). The quest is complete when EVERY objective is
    /// complete (AND-semantics — OR/branching is a future feature).
    ///
    /// Lifecycle:
    ///   - <see cref="Begin"/> calls Begin on every child objective and
    ///     subscribes to their progress events.
    ///   - <see cref="End"/> calls End on every child and unsubscribes.
    ///     Idempotent in both directions.
    ///   - <see cref="OnObjectiveProgressed"/> fires whenever any child
    ///     objective ticks (UI repaint trigger).
    ///   - <see cref="OnCompleted"/> fires exactly once when the last
    ///     incomplete child becomes complete. Calling Begin again after
    ///     completion is a no-op — quests are one-shot.
    ///
    /// Ownership: this class does NOT own the objectives — the caller
    /// constructs them, hands them to the quest, and disposes them after.
    /// Tests can hand in mock objectives without spinning up real game
    /// events.
    /// </summary>
    public sealed class Quest
    {
        public string Id          { get; }
        public string DisplayName { get; }
        public IReadOnlyList<IObjective> Objectives { get; }

        public bool IsCompleted   { get; private set; }
        public bool IsActive      { get; private set; }

        public event Action<IObjective> OnObjectiveProgressed;
        public event Action OnCompleted;

        // Track which objectives we wired so Begin/End can be idempotent.
        private readonly Dictionary<IObjective, Action<int, int>> _kcHandlers
            = new Dictionary<IObjective, Action<int, int>>();

        public Quest(string id, string displayName, IList<IObjective> objectives)
        {
            Id          = id ?? string.Empty;
            DisplayName = displayName ?? string.Empty;
            Objectives  = objectives != null
                ? new List<IObjective>(objectives)
                : (IReadOnlyList<IObjective>)Array.Empty<IObjective>();
        }

        public void Begin()
        {
            if (IsActive || IsCompleted) return;
            IsActive = true;

            foreach (var obj in Objectives)
            {
                if (obj == null) continue;
                obj.Begin();

                // KillCountObjective exposes OnProgressChanged; subscribe
                // to it via duck-typing so the Quest layer doesn't have to
                // care which concrete IObjective is in the list.
                if (obj is KillCountObjective kc)
                {
                    Action<int, int> handler = (cur, tgt) =>
                    {
                        OnObjectiveProgressed?.Invoke(obj);
                        CheckCompletion();
                    };
                    kc.OnProgressChanged += handler;
                    _kcHandlers[obj] = handler;
                }
            }

            // A quest may be born "already complete" if every objective
            // started with Current >= Target (rare but possible for trivial
            // 0/0 placeholders). Check after Begin so the OnCompleted event
            // fires consistently.
            CheckCompletion();
        }

        public void End()
        {
            if (!IsActive) return;
            IsActive = false;

            foreach (var obj in Objectives)
            {
                if (obj == null) continue;
                if (obj is KillCountObjective kc &&
                    _kcHandlers.TryGetValue(obj, out var handler))
                {
                    kc.OnProgressChanged -= handler;
                }
                obj.End();
            }
            _kcHandlers.Clear();
        }

        /// <summary>Compute completion fraction across all objectives (0..1).</summary>
        public float OverallProgress
        {
            get
            {
                if (Objectives.Count == 0) return 1f;
                int completed = 0;
                foreach (var obj in Objectives)
                    if (obj != null && obj.IsComplete) completed++;
                return (float)completed / Objectives.Count;
            }
        }

        private void CheckCompletion()
        {
            if (IsCompleted) return;
            foreach (var obj in Objectives)
            {
                if (obj == null) continue;
                if (!obj.IsComplete) return;
            }
            IsCompleted = true;
            OnCompleted?.Invoke();

            // Auto-tear-down: a completed quest doesn't need to keep
            // listening. End() is idempotent so calling it here is safe
            // even if the caller End()s explicitly afterwards.
            End();
        }
    }
}
