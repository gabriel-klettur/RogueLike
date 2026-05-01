using UnityEngine;
using Valkur.Data;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// Context passed to spell executors containing all data needed to execute a spell.
    /// </summary>
    public struct SpellContext
    {
        public SpellDefinition Spell;
        public Transform Caster;
        public Vector2 Direction;
        public LayerMask TargetLayers;
        public GameObject ProjectilePrefab;
    }

    /// <summary>
    /// Strategy interface for spell execution.
    /// Each spell type (Projectile, Slash, Area, Dash) has its own executor.
    /// </summary>
    public interface ISpellExecutor
    {
        void Execute(SpellContext ctx);
    }
}
