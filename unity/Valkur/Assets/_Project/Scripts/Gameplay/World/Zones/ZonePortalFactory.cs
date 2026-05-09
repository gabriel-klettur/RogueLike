using System.Reflection;
using UnityEngine;

namespace Valkur.Gameplay.World
{
    /// <summary>
    /// Programmatic factory for <see cref="ZonePortal"/> instances created from
    /// Map-Editor portal records (vs. the hand-placed portals that ship in
    /// authored scenes).
    ///
    /// The MonoBehaviour itself only exposes inspector-edited <c>SerializeField</c>
    /// destinations; this helper pokes those fields via reflection so the
    /// runtime portal-placement editor doesn't force the production type to
    /// grow a public mutable surface that designers should never touch.
    /// Limited blast radius: every reflective write is gated behind nullable
    /// FieldInfo lookups so a future field rename surfaces as a one-line
    /// log warning rather than a crash.
    /// </summary>
    public static class ZonePortalFactory
    {
        public struct PortalSpawnSpec
        {
            public Vector3 worldPosition;
            public string destinationZoneName;
            public bool useDestinationZoneCenter;
            public Vector2 destinationWorldPosition;
            public float activationRadius;
        }

        /// <summary>
        /// Create a runtime <see cref="ZonePortal"/> GameObject under
        /// <paramref name="parent"/> at <paramref name="spec"/>'s position
        /// and bind its destination via reflection. Caller owns the lifetime;
        /// destroy via <c>Object.Destroy(go)</c> when the slot is unloaded.
        /// </summary>
        public static GameObject Spawn(Transform parent, PortalSpawnSpec spec, ZoneManager zoneManager)
        {
            var go = new GameObject("ZonePortal_Runtime");
            if (parent != null) go.transform.SetParent(parent, worldPositionStays: false);
            go.transform.position = spec.worldPosition;

            // Trigger collider — ZonePortal.RequireComponent already enforces
            // Collider2D, but creating it explicitly with an authored radius
            // gives us a single tunable knob from the editor.
            float radius = spec.activationRadius > 0f ? spec.activationRadius : 0.6f;
            var collider = go.AddComponent<CircleCollider2D>();
            collider.isTrigger = true;
            collider.radius = radius;

            var portal = go.AddComponent<ZonePortal>();

            // Resolve destination world-position: explicit coords if provided,
            // otherwise the destination zone's centre at spawn time. This
            // keeps the on-disk record portable when zones are later moved.
            Vector2 dest = spec.destinationWorldPosition;
            if (spec.useDestinationZoneCenter && zoneManager != null
                && zoneManager.TryGetZone(spec.destinationZoneName, out var destZone))
            {
                // ZoneManager.GetZoneRect returns RectInt (tile coordinates);
                // convert to world-unit centre via the zone's tile size.
                RectInt tileRect = zoneManager.GetZoneRect(destZone);
                float tileSize = zoneManager.TileSize > 0f ? zoneManager.TileSize : 1f;
                dest = new Vector2(
                    (tileRect.xMin + tileRect.width  * 0.5f) * tileSize,
                    (tileRect.yMin + tileRect.height * 0.5f) * tileSize);
            }

            // Inject inspector fields via reflection so the production
            // ZonePortal type stays designer-only.
            ApplyDestination(portal, spec.destinationZoneName, dest, radius);
            return go;
        }

        private static void ApplyDestination(ZonePortal portal, string destinationZoneName,
            Vector2 destinationWorldPos, float activationRadius)
        {
            var t = typeof(ZonePortal);
            const BindingFlags F = BindingFlags.Instance | BindingFlags.NonPublic;

            // Store destinationZoneName as the overlay filename hint — the
            // ZonePortal runtime path uses ZoneManager.ForceZoneName(...) on
            // activation, so a bare zone name (no extension) is what it
            // expects. Adding ".overlay.json" would route it down the file-
            // load path which we don't want for in-slot zone teleports.
            TrySetString(t, F, portal, "destinationOverlay", destinationZoneName ?? string.Empty);
            TrySetVector2(t, F, portal, "teleportPosition", destinationWorldPos);
            TrySetBool(t, F, portal, "isSceneTransition", false);
            TrySetFloat(t, F, portal, "activationRadius", activationRadius);
        }

        private static void TrySetString(System.Type t, BindingFlags f, object target, string fieldName, string value)
        {
            var fi = t.GetField(fieldName, f);
            if (fi == null) { LogMissing(fieldName); return; }
            fi.SetValue(target, value);
        }

        private static void TrySetVector2(System.Type t, BindingFlags f, object target, string fieldName, Vector2 value)
        {
            var fi = t.GetField(fieldName, f);
            if (fi == null) { LogMissing(fieldName); return; }
            fi.SetValue(target, value);
        }

        private static void TrySetBool(System.Type t, BindingFlags f, object target, string fieldName, bool value)
        {
            var fi = t.GetField(fieldName, f);
            if (fi == null) { LogMissing(fieldName); return; }
            fi.SetValue(target, value);
        }

        private static void TrySetFloat(System.Type t, BindingFlags f, object target, string fieldName, float value)
        {
            var fi = t.GetField(fieldName, f);
            if (fi == null) { LogMissing(fieldName); return; }
            fi.SetValue(target, value);
        }

        private static void LogMissing(string fieldName)
        {
            Debug.LogWarning($"[ZonePortalFactory] Missing private field '{fieldName}' on ZonePortal — " +
                             $"runtime placement may not bind that property until the factory is updated.");
        }
    }
}
