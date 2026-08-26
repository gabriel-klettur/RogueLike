using System;
using UnityEngine;

namespace Valkur.Data
{
    /// <summary>
    /// Per-instance door destination for a placed building — "this house leads THERE".
    ///
    /// Deliberately split from the door's GEOMETRY, which lives on
    /// <see cref="BuildingTemplateData"/> as a normalized anchor:
    ///
    ///   • WHERE the door is drawn is a property of the ART. Every placement of
    ///     <c>house_a</c> has its doorway on the same pixels, so the anchor belongs to the
    ///     template — the same split <see cref="BuildingTemplateData.lightPresetKey"/> and
    ///     <see cref="BuildingTemplateData.lightOffsetNormalized"/> already use for fixtures.
    ///   • WHERE the door LEADS is a property of the PLACEMENT. Two houses of the same type
    ///     need two different interiors, so the destination can only be per instance.
    ///
    /// Stored inside the instance's <c>overrides</c> block in
    /// <c>buildings_instances.json</c> under the key <c>door</c>, so it travels with the
    /// building when it is moved, duplicated or deleted. A separate side file would leave
    /// orphan records behind on every delete.
    ///
    /// The destination is an OVERLAY FILENAME, matching what
    /// <c>ZonePortal.destinationOverlay</c> consumes — a door is an authored anchor that
    /// emits the existing transition, never a second transition system.
    /// </summary>
    [Serializable]
    public sealed class BuildingDoorSpec
    {
        [Tooltip("Overlay filename in StreamingAssets/Maps/ this door leads to, " +
                 "e.g. 'house_a_int.overlay.json'. Empty = the door is inert.")]
        public string target = "";

        [Tooltip("Ignore spawnX/spawnY and let the destination decide where the player lands.")]
        public bool useDefaultSpawn;

        [Tooltip("World-unit X the player lands on in the destination. Ignored when useDefaultSpawn.")]
        public float spawnX;

        [Tooltip("World-unit Y the player lands on in the destination. Ignored when useDefaultSpawn.")]
        public float spawnY;

        [Tooltip("Optional label for UI (minimap tooltip, Phase 4 interaction prompt). " +
                 "Empty = the caller picks a default.")]
        public string prompt = "";

        /// <summary>
        /// A door with no destination does nothing, and a door that does nothing must not
        /// be attached — an inert trigger sitting on a doorway reads as a broken door.
        /// </summary>
        public bool IsValid => !string.IsNullOrWhiteSpace(target);

        /// <summary>Authored landing position. Meaningless when <see cref="useDefaultSpawn"/>.</summary>
        public Vector2 SpawnPosition => new Vector2(spawnX, spawnY);

        /// <summary>
        /// Deep copy. Specs are handed from the parsed DTO to the live
        /// <c>BuildingObject</c> and back out to the serializer; sharing one instance
        /// between a parsed record and a scene object would let an editor edit rewrite
        /// data the loader still considers pristine.
        /// </summary>
        public BuildingDoorSpec Clone() => new BuildingDoorSpec
        {
            target          = target,
            useDefaultSpawn = useDefaultSpawn,
            spawnX          = spawnX,
            spawnY          = spawnY,
            prompt          = prompt,
        };
    }
}
