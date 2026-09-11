using System;
using Valkur.Core;

namespace Valkur.Gameplay.Quests
{
    /// <summary>
    /// "Go and speak to X." Ticks on <c>GameEvents.OnNpcConversed</c>, which fires
    /// once per conversation OPENED rather than per message — a per-message event
    /// would complete this by chatting with somebody the player was already stood
    /// in front of.
    ///
    /// <para>The <see cref="Gate"/> is what makes a TURN-IN possible.
    /// <c>QuestManager</c> appends one of these when a quest declares a
    /// <c>turnInPersonaId</c>, with a gate that answers "is every authored
    /// objective already done". Without it, the conversation that HANDS THE QUEST
    /// OVER would close it on the spot: the giver and the turn-in are usually the
    /// same character, and the objective would be born complete.</para>
    ///
    /// <para>A null gate means always open, which is what a hand-authored Talk
    /// objective ("find the hermit") wants.</para>
    /// </summary>
    public sealed class TalkObjective : ObjectiveBase
    {
        public string PersonaId { get; }

        /// <summary>Extra condition that must hold for a conversation to count.</summary>
        public Func<bool> Gate { get; }

        public TalkObjective(string id, string description, string personaId, Func<bool> gate = null)
            : base(id, description, 1)
        {
            PersonaId = personaId ?? string.Empty;
            Gate      = gate;
        }

        protected override void OnBegin() => GameEvents.OnNpcConversed += HandleConversed;
        protected override void OnEnd()   => GameEvents.OnNpcConversed -= HandleConversed;

        private void HandleConversed(string personaId)
        {
            if (IsComplete || string.IsNullOrEmpty(personaId)) return;
            if (!string.IsNullOrEmpty(PersonaId) &&
                !string.Equals(personaId, PersonaId, StringComparison.OrdinalIgnoreCase)) return;
            if (Gate != null && !Gate()) return;
            Increment();
        }
    }
}
