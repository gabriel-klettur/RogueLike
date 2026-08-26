using UnityEngine;
using Valkur.Data;

namespace Valkur.Gameplay.World
{
    /// <summary>
    /// Attaches a <see cref="BuildingDoor"/> to a placed building. The sibling of
    /// <c>WorldLightLoader.RegisterDerivedLight</c>: a template field plus a per-instance
    /// record makes a placement grow a runtime child object, rather than every author
    /// having to remember to drop a matching thing next to each building by hand.
    ///
    /// Attachment needs BOTH halves and says so when only one is present:
    ///   • the TEMPLATE must declare a doorway (that is where the art has one);
    ///   • the INSTANCE must carry a destination (that is what makes this house lead
    ///     somewhere and the identical house next door lead nowhere).
    /// A destination aimed at a template with no doorway is an authoring mistake and is
    /// reported. A doorway with no destination is the normal resting state of every
    /// un-assigned house and is silent.
    /// </summary>
    public static class BuildingDoorFactory
    {
        /// <summary>
        /// Create (or re-configure) the doorway child on <paramref name="owner"/>.
        /// Returns null when nothing was attached. Safe to call repeatedly: an existing
        /// doorway child is reused rather than duplicated, which is what makes this usable
        /// from the F10 editor's live re-apply path as well as from load.
        /// </summary>
        public static BuildingDoor TryAttach(BuildingObject owner, BuildingDoorSpec spec)
        {
            if (owner == null) return null;

            bool hasDestination = spec != null && spec.IsValid;

            if (!owner.TemplateDeclaresDoor)
            {
                if (hasDestination)
                {
                    Debug.LogWarning(
                        $"[BuildingDoorFactory] Instance {owner.InstanceId} points at " +
                        $"'{spec.target}' but its template " +
                        $"'{(owner.Template != null ? owner.Template.name : "<null>")}' does not " +
                        "declare a doorway (hasDoor is false), so there is nowhere on the art to " +
                        "put it. Enable hasDoor on the template or clear overrides.door.",
                        owner);
                }
                return null;
            }

            if (!hasDestination)
            {
                // The art has a doorway, this placement just does not lead anywhere yet.
                owner.DoorSpec = null;
                Remove(owner);
                return null;
            }

            owner.DoorSpec = spec.Clone();

            var door = Find(owner);
            if (door == null)
            {
                var go = new GameObject(BuildingDoor.CHILD_NAME);
                go.transform.SetParent(owner.transform, worldPositionStays: false);
                door = go.AddComponent<BuildingDoor>();
            }

            door.Configure(owner, owner.DoorSpec);

            if (!door.RefreshGeometry())
            {
                Debug.LogWarning(
                    $"[BuildingDoorFactory] Instance {owner.InstanceId} declares a doorway but its " +
                    "world bounds are not resolvable yet, so the doorway has nowhere to sit. Call " +
                    "RefreshGeometry() again once the building's renderers exist.",
                    owner);
            }

            return door;
        }

        /// <summary>Existing doorway child of this building, or null.</summary>
        public static BuildingDoor Find(BuildingObject owner)
            => owner != null ? owner.GetComponentInChildren<BuildingDoor>(includeInactive: true) : null;

        /// <summary>Destroy the doorway child, if any. No-op when there is none.</summary>
        public static void Remove(BuildingObject owner)
        {
            var door = Find(owner);
            if (door == null) return;

            // Plain Destroy() throws "may not be called from edit mode", which the F10
            // editor and the EditMode suite both run in.
            if (Application.isPlaying) Object.Destroy(door.gameObject);
            else                       Object.DestroyImmediate(door.gameObject);
        }

        /// <summary>
        /// Re-derive the doorway trigger from the building's current bounds. Call after any
        /// path that moves or resizes the building outside the load pipeline — the same
        /// contract <c>BuildingObject.RefreshSorting</c> carries for the Y-sort.
        /// </summary>
        public static void RefreshGeometry(BuildingObject owner)
        {
            var door = Find(owner);
            if (door != null) door.RefreshGeometry();
        }
    }
}
