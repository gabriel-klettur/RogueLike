using UnityEngine;
using Valkur.Data;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// Executor for the Beam spell type.
    /// Spawns a LaserBeamController on the caster that handles the sustained beam logic.
    /// Maps to Python's LaserBeamComponent + LaserBeamEmitterSystem.
    /// </summary>
    public class LaserBeamExecutor : ISpellExecutor
    {
        public void Execute(SpellContext ctx)
        {
            // Prevent stacking multiple beams on the same caster
            var existing = ctx.Caster.GetComponent<LaserBeamController>();
            if (existing != null)
                return;

            var controller = ctx.Caster.gameObject.AddComponent<LaserBeamController>();
            controller.Begin(ctx);
        }
    }
}
