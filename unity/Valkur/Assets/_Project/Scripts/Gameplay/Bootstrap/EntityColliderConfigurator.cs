using UnityEngine;
using Valkur.Data;
using Valkur.Gameplay.Combat;

namespace Valkur.Gameplay
{
    /// <summary>
    /// Builds an entity's collision layers: the FOOTPRINT at its feet and the shaped HURTBOX
    /// over its body (<see cref="EntityColliderRig"/>).
    ///
    /// <para><b>What this replaced.</b> One centred square, half the sprite's shorter side, did
    /// every job at once. Measured on the shipped roster: the dwarf (1.22 x 1.86 u) collided with
    /// walls on a 0.61 u square floating 0.63 u above his boots, so he walked into the bottom of
    /// every wall up to his waist; the red dragon (8.23 x 4.55 u) could be hit only inside a
    /// 2.28 u square on its back, so a fireball at its head or its tail flew through. The player
    /// prefab had always had a footprint at the feet (0.5 x 0.3); monsters now follow the same
    /// convention, and both get a hurtbox.</para>
    /// </summary>
    public static class EntityColliderConfigurator
    {
        private const float MinSide = 0.05f;

        public static void ApplyLayerRecursively(GameObject root, int layer)
        {
            if (root == null) return;

            var transforms = root.GetComponentsInChildren<Transform>(includeInactive: true);
            for (int i = 0; i < transforms.Length; i++)
                transforms[i].gameObject.layer = layer;
        }

        /// <summary>
        /// The NPC footprint: a horizontal capsule centred on the pivot (the feet), sized from
        /// the profile or, when nothing is authored, from the frame on screen. Replaces every
        /// other collider on the root — the prefab's placeholder circle included.
        ///
        /// <para>A capsule rather than a box because a rounded end slides along a wall corner and
        /// along another body instead of catching on it, which is most of what a crowd of monsters
        /// does all day.</para>
        /// </summary>
        public static CapsuleCollider2D ConfigureNpcFootprint(GameObject entity,
            SpriteRenderer preferredRenderer = null, EntityCollisionProfile profile = null)
        {
            if (entity == null) return null;

            var capsule = entity.GetComponent<CapsuleCollider2D>();
            if (capsule == null) capsule = entity.AddComponent<CapsuleCollider2D>();
            RemoveOtherRootColliders(entity, capsule);

            Vector2 worldSize;
            Vector2 worldOffset = Vector2.zero;
            if (profile != null && profile.HasAuthoredFootprint)
            {
                // Authored in world units at scale 1: the entity's own scale still applies,
                // exactly as it does to the art, so a scaled-up variant gets bigger feet.
                Vector3 s = entity.transform.lossyScale;
                worldSize = new Vector2(profile.footprintSize.x * Mathf.Abs(s.x), profile.footprintSize.y * Mathf.Abs(s.y));
                worldOffset = new Vector2(profile.footprintOffset.x * Mathf.Abs(s.x), profile.footprintOffset.y * Mathf.Abs(s.y));
            }
            else
            {
                Vector2 body = ResolveBodySize(entity, preferredRenderer);
                worldSize = EntityCollisionProfile.AutoFootprintSize(body.x, body.y);
            }

            Vector3 lossy = entity.transform.lossyScale;
            float sx = Mathf.Max(0.0001f, Mathf.Abs(lossy.x));
            float sy = Mathf.Max(0.0001f, Mathf.Abs(lossy.y));

            capsule.enabled = true;
            capsule.isTrigger = false;
            capsule.direction = worldSize.x >= worldSize.y ? CapsuleDirection2D.Horizontal : CapsuleDirection2D.Vertical;
            capsule.offset = new Vector2(worldOffset.x / sx, worldOffset.y / sy);
            capsule.size = new Vector2(Mathf.Max(MinSide, worldSize.x) / sx, Mathf.Max(MinSide, worldSize.y) / sy);
            return capsule;
        }

        /// <summary>
        /// Installs the hurtbox over <paramref name="footprint"/>'s entity. Safe to call again:
        /// the rig re-states itself.
        /// </summary>
        public static EntityColliderRig InstallRig(GameObject entity, EntityCollisionProfile profile,
                                                   SpriteRenderer renderer, Collider2D footprint)
        {
            if (entity == null) return null;
            if (renderer == null) renderer = entity.GetComponentInChildren<SpriteRenderer>();
            if (renderer == null) return null;

            var rig = entity.GetComponent<EntityColliderRig>();
            if (rig == null) rig = entity.AddComponent<EntityColliderRig>();
            rig.Configure(profile, renderer, entity.GetComponent<DirectionalAnimator>(), footprint);
            return rig;
        }

        /// <summary>
        /// The collider that stands on the ground — the rig's footprint when there is one, else
        /// the first enabled solid collider on the ROOT. Never a hurtbox capsule, which live on a
        /// child.
        /// </summary>
        public static Collider2D GetBodyCollider(GameObject entity)
        {
            if (entity == null) return null;

            var rig = entity.GetComponent<EntityColliderRig>();
            if (rig != null && IsUsableBodyCollider(rig.Footprint))
                return rig.Footprint;

            var colliders = entity.GetComponents<Collider2D>();
            for (int i = 0; i < colliders.Length; i++)
            {
                if (IsUsableBodyCollider(colliders[i]))
                    return colliders[i];
            }

            return null;
        }

        private static void RemoveOtherRootColliders(GameObject entity, Collider2D keep)
        {
            var colliders = entity.GetComponents<Collider2D>();
            for (int i = 0; i < colliders.Length; i++)
            {
                var collider = colliders[i];
                if (collider == null || collider == keep) continue;
                collider.enabled = false;

                if (Application.isPlaying)
                    Object.Destroy(collider);
                else
                    Object.DestroyImmediate(collider);
            }
        }

        /// <summary>World size of the frame on screen, or a unit square when there is nothing drawn.</summary>
        private static Vector2 ResolveBodySize(GameObject entity, SpriteRenderer preferredRenderer)
        {
            var renderer = preferredRenderer != null ? preferredRenderer : entity.GetComponentInChildren<SpriteRenderer>();
            if (renderer != null && renderer.sprite != null)
            {
                Rect r = HurtShapeSpace.LocalRect(renderer.sprite);
                Vector3 s = renderer.transform.lossyScale;
                return new Vector2(r.width * Mathf.Abs(s.x), r.height * Mathf.Abs(s.y));
            }
            return Vector2.one;
        }

        private static bool IsUsableBodyCollider(Collider2D collider)
        {
            return collider != null && collider.enabled && !collider.isTrigger;
        }
    }
}
