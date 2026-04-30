using UnityEngine;

namespace Valkur.Gameplay
{
    /// <summary>
    /// Builds the runtime body collider for NPC-like entities from their visual bounds.
    /// Keeping this outside EntitySetup makes collider policy testable and isolated.
    /// </summary>
    public static class EntityColliderConfigurator
    {
        public const float DefaultNpcBodyCoverage = 0.5f;
        private const float MinBodySide = 0.05f;

        public static void ApplyLayerRecursively(GameObject root, int layer)
        {
            if (root == null) return;

            var transforms = root.GetComponentsInChildren<Transform>(includeInactive: true);
            for (int i = 0; i < transforms.Length; i++)
                transforms[i].gameObject.layer = layer;
        }

        public static BoxCollider2D ConfigureNpcBodyCollider(
            GameObject entity,
            SpriteRenderer preferredRenderer = null,
            float bodyCoverage = DefaultNpcBodyCoverage)
        {
            if (entity == null) return null;

            bodyCoverage = Mathf.Clamp01(bodyCoverage);
            if (bodyCoverage <= 0f)
                bodyCoverage = DefaultNpcBodyCoverage;

            var box = ResolveBodyBox(entity);
            RemoveLegacyRootColliders(entity, box);

            Bounds visualBounds = ResolveVisualBounds(entity, preferredRenderer, box);
            float worldSide = Mathf.Max(MinBodySide, Mathf.Min(visualBounds.size.x, visualBounds.size.y) * bodyCoverage);
            Vector3 localCenter = entity.transform.InverseTransformPoint(visualBounds.center);
            Vector3 scale = entity.transform.lossyScale;

            float scaleX = Mathf.Max(0.0001f, Mathf.Abs(scale.x));
            float scaleY = Mathf.Max(0.0001f, Mathf.Abs(scale.y));

            box.enabled = true;
            box.isTrigger = false;
            box.usedByComposite = false;
            box.offset = new Vector2(localCenter.x, localCenter.y);
            box.size = new Vector2(worldSide / scaleX, worldSide / scaleY);

            return box;
        }

        public static Collider2D GetBodyCollider(GameObject entity)
        {
            if (entity == null) return null;

            var box = entity.GetComponent<BoxCollider2D>();
            if (IsUsableBodyCollider(box))
                return box;

            var colliders = entity.GetComponents<Collider2D>();
            for (int i = 0; i < colliders.Length; i++)
            {
                if (IsUsableBodyCollider(colliders[i]))
                    return colliders[i];
            }

            return null;
        }

        private static BoxCollider2D ResolveBodyBox(GameObject entity)
        {
            var boxes = entity.GetComponents<BoxCollider2D>();
            BoxCollider2D body = boxes.Length > 0 ? boxes[0] : entity.AddComponent<BoxCollider2D>();

            for (int i = 1; i < boxes.Length; i++)
                RemoveCollider(boxes[i]);

            return body;
        }

        private static void RemoveLegacyRootColliders(GameObject entity, BoxCollider2D body)
        {
            var colliders = entity.GetComponents<Collider2D>();
            for (int i = 0; i < colliders.Length; i++)
            {
                var collider = colliders[i];
                if (collider == null || collider == body) continue;
                RemoveCollider(collider);
            }
        }

        private static void RemoveCollider(Collider2D collider)
        {
            if (collider == null) return;
            collider.enabled = false;

            if (Application.isPlaying)
                Object.Destroy(collider);
            else
                Object.DestroyImmediate(collider);
        }

        private static Bounds ResolveVisualBounds(GameObject entity, SpriteRenderer preferredRenderer, Collider2D fallbackCollider)
        {
            var renderer = preferredRenderer != null ? preferredRenderer : entity.GetComponentInChildren<SpriteRenderer>();
            if (renderer != null && renderer.sprite != null)
                return renderer.bounds;

            if (fallbackCollider != null)
                return fallbackCollider.bounds;

            return new Bounds(entity.transform.position, Vector3.one);
        }

        private static bool IsUsableBodyCollider(Collider2D collider)
        {
            return collider != null && collider.enabled && !collider.isTrigger;
        }
    }
}
