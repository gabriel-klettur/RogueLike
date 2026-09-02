using UnityEngine;
using Valkur.Core.Input;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// Where a spell that is PLACED on the ground lands.
    ///
    /// <para><c>SpellDefinition.spawnAtMouse</c> did not read the mouse. All three executors
    /// that honour it resolved the same thing — <c>castStart + direction * someFixedDistance</c>
    /// — so the flag only chose WHICH constant offset to use, and a vortex aimed at a target
    /// two units away landed six units past them while one aimed across the room landed in the
    /// same place. Nothing failed: the field was internally consistent and simply did not mean
    /// what it says. Same class as <c>vfxPreset</c> on a vortex, and as
    /// <c>animation_map.json</c>.</para>
    ///
    /// <para>ONE OWNER, because the flag has to mean the same thing everywhere. Duplicating the
    /// cursor projection into each executor is how two of them end up clamping to different
    /// ranges.</para>
    ///
    /// <para>The cursor is a PLAYER concept. A monster casting the same definition has no
    /// pointer, so it falls back to its facing — which is what every caster did before, and is
    /// why the fallback is not an error path.</para>
    /// </summary>
    public static class SpellTargeting
    {
        /// <summary>
        /// Resolve the world point a ground-placed spell should spawn at.
        /// </summary>
        /// <param name="aimedFallbackRange">
        /// Cast range to use when the definition authors none. Also the clamp: a cursor beyond
        /// it lands at the limit rather than wherever the player happened to click, so the
        /// spell keeps a reach the player can learn.
        /// </param>
        /// <param name="placedFallbackDistance">
        /// How far in front of the caster a NON-aimed spell of this kind sits, when the
        /// definition authors no <c>distance</c>.
        /// </param>
        public static Vector2 ResolveGroundTarget(SpellContext ctx, float aimedFallbackRange,
                                                  float placedFallbackDistance)
        {
            Vector2 start = ProjectileExecutor.ResolveCastStart(ctx.Caster, ctx.Direction, ctx.Spell);

            if (!ctx.Spell.spawnAtMouse)
            {
                float distance = ctx.Spell.distance > 0f ? ctx.Spell.distance : placedFallbackDistance;
                return start + ctx.Direction * distance;
            }

            float reach = ctx.Spell.range > 0f ? ctx.Spell.range : aimedFallbackRange;

            Vector2 cursor;
            if (!TryResolveCursorWorld(ctx.Caster, out cursor))
                return start + ctx.Direction * reach;

            Vector2 offset = cursor - start;
            if (offset.sqrMagnitude > reach * reach) offset = offset.normalized * reach;
            return start + offset;
        }

        /// <summary>
        /// The cursor in world space, or false when there is no cursor to speak of — an NPC
        /// caster, no main camera, or an input backend that has dropped its device.
        /// </summary>
        private static bool TryResolveCursorWorld(Transform caster, out Vector2 world)
        {
            world = Vector2.zero;
            if (caster == null || !caster.CompareTag("Player")) return false;

            var camera = Camera.main;
            if (camera == null) return false;

            // Through MouseInputManager, never Mouse.current: it ORs the new InputSystem with
            // the legacy backend, which is what keeps aiming alive across the Unity 2022.3
            // Editor event-drop bug.
            Vector2 screen;
            if (!MouseInputManager.TryGetScreenMousePosition(out screen)) return false;

            // The z is the distance from the camera to the play plane. It changes nothing under
            // an orthographic camera and keeps this correct if one is ever swapped in.
            Vector3 point = camera.ScreenToWorldPoint(
                new Vector3(screen.x, screen.y, -camera.transform.position.z));
            world = new Vector2(point.x, point.y);
            return true;
        }
    }
}
