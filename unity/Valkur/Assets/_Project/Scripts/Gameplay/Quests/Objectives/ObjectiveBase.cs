using System;
using UnityEngine;

namespace Valkur.Gameplay.Quests
{
    /// <summary>
    /// Shared skeleton for every objective: identity, the counter, the
    /// subscribe-once guard, and the ONE progress event the <see cref="Quest"/>
    /// aggregator listens to.
    ///
    /// <para><b>Why the event has to live here and not on each objective.</b>
    /// <c>Quest.Begin</c> used to duck-type exactly one concrete class —
    /// <c>KillCountObjective.OnProgressChanged</c> — and re-check completion from
    /// that handler. So a quest whose last objective was anything else would tick
    /// its counter, become complete, and <c>OnCompleted</c> would never fire: the
    /// quest stayed in the active list forever, rewards never paid, and nothing
    /// logged. It was invisible while KillCount was the only kind that existed,
    /// which is precisely why it survived. Every objective now reports through
    /// <see cref="Progressed"/>.</para>
    ///
    /// <para><see cref="IObjective"/> itself is deliberately NOT widened with that
    /// event. The interface is what test fixtures implement with three-line stubs,
    /// and a new member there breaks every one of them for no gain — the aggregator
    /// can ask <c>is ObjectiveBase</c> and treat anything else as a passive
    /// counter, which is exactly what a stub is.</para>
    ///
    /// <para>Two flavours of objective derive from this and they differ in ONE
    /// thing: an EVENT objective subscribes in <see cref="Begin"/> and is told when
    /// something happened; a POLLED objective answers <see cref="IsPollable"/> true
    /// and is asked, by <c>QuestManager</c>'s low-rate ticker, whether the world
    /// currently satisfies it. Collecting is polled on purpose — the pickup event
    /// carries a DISPLAY NAME rather than an itemId (<c>WorldPickup</c> passes
    /// <c>itemDefinition.displayName</c>), and it does not fire at all for an item
    /// that arrived by crafting, by trade or as a quest reward. Asking the bag what
    /// is in it cannot miss a source.</para>
    /// </summary>
    public abstract class ObjectiveBase : IObjective
    {
        public string Id          { get; }
        public string Description { get; }
        public int    Target      { get; }

        public int  Current    { get; protected set; }
        public virtual bool IsComplete => Current >= Target;

        /// <summary>Fires with this objective after any change to <see cref="Current"/>.</summary>
        public event Action<ObjectiveBase> Progressed;

        /// <summary>
        /// True when this objective is answered by looking at the world rather than
        /// by an event. <c>QuestManager</c> calls <see cref="Poll"/> on these a few
        /// times a second while the quest is active.
        /// </summary>
        public virtual bool IsPollable => false;

        protected bool Subscribed { get; private set; }

        protected ObjectiveBase(string id, string description, int target)
        {
            Id          = id ?? string.Empty;
            Description = description ?? string.Empty;
            Target      = Mathf.Max(1, target);
        }

        public void Begin()
        {
            if (Subscribed) return;
            Subscribed = true;
            OnBegin();
        }

        public void End()
        {
            if (!Subscribed) return;
            Subscribed = false;
            OnEnd();
        }

        /// <summary>Subscribe to whatever this objective watches. Called once.</summary>
        protected virtual void OnBegin() { }

        /// <summary>Unsubscribe. Called once, and only after a matching OnBegin.</summary>
        protected virtual void OnEnd() { }

        /// <summary>
        /// Re-read the world and update <see cref="Current"/>. Only called when
        /// <see cref="IsPollable"/> is true. Must be cheap and must be safe to call
        /// when the player does not exist yet — the boot sequence restores quests
        /// before the world has finished coming up.
        /// </summary>
        public virtual void Poll() { }

        /// <summary>
        /// Raise <see cref="Current"/> by one and report. Clamped at
        /// <see cref="Target"/> so an event that arrives after completion cannot
        /// push the counter past what the log displays.
        /// </summary>
        protected void Increment()
        {
            if (Current >= Target) return;
            Current++;
            Progressed?.Invoke(this);
        }

        /// <summary>
        /// Set <see cref="Current"/> to an absolute value and report if it moved.
        /// This is the polled objectives' entry point — a bag can lose items, so
        /// their counter has to be able to go DOWN, which <see cref="Increment"/>
        /// cannot express.
        /// </summary>
        protected void SetCurrent(int value)
        {
            int clamped = Mathf.Clamp(value, 0, Target);
            if (clamped == Current) return;
            Current = clamped;
            Progressed?.Invoke(this);
        }

        /// <summary>
        /// Set the counter from an AUTHORING tool, reporting it exactly as the game would.
        ///
        /// <para>The Quests editor needs to drive an objective to any value to see what a
        /// quest does at each step, and it must do so through the SAME event the world
        /// raises: <see cref="RestoreProgress"/> is silent by design, so an editor built on
        /// it could fill every counter and leave the quest sitting there unfinished, which
        /// would be a second definition of "complete" that disagrees with the first. Going
        /// through <see cref="SetCurrent"/> means the aggregator notices, rewards are paid by
        /// the one path that pays them, and the tracker redraws.</para>
        ///
        /// <para>Public because <c>SetCurrent</c> is protected — this is the seam, and giving
        /// it its own name keeps "the world moved this" and "an author moved this" legible at
        /// every call site.</para>
        /// </summary>
        public void ForceProgress(int value) => SetCurrent(value);

        /// <summary>
        /// Seed the counter from a save with no event. Used by the persistence path
        /// only: replaying real kills is impossible, and re-firing
        /// <see cref="Progressed"/> during a restore would complete the quest and
        /// pay it out a second time.
        /// </summary>
        public void RestoreProgress(int current)
        {
            Current = Mathf.Clamp(current, 0, Target);
        }
    }
}
