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
        //
        // Keyed on ObjectiveBase rather than IObjective because that is the type
        // carrying the progress event. A plain IObjective (test stubs) is still a
        // legal member of a quest — it simply reports nothing, and the quest
        // re-checks it whenever any of its siblings ticks.
        private readonly Dictionary<ObjectiveBase, Action<ObjectiveBase>> _handlers
            = new Dictionary<ObjectiveBase, Action<ObjectiveBase>>();

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

                // Every shipped objective derives from ObjectiveBase and reports
                // through ONE event. This used to duck-type KillCountObjective
                // alone, which was invisible while that was the only kind in
                // existence and became a silent hole the moment a second one
                // arrived: a quest whose last objective was a Collect or a Survive
                // would go complete without OnCompleted ever firing, so it stayed
                // active forever and never paid out.
                if (obj is ObjectiveBase ob)
                {
                    Action<ObjectiveBase> handler = _ =>
                    {
                        OnObjectiveProgressed?.Invoke(obj);
                        CheckCompletion();
                    };
                    ob.Progressed += handler;
                    _handlers[ob] = handler;
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
                if (obj is ObjectiveBase ob &&
                    _handlers.TryGetValue(ob, out var handler))
                {
                    ob.Progressed -= handler;
                }
                obj.End();
            }
            _handlers.Clear();
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

        /// <summary>
        /// Ask every polled objective to re-read the world. Driven by
        /// <c>QuestManager</c> at a low rate — a bag count, a purse balance and a
        /// level are facts that change a few times a minute, not a few times a
        /// frame, and the one time-based objective accumulates <c>deltaTime</c>
        /// itself rather than caring how often it is asked.
        ///
        /// <para>No-op once the quest is done, so a completed quest that has not yet
        /// been cleaned up costs nothing.</para>
        /// </summary>
        public void Poll()
        {
            if (!IsActive || IsCompleted) return;
            for (int i = 0; i < Objectives.Count; i++)
            {
                if (Objectives[i] is ObjectiveBase ob && ob.IsPollable) ob.Poll();
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
