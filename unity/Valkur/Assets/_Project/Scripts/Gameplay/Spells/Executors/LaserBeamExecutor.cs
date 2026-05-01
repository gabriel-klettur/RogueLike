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
            // Hold-to-channel: if a beam is already active on this caster, just
            // refresh its keep-alive timer instead of stacking another instance.
            var existing = ctx.Caster.GetComponent<LaserBeamController>();
            if (existing != null)
            {
                existing.Refresh();
                return;
            }

            var controller = ctx.Caster.gameObject.AddComponent<LaserBeamController>();
            controller.Begin(ctx);
        }
    }
}
