using System;
using UnityEngine;
using Valkur.Core;

namespace Valkur.Gameplay.Quests
{
    /// <summary>
    /// "Cast spellKey N times", or cast anything N times when
    /// <see cref="SpellKey"/> is empty.
    ///
    /// <para>Filtered to the PLAYER's casts. <c>GameEvents.OnSpellCast</c> is raised
    /// by <c>SpellCaster.ExecuteSpell</c>, which is the single seam every cast in
    /// the game passes through — monsters included — so without the filter a quest
    /// to "practise your fire" would be completed by standing next to a barbol that
    /// autocasts.</para>
    ///
    /// <para>The nineteen <c>anim_*</c> animation probes cast through the same seam.
    /// They are refused here by key: an objective that named one would be a quest to
    /// open the Spells editor.</para>
    /// </summary>
    public sealed class CastSpellObjective : ObjectiveBase
    {
        public string SpellKey { get; }

        private const string AnimationProbePrefix = "anim_";

        public CastSpellObjective(string id, string description, int target, string spellKey)
            : base(id, description, target)
        {
            SpellKey = spellKey ?? string.Empty;
        }

        protected override void OnBegin() => GameEvents.OnSpellCast += HandleSpellCast;
        protected override void OnEnd()   => GameEvents.OnSpellCast -= HandleSpellCast;

        private void HandleSpellCast(GameObject caster, string spellKey, string displayName, float cooldown)
        {
            if (IsComplete || caster == null || string.IsNullOrEmpty(spellKey)) return;
            if (!caster.CompareTag("Player")) return;
            if (spellKey.StartsWith(AnimationProbePrefix, StringComparison.OrdinalIgnoreCase)) return;

            if (!string.IsNullOrEmpty(SpellKey) &&
                !string.Equals(spellKey, SpellKey, StringComparison.OrdinalIgnoreCase)) return;

            Increment();
        }
    }
}
