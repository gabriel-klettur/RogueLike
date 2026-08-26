using UnityEngine;
using Valkur.Data;

namespace Valkur.Gameplay.World
{
    /// <summary>
    /// Doorway aspect of <see cref="BuildingObject"/>: the per-instance destination and the
    /// world rect the runtime trigger occupies.
    ///
    /// The geometry itself lives in <see cref="BuildingDoorGeometry"/> and the world bounds
    /// come from <see cref="BuildingObject.TryGetWorldRect"/>, so a doorway is derived from
    /// the same rect the collision cells and the F10 outline already use. Nothing here
    /// re-derives sprite size, scale or split — that would be a fourth copy of geometry
    /// waiting to disagree with the other three.
    /// </summary>
    public partial class BuildingObject
    {
        // NOT [SerializeField] on purpose. Unity materialises a non-null instance for every
        // serialized [Serializable] class field, which would make "_doorSpec != null" true
        // for all 800+ placed buildings and attach an inert trigger to every one of them.
        // The spec is runtime data set by BuildingLoader from overrides.door (or by the F10
        // editor), and null genuinely means "this placement has no destination".
        private BuildingDoorSpec _doorSpec;

        /// <summary>
        /// Per-instance door destination, or null when this placement has none.
        /// Set by <see cref="BuildingLoader"/> at spawn and read back by the F10 save path.
        /// </summary>
        public BuildingDoorSpec DoorSpec
        {
            get => _doorSpec;
            set => _doorSpec = value;
        }

        /// <summary>The ART has a doorway drawn on it. Says nothing about where it leads.</summary>
        public bool TemplateDeclaresDoor => _template != null && _template.hasDoor;

        /// <summary>
        /// This placement has a doorway AND somewhere for it to lead. Both halves are
        /// required: a destination on a template with no doorway has nowhere to attach,
        /// and a doorway with no destination is an inert trigger that reads as a broken door.
        /// </summary>
        public bool HasUsableDoor => TemplateDeclaresDoor && _doorSpec != null && _doorSpec.IsValid;

        /// <summary>
        /// World-space rect of this building's doorway. False when the template declares no
        /// door, or when the building's own bounds are not resolvable yet (renderers not
        /// built) — callers must read that as "not ready", not as "no door".
        /// </summary>
        public bool TryGetDoorWorldRect(out Rect doorRect)
        {
            doorRect = default;
            if (_template == null || !_template.hasDoor) return false;
            if (!TryGetWorldRect(out var buildingRect)) return false;

            return BuildingDoorGeometry.TryGetDoorRect(
                buildingRect,
                _template.doorOffsetNormalized,
                _template.doorSizeNormalized,
                out doorRect);
        }
    }
}
