using UnityEngine;
using Valkur.Data;
using Valkur.Gameplay.Combat;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// Raises a blocking barrier across the cast direction.
    ///
    /// <para>UNITS. <c>wallWidth</c> and <c>wallHeight</c> are WORLD UNITS. They used to be
    /// divided by 32 — a leftover from the Python build, where they were pixels — so the
    /// shipped <c>wall_ice</c> (12.5 x 3.125) resolved to a quad 0.78 units wide and 0.049
    /// tall: twelve screen pixels by less than one, collider included. The defaults in this
    /// file were thirty times larger than anything the asset could produce, which is what
    /// gave the mistake away.</para>
    ///
    /// <para>AIMING. <c>spawnAtMouse</c> was authored, serialised, shown in the F4 editor and
    /// read by nobody here: this executor placed the barrier at a fixed <c>distance</c> along
    /// the caster's facing whatever the flag said, so <c>arcane_barrier</c> shipped with the
    /// box ticked and no way to aim. That is the same defect the puddle, totem, vortex field
    /// and arcane flame each carried, and it has the same single owner —
    /// <see cref="SpellTargeting.ResolveGroundTarget"/>, which also clamps the cursor to the
    /// spell's own <c>range</c> and falls back to the facing for a monster, which has no
    /// pointer. <c>wall_ice</c> authors the flag off and is untouched.</para>
    ///
    /// <para>The barrier stands on the Building layer because that is the layer Player(8),
    /// NPC(9) and Projectile(10) all collide with in the Physics2D matrix — it blocks the
    /// caster too, which is deliberate, and <c>PathFinder.IsWalkable</c> masks on
    /// <c>BlockingMask()</c> (which contains Building), so monsters route around it rather
    /// than walking into it.</para>
    /// </summary>
    public class WallExecutor : ISpellExecutor
    {
        private const float DefaultLengthWu = 6f;
        private const float DefaultHeightWu = 1.8f;
        private const float DefaultDistanceWu = 3f;

        /// <summary>Cast reach for an AIMED barrier whose definition authors no range.</summary>
        private const float DefaultRangeWu = 6f;
        private const float DefaultHp = 100f;
        private const float DefaultDurationSeconds = 6f;

        public void Execute(SpellContext ctx)
        {
            float length = ctx.Spell.wallWidth > 0 ? ctx.Spell.wallWidth : DefaultLengthWu;
            float height = ctx.Spell.wallHeight > 0 ? ctx.Spell.wallHeight : DefaultHeightWu;
            float hp = ctx.Spell.wallHP > 0 ? ctx.Spell.wallHP : DefaultHp;
            // The wall's real exit is its HP reaching zero; the timer is a backstop.
            float duration = ctx.Spell.infinite
                ? float.PositiveInfinity
                : (ctx.Spell.duration > 0 ? ctx.Spell.duration : DefaultDurationSeconds);

            Vector2 direction = ctx.Direction.sqrMagnitude > 1e-4f ? ctx.Direction.normalized : Vector2.right;
            Vector2 spawnPos = SpellTargeting.ResolveGroundTarget(ctx, DefaultRangeWu, DefaultDistanceWu);

            // Perpendicular to the cast: the barrier stands ACROSS the line of fire.
            Vector2 axis = new Vector2(-direction.y, direction.x);

            var wallGo = new GameObject("SpellWall");
            wallGo.transform.position = spawnPos;
            // Identity rotation and unit scale: IceWallVisual documents why both matter.
            wallGo.layer = BuildingLayer();

            var collider = BuildCollider(wallGo.transform, axis, length, height, ctx.Spell);

            var health = wallGo.AddComponent<Health>();
            health.Initialize(Mathf.RoundToInt(hp));

            var controller = wallGo.AddComponent<WallController>();
            controller.Initialize(new WallController.Setup
            {
                Duration = duration,
                Health = health,
                Collider = collider,
                Length = length,
                Height = height,
                Axis = axis,
                Element = ProjectileExecutor.ResolveElement(ctx.Spell),
                // The barrier's whole palette. It used to reach nothing at all: the element was
                // captured into this same struct and never read by a single line, so a spell
                // authored Arcane with a violet swatch rendered as an ice wall.
                Swatch = ctx.Spell.particleColor,
            });

            // Free-standing world object: nothing else can end it. The registry enforces
            // maxInstances and clears it on a zone change.
            SpellEffectRegistry.Track(wallGo, ctx.Spell, ctx.Caster != null ? ctx.Caster.gameObject : null);
        }

        /// <summary>
        /// The blocking box, on its own rotated child.
        ///
        /// <para>The root is left unrotated so the crystals can stand up on screen whichever
        /// way the barrier runs, so the box gets a child of its own. Its depth is a FRACTION
        /// of the drawn height: in a top-down projection what a wall occupies on the floor is
        /// its footprint, not its silhouette, and a collider as deep as the art is tall pushes
        /// the player a body-length away from something they can see themselves touching.</para>
        /// </summary>
        private static BoxCollider2D BuildCollider(Transform root, Vector2 axis, float length,
            float height, SpellDefinition spell)
        {
            var go = new GameObject("Collision");
            go.transform.SetParent(root, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.Euler(0f, 0f,
                Mathf.Atan2(axis.y, axis.x) * Mathf.Rad2Deg);
            go.layer = BuildingLayer();

            var collider = go.AddComponent<BoxCollider2D>();
            collider.size = new Vector2(length, Mathf.Clamp(height * 0.42f, 0.35f, 1.1f));

            // blockProjectiles / blockUnits were authored, serialised and shown in the F4
            // editor for as long as the spell has existed, and no code had ever read either
            // one: the blocking came entirely from the physics matrix, so clearing a flag
            // changed nothing. excludeLayers is what makes them mean something.
            int excluded = 0;
            if (!spell.blockProjectiles) excluded |= LayerOrZero("Projectile");
            if (!spell.blockUnits) excluded |= LayerOrZero("Player") | LayerOrZero("NPC");
            collider.excludeLayers = excluded;

            return collider;
        }

        private static int BuildingLayer()
        {
            int layer = LayerMask.NameToLayer("Building");
            return layer >= 0 ? layer : 14;
        }

        private static int LayerOrZero(string name)
        {
            int layer = LayerMask.NameToLayer(name);
            return layer >= 0 ? 1 << layer : 0;
        }
    }
}
