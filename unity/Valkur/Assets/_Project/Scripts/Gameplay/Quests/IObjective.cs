namespace Valkur.Gameplay.Quests
{
    /// <summary>
    /// Single-objective contract for the quest system. Each objective tracks
    /// its own progress (current vs target) and decides when it's complete.
    /// Aggregated by a future <c>Quest</c> type which combines N objectives
    /// into a player-facing goal.
    ///
    /// Objectives subscribe to game events on <see cref="Begin"/> and
    /// unsubscribe on <see cref="End"/>; the quest manager is responsible
    /// for the lifecycle.
    /// </summary>
    public interface IObjective
    {
        /// <summary>Stable id for serialization / quest log lookups.</summary>
        string Id { get; }

        /// <summary>Player-facing description ("Kill 10 wolves").</summary>
        string Description { get; }

        /// <summary>Progress so far (e.g. wolves killed).</summary>
        int Current { get; }

        /// <summary>Target value the objective needs to reach (e.g. 10).</summary>
        int Target { get; }

        /// <summary>True once Current >= Target.</summary>
        bool IsComplete { get; }

        /// <summary>Subscribe to relevant events. Idempotent — calling Begin
        /// twice without End must not double-subscribe.</summary>
        void Begin();

        /// <summary>Unsubscribe and stop tracking. Safe to call before Begin.</summary>
        void End();
    }
}
