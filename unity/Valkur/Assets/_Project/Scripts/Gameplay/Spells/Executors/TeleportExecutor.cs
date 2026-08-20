using UnityEngine;
using Valkur.Core;
using Valkur.Gameplay.VFX;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// Blink along the cast direction, presented as a transporter cycle: the body is
    /// dematerialised into a shimmering column at the point it left and reassembled out of
    /// the same motes at the point it arrives.
    ///
    /// It used to open two spinning arcane portals instead, which is a different fiction —
    /// a portal implies the character walked through a hole in space, and it left the body
    /// fully solid at both ends, so nothing about the character read as having been
    /// transported at all. The silhouette and the sprite's own alpha do that work now.
    /// </summary>
    public class TeleportExecutor : ISpellExecutor
    {
        private const float DEFAULT_DISTANCE = 4f;

        /// <summary>World and Building: the two layers a blink may not pass through.</summary>
        private const int BLOCKING_MASK = (1 << 11) | (1 << 14);

        /// <summary>Probe radius and wall clearance for the blink sweep, in world units.</summary>
        private const float SWEEP_RADIUS = 0.3f;
        private const float WALL_CLEARANCE = 0.4f;

        /// <summary>Silhouette used when the caster has no sprite to measure.</summary>
        private static readonly Vector2 FallbackSilhouette = new Vector2(0.7f, 1.1f);

        /// <summary>Transporter amber, used when the spell leaves its tint unset.</summary>
        private static readonly Color DefaultTint = new Color(1f, 0.87f, 0.5f, 1f);

        public void Execute(SpellContext ctx)
        {
            float distance = ctx.Spell.distance > 0f ? ctx.Spell.distance : DEFAULT_DISTANCE;
            Vector2 origin = ctx.Caster.position;
            Vector2 destination = ResolveDestination(ctx, origin, distance);

            // Everything the departure needs is read before the caster moves: afterwards the
            // renderer is at the far end and its bounds describe the arrival, not the exit.
            SpriteRenderer body = ResolveBodyRenderer(ctx.Caster);
            Vector2 silhouette = body != null && body.sprite != null
                ? (Vector2)body.bounds.size
                : FallbackSilhouette;
            Vector3 centerOffset = body != null && body.sprite != null
                ? body.bounds.center - ctx.Caster.position
                : new Vector3(0f, FallbackSilhouette.y * 0.5f, 0f);

            Sprite departingSprite = body != null ? body.sprite : null;
            bool departingFlip = body != null && body.flipX;
            int sortingLayerId = body != null ? body.sortingLayerID : 0;
            int sortingOrder = body != null ? body.sortingOrder : 0;

            Move(ctx, destination);

            Color tint = ctx.Spell.particleColor != Color.clear ? ctx.Spell.particleColor : DefaultTint;

            TransporterFX.Dematerialize((Vector3)origin + centerOffset, silhouette,
                departingSprite, departingFlip, sortingLayerId, sortingOrder, tint);
            TransporterFX.Materialize(ctx.Caster, (Vector3)destination + centerOffset, silhouette, tint);

            // The arrival chirp is played by the materialising end once its column is up, so
            // the two halves of the cycle are heard apart rather than as one doubled sound.
            ServiceLocator.Get<IAudioService>()?.PlaySfxById("spell_teleport_depart");

            if (VFXManager.Instance != null && !string.IsNullOrEmpty(ctx.Spell.vfxPreset))
                VFXManager.Instance.SpawnParticlePreset(ctx.Spell.vfxPreset, (Vector3)destination);
        }

        private static Vector2 ResolveDestination(SpellContext ctx, Vector2 origin, float distance)
        {
            var hit = Physics2D.CircleCast(origin, SWEEP_RADIUS, ctx.Direction, distance, BLOCKING_MASK);
            if (hit.collider == null) return origin + ctx.Direction * distance;
            return origin + ctx.Direction * Mathf.Max(0f, hit.distance - WALL_CLEARANCE);
        }

        private static void Move(SpellContext ctx, Vector2 destination)
        {
            var rb = ctx.Caster.GetComponent<Rigidbody2D>();
            if (rb != null) rb.MovePosition(destination);
            else ctx.Caster.position = destination;
        }

        /// <summary>
        /// The renderer that draws the character, resolved the same way every other body
        /// effect in the project resolves it.
        /// </summary>
        private static SpriteRenderer ResolveBodyRenderer(Transform caster)
        {
            var sr = caster.GetComponent<SpriteRenderer>();
            if (sr != null && sr.sprite != null) return sr;

            foreach (var candidate in caster.GetComponentsInChildren<SpriteRenderer>())
                if (candidate != null && candidate.sprite != null) return candidate;

            return null;
        }
    }
}
