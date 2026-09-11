using System;
using UnityEngine;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay.FSM;

namespace Valkur.Gameplay.Quests
{
    /// <summary>
    /// "Kill N monsters of type X" objective. Listens to GameEvents.OnEntityDied,
    /// filters by <see cref="MonsterDefinition.monsterKey"/> on the victim's
    /// FSMMonsterBrain, and increments <see cref="ObjectiveBase.Current"/> until
    /// reaching <see cref="ObjectiveBase.Target"/>. Empty <see cref="MonsterKey"/>
    /// matches any non-player victim — useful for "kill 50 enemies" generic
    /// objectives.
    ///
    /// <para>Fires <see cref="OnProgressChanged"/> after each increment so quest
    /// log UI can refresh without polling each frame. That event predates
    /// <see cref="ObjectiveBase.Progressed"/> and is kept because
    /// <c>QuestLogHUD</c> and the shipped fixtures bind to it; both are raised, and
    /// the base one is what <see cref="Quest"/> aggregates.</para>
    /// </summary>
    public sealed class KillCountObjective : ObjectiveBase
    {
        public string MonsterKey { get; }

        /// <summary>Fires (current, target) after each increment.</summary>
        public event Action<int, int> OnProgressChanged;

        public KillCountObjective(string id,
                                  string description,
                                  int target,
                                  string monsterKey = null)
            : base(id, description, target)
        {
            MonsterKey = monsterKey;
            Progressed += _ => OnProgressChanged?.Invoke(Current, Target);
        }

        protected override void OnBegin() => GameEvents.OnEntityDied += HandleEntityDied;
        protected override void OnEnd()   => GameEvents.OnEntityDied -= HandleEntityDied;

        private void HandleEntityDied(GameObject victim, GameObject killer)
        {
            if (IsComplete) return;
            if (victim == null) return;

            // Filter out the player and any non-monster fatalities so a
            // generic "kill 50" objective doesn't tick when the player dies.
            if (victim.CompareTag("Player")) return;

            // Filter by monster key when specified. Brain may be missing on
            // generic NPCs (e.g. coin pickups that fire OnEntityDied) — skip
            // those when MonsterKey is set.
            if (!string.IsNullOrEmpty(MonsterKey))
            {
                var brain = victim.GetComponent<FSMMonsterBrain>();
                if (brain == null || brain.Definition == null) return;
                if (!string.Equals(brain.Definition.monsterKey, MonsterKey,
                        StringComparison.OrdinalIgnoreCase)) return;
            }

            Increment();
        }
    }
}
