using UnityEngine;
using Valkur.Core.Input;
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

            // Maximum distance from the caster that the meteor centre may sit at.
            // Player casts use the cursor (clamped to this); NPCs / no-mouse fall
            // back to a fixed direction × distance offset (legacy behaviour).
            float spawnDist = ctx.Spell.range > 0 ? ctx.Spell.range / 16f : 6f;
            Vector2 center = ResolveMeteorCenter(ctx, spawnDist);

            var controllerGo = new GameObject("MeteorShower");
            controllerGo.transform.position = (Vector3)center;
            var controller = controllerGo.AddComponent<MeteorStrikeController>();
            controller.Initialize(count, interval, areaRadius, impactRadius,
                Mathf.RoundToInt(damage), ctx.TargetLayers, ctx.Spell.impactPreset);

        }

        /// <summary>
        /// Resolves the world-space centre of the meteor area. For player casts the
        /// centre tracks the cursor's world position clamped to <paramref name="maxDist"/>
        /// from the caster — so meteors rain around wherever the player is pointing,
        /// up to the spell's range. NPCs and players whose cursor is off-screen fall
        /// back to <c>casterPos + direction × maxDist</c>.
        /// </summary>
        private static Vector2 ResolveMeteorCenter(SpellContext ctx, float maxDist)
        {
            var pc = ctx.Caster != null
                ? ctx.Caster.GetComponent<Valkur.Gameplay.PlayerController>()
                : null;
            if (pc != null)
            {
                var cam = Camera.main;
                if (cam != null && MouseInputManager.TryGetWorldMousePosition(
                        out Vector2 mouseWorld,
                        cam,
                        requireInView: true,
                        requireApplicationFocus: false))
                {
                    Vector2 casterPos = (Vector2)ctx.Caster.position;
                    Vector2 toMouse = mouseWorld - casterPos;
                    float dist = toMouse.magnitude;
                    if (dist > maxDist && dist > 0.0001f)
                        toMouse = toMouse * (maxDist / dist);
                    return casterPos + toMouse;
                }
            }
            // Fallback: NPC casting or cursor off-screen — direction × maxDist.
            return (Vector2)ctx.Caster.position + ctx.Direction * maxDist;
        }
    }
}
