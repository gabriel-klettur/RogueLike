using UnityEngine;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay.Combat;
using Valkur.Gameplay.VFX;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// Blink to a point, and leave whatever the spell authors at BOTH ends of the trip.
    ///
    /// <para>THREE SPELLS SHARED ONE GESTURE. <c>teleport</c>, <c>glacial_step</c> and
    /// <c>shadow_step</c> all ran the transporter cycle and differed only by tint, so the
    /// Mobility category had one visual wearing three names. A blink that shatters into ice
    /// and a blink that is drawn down into the floor are different fictions, and the colour is
    /// the least of what separates them — so the gesture is now dispatched on the spell's own
    /// ELEMENT, the same way <c>SlashProfile</c> dispatches a swing on its arc.</para>
    ///
    /// <para>AND <c>glacial_step</c> FROZE NOTHING. It authors <c>radius: 1.9</c>,
    /// <c>damage: 10</c> and a 2.5 s <c>Slow</c>, and this executor performed no overlap, no
    /// damage and no status application at either end: the spell's entire combat payload was
    /// unread, exactly as <c>leap_slam</c>'s was. The area is applied at the departure point
    /// AND the arrival point now, which is the whole design — an escape that punishes whoever
    /// was chasing. <c>shadow_step</c> authors no radius and stays harmless, by data rather
    /// than by a branch.</para>
    /// </summary>
    public class TeleportExecutor : ISpellExecutor
    {
        private const float DEFAULT_DISTANCE = 4f;

        /// <summary>
        /// Every layer a blink may not pass through or land inside: World(11),
        /// Building(14) and every painted <c>WorldL{N}</c> / <c>WorldAll</c> cell.
        /// Masking on the two legacy layers alone blinked the caster into walls.
        /// </summary>
        private static int BLOCKING_MASK => World.Layering.WorldCollisionLayers.BlockingMask();

        /// <summary>Probe radius and wall clearance for the blink sweep, in world units.</summary>
        private const float SWEEP_RADIUS = 0.3f;
        private const float WALL_CLEARANCE = 0.4f;

        /// <summary>
        /// How long after the departure the arrival lands. The ORDER carries the whole spell:
        /// seeing both ends at once is one event, seeing one and then the other is a journey,
        /// and it is what drags the eye from the place left to the place arrived at.
        /// </summary>
        private const float ARRIVAL_LEAD = 0.05f;

        /// <summary>Silhouette used when the caster has no sprite to measure.</summary>
        private static readonly Vector2 FallbackSilhouette = new Vector2(0.7f, 1.1f);

        /// <summary>Transporter amber, used when the spell leaves its tint unset.</summary>
        private static readonly Color DefaultTint = new Color(1f, 0.87f, 0.5f, 1f);

        public void Execute(SpellContext ctx)
        {
            if (ctx.Caster == null || ctx.Spell == null) return;

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

            // Both ends, and in that order: the punishment is left where the chaser is, not
            // only where the caster went.
            ApplyAreaAt(ctx, origin);
            ApplyAreaAt(ctx, destination);

            DrawGesture(ctx, origin, destination, centerOffset, silhouette,
                        departingSprite, departingFlip, sortingLayerId, sortingOrder);

            // Gated on HasSfx: AudioCatalog.asset holds no spell_* id at all, so an ungated
            // call is a guaranteed console warning for a sound that was never authored.
            var audio = ServiceLocator.Get<IAudioService>();
            if (audio != null && audio.HasSfx("spell_teleport_depart"))
                audio.PlaySfxById("spell_teleport_depart");

            if (VFXManager.Instance != null && !string.IsNullOrEmpty(ctx.Spell.vfxPreset))
                VFXManager.Instance.SpawnParticlePreset(ctx.Spell.vfxPreset, (Vector3)destination);
        }

        /// <summary>
        /// Which of the three blinks this is. Dispatching on the ELEMENT rather than on the
        /// spell key means a new cryomancy or umbramancy blink inherits its own gesture
        /// without touching this method — and a spell with no element keeps the transporter
        /// cycle, which is what <c>teleport</c> has always looked like.
        /// </summary>
        private static void DrawGesture(SpellContext ctx, Vector2 origin, Vector2 destination,
                                        Vector3 centerOffset, Vector2 silhouette,
                                        Sprite sprite, bool flipX,
                                        int sortingLayerId, int sortingOrder)
        {
            // A blink that authors no element keeps the transporter cycle, which is what
            // `teleport` has always looked like — Arcane is the "no element" stand-in here
            // rather than a claim, and it is deliberately NOT the enum's default (Dark), which
            // would have silently handed every unauthored blink the shadow gesture.
            SpellElement element = ProjectileExecutor.ResolveElement(ctx.Spell)
                                   ?? SpellElement.Arcane;

            // L8: the element chooses the palette, the spell's own swatch chooses the hue, and
            // the raw field is never read directly.
            var palette = ElementPalette.For(element).RecolouredTo(ctx.Spell.particleColor);

            Vector3 departCenter = (Vector3)origin + centerOffset;
            Vector3 arriveCenter = (Vector3)destination + centerOffset;

            switch (element)
            {
                case SpellElement.Ice:
                    GlacialStepFX.Shatter(departCenter, silhouette, sprite, flipX,
                                          sortingLayerId, sortingOrder, palette, ctx.Spell);
                    GlacialStepFX.Resolve(ctx.Caster, arriveCenter, silhouette, palette,
                                          ctx.Spell, ARRIVAL_LEAD);
                    return;

                case SpellElement.Dark:
                    ShadowStepFX.Peel(departCenter, silhouette, sprite, flipX,
                                      sortingLayerId, sortingOrder, palette);
                    ShadowStepFX.Knit(ctx.Caster, departCenter, arriveCenter, silhouette,
                                      sprite, flipX, sortingLayerId, sortingOrder,
                                      palette, ctx.Spell, ARRIVAL_LEAD);
                    return;

                default:
                    Color tint = ctx.Spell.particleColor != Color.clear
                        ? ctx.Spell.particleColor
                        : DefaultTint;
                    TransporterFX.Dematerialize(departCenter, silhouette, sprite, flipX,
                                                sortingLayerId, sortingOrder, tint);
                    TransporterFX.Materialize(ctx.Caster, arriveCenter, silhouette, tint);
                    return;
            }
        }

        /// <summary>
        /// The authored area, applied at one end of the trip. A no-op for a blink that authors
        /// no radius, which is how <c>teleport</c> and <c>shadow_step</c> stay harmless without
        /// a spell-key branch.
        /// </summary>
        private static void ApplyAreaAt(SpellContext ctx, Vector2 center)
        {
            if (ctx.Spell.radius <= 0f || ctx.TargetLayers.value == 0) return;

            bool hasStatus = ctx.Spell.statusApplications != null
                          && ctx.Spell.statusApplications.Length > 0;
            if (ctx.Spell.damage <= 0f && !hasStatus) return;

            var element = ProjectileExecutor.ResolveElement(ctx.Spell);
            var hits = Physics2D.OverlapCircleAll(center, ctx.Spell.radius, ctx.TargetLayers);

            for (int i = 0; i < hits.Length; i++)
            {
                var collider = hits[i];
                if (collider == null) continue;
                if (collider.transform.IsChildOf(ctx.Caster)) continue;

                var health = collider.GetComponentInParent<Health>();
                if (health == null || health.IsDead) continue;

                if (ctx.Spell.damage > 0f)
                {
                    int dealt = SpellPower.ScaleToInt(ctx.Spell.damage, ctx.Caster);
                    health.TakeDamage(dealt, ctx.Caster.gameObject, element);
                    GameEvents.FireHitDealt(ctx.Caster.gameObject, health.gameObject, dealt);
                }

                StatusApplicationFactory.ApplyAll(ctx.Spell.statusApplications,
                                                  health.gameObject, ctx.Caster.gameObject);
            }
        }

        /// <summary>
        /// Where the blink lands. An aimed blink goes to the CURSOR — resolved through
        /// <see cref="SpellTargeting"/>, the single owner of what <c>spawnAtMouse</c> means,
        /// and then rebased onto the caster's own ground plane because that helper measures
        /// from hand height plus forward clearance. Either way the path is swept, because a
        /// cursor can point straight through a wall.
        /// </summary>
        private static Vector2 ResolveDestination(SpellContext ctx, Vector2 origin, float distance)
        {
            Vector2 travel;
            if (ctx.Spell.spawnAtMouse)
            {
                Vector2 aimed = SpellTargeting.ResolveGroundTarget(ctx, distance, distance);
                Vector2 lift = (Vector2)ProjectileExecutor.ResolveCastStart(
                                   ctx.Caster, ctx.Direction, ctx.Spell) - origin;
                travel = aimed - lift - origin;
                if (travel.sqrMagnitude > distance * distance) travel = travel.normalized * distance;
                if (travel.sqrMagnitude < 0.0025f) travel = ctx.Direction * distance;
            }
            else
            {
                travel = ctx.Direction * distance;
            }

            float length = travel.magnitude;
            if (length < 0.01f) return origin;
            Vector2 heading = travel / length;

            var hit = Physics2D.CircleCast(origin, SWEEP_RADIUS, heading, length, BLOCKING_MASK);
            if (hit.collider == null) return origin + travel;
            return origin + heading * Mathf.Max(0f, hit.distance - WALL_CLEARANCE);
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
