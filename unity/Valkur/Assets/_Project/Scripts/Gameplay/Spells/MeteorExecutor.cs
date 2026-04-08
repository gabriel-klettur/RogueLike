using UnityEngine;
using Valkur.Data;
using Valkur.Gameplay.Combat;
using Valkur.Gameplay.VFX;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// Executes meteor shower: sequential meteor strikes in an area around the target position.
    /// Mirrors Python's MeteorShowerResolver.
    /// </summary>
    public class MeteorExecutor : ISpellExecutor
    {
        public void Execute(SpellContext ctx)
        {
            int count = ctx.Spell.meteorCount > 0 ? ctx.Spell.meteorCount : 8;
            float interval = ctx.Spell.meteorInterval > 0 ? ctx.Spell.meteorInterval : 0.25f;
            float areaRadius = ctx.Spell.meteorAreaRadius > 0 ? ctx.Spell.meteorAreaRadius / 16f : 32.5f;
            float impactRadius = ctx.Spell.meteorImpactRadius > 0 ? ctx.Spell.meteorImpactRadius / 16f : 10f;
            float damage = ctx.Spell.damage;

            // Spawn at mouse position (distance from caster) or in front of caster
            float spawnDist = ctx.Spell.range > 0 ? ctx.Spell.range / 16f : 6f;
            Vector2 center = (Vector2)ctx.Caster.position + ctx.Direction * spawnDist;

            var controllerGo = new GameObject("MeteorShower");
            controllerGo.transform.position = (Vector3)center;
            var controller = controllerGo.AddComponent<MeteorStrikeController>();
            controller.Initialize(count, interval, areaRadius, impactRadius,
                Mathf.RoundToInt(damage), ctx.TargetLayers, ctx.Spell.impactPreset);

            Debug.Log($"[SpellDebug] MeteorShower at {center}, count={count}, interval={interval:F2}s, area={areaRadius:F1}, impact={impactRadius:F1}, dmg={damage}");
        }
    }
}
