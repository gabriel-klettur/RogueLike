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
    /// FSMMonsterBrain, and increments <see cref="Current"/> until reaching
    /// <see cref="Target"/>. Empty <see cref="MonsterKey"/> matches any
    /// non-player victim — useful for "kill 50 enemies" generic objectives.
    ///
    /// Fires <see cref="OnProgressChanged"/> after each increment so quest
    /// log UI can refresh without polling each frame.
    /// </summary>
    public sealed class KillCountObjective : IObjective
    {
        public string Id          { get; }
        public string Description { get; }
        public int    Target      { get; }
        public string MonsterKey  { get; }

        public int  Current    { get; private set; }
        public bool IsComplete => Current >= Target;

        /// <summary>Fires (current, target) after each increment.</summary>
        public event Action<int, int> OnProgressChanged;

        private bool _subscribed;

        public KillCountObjective(string id,
                                  string description,
                                  int target,
                                  string monsterKey = null)
        {
            Id          = id ?? string.Empty;
            Description = description ?? string.Empty;
            Target      = Mathf.Max(1, target);
            MonsterKey  = monsterKey;
        }

        public void Begin()
        {
            if (_subscribed) return;
            GameEvents.OnEntityDied += HandleEntityDied;
            _subscribed = true;
        }

        public void End()
        {
            if (!_subscribed) return;
            GameEvents.OnEntityDied -= HandleEntityDied;
            _subscribed = false;
        }

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

            Current++;
            OnProgressChanged?.Invoke(Current, Target);
        }
    }
}
