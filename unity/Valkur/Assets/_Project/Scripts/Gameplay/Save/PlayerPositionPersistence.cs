using System;
using UnityEngine;

namespace Valkur.Gameplay.Save
{
    /// <summary>
    /// The one answer to "which player position may be written down, and may a written one be
    /// spawned on".
    ///
    /// <para>WHAT WENT WRONG. <c>WorldTransitionService.RefuseWorldContentWrite</c> already
    /// stops the buildings, entities, lights and particles files being written while the base
    /// world is torn down for an interior. The PLAYER's position had no such guard, and it is
    /// the one value that is meaningless outside the world it was measured in: an interior is
    /// its own little grid loaded at the origin, so a save taken inside a fourteen-by-ten room
    /// records something like <c>(11, -8)</c> — and the next boot spawns the player at
    /// <c>(11, -8)</c> of the OUTDOOR world, which is off the map. Reproduced end to end
    /// before this existed; the checkpoint read
    /// <c>{"x":11.0,"y":-8.0,"zone":"house_interior_small.overlay"}</c> and the autosave's
    /// player block agreed with it.</para>
    ///
    /// <para>TWO HALVES, AND THE SECOND IS NOT REDUNDANT. <see cref="Resolve"/> keeps a bad
    /// position from being written; <see cref="IsUsableSpawn"/> keeps one that was already
    /// written from being spawned on. The second is what heals the saves poisoned before the
    /// first existed, and it is also the backstop for the one case the first cannot answer —
    /// a player who has been inside an interior since before the save system ever sampled the
    /// base world has no good position anywhere, and only the RESTORE side can decide to fall
    /// back to the world's own spawn.</para>
    ///
    /// <para>Pure and static on purpose: every rule here is decidable from a handful of values,
    /// and a policy that needed a scene to test is a policy that gets tested by playing.</para>
    /// </summary>
    public static class PlayerPositionPersistence
    {
        /// <summary>Where a player position came from. Carried into the log line, because
        /// "the save moved me" is otherwise indistinguishable from "the game moved me".</summary>
        public enum Source
        {
            /// <summary>The live transform, in the base world. The ordinary case.</summary>
            Live,
            /// <summary>The doorway the player walked through to reach the interior.</summary>
            ReturnPoint,
            /// <summary>The last position sampled while the base world was loaded.</summary>
            LastBaseWorld,
            /// <summary>Inside an interior with nothing better known. NOT a spawn point.</summary>
            Interior,
            /// <summary>On a trip to another map: the spot in Pepitoria the trip started from.</summary>
            ExcursionHome,
        }

        /// <summary>
        /// The record for a player who is away from Pepitoria on another map. It outranks every rule
        /// in <see cref="Resolve"/>: a generated world is not an interior of the base world, so neither
        /// its live position nor anything sampled in it is somewhere the base world can put the player
        /// — only the ticket's home is.
        /// </summary>
        public static Record AwayFromHome(Vector2 homePosition, string homeZone)
            => new Record(homePosition, homeZone, true, Source.ExcursionHome);

        /// <summary>
        /// A position and whether it is fit to be persisted as somewhere the player can be put
        /// back down.
        /// </summary>
        public readonly struct Record
        {
            public readonly Vector2 Position;
            public readonly string  Zone;
            public readonly bool    IsBaseWorld;
            public readonly Source  From;

            public Record(Vector2 position, string zone, bool isBaseWorld, Source from)
            {
                Position    = position;
                Zone        = zone ?? "";
                IsBaseWorld = isBaseWorld;
                From        = from;
            }
        }

        /// <summary>
        /// Which position to write down right now.
        ///
        /// <para>The order is the design. A RETURN POINT is the doorway the player walked
        /// through, so it is both in the base world and somewhere they can stand — the best
        /// answer available. Failing that, the LAST BASE-WORLD SAMPLE is where they were before
        /// stepping inside, which is the same place to within a stride. Only when neither
        /// exists is the interior position returned, and it is flagged <c>IsBaseWorld
        /// false</c> so a caller that can decline to write simply does not.</para>
        ///
        /// <para>A return point that is itself an interior is refused rather than used: this
        /// project does not nest interiors today, but the field exists and a nested one is not
        /// a base-world position however recently it was recorded.</para>
        /// </summary>
        public static Record Resolve(bool baseWorldSuspended,
                                     bool hasReturnPoint, bool returnIsBaseWorld, Vector2 returnPosition,
                                     bool hasLastBaseWorld, Vector2 lastBaseWorldPosition, string lastBaseWorldZone,
                                     Vector2 livePosition, string liveZone)
        {
            if (!baseWorldSuspended)
                return new Record(livePosition, liveZone, true, Source.Live);

            if (hasReturnPoint && returnIsBaseWorld)
                // The zone label follows the last base-world sample rather than being derived
                // from the return position: resolving a zone needs the zone database, this
                // function is pure, and the two positions are a doorway apart.
                return new Record(returnPosition, lastBaseWorldZone, true, Source.ReturnPoint);

            if (hasLastBaseWorld)
                return new Record(lastBaseWorldPosition, lastBaseWorldZone, true, Source.LastBaseWorld);

            return new Record(livePosition, liveZone, false, Source.Interior);
        }

        /// <summary>
        /// May a stored position be used to put the player down in the base world?
        ///
        /// <para>The test is the ZONE, not the coordinates. A bounds check would need the
        /// world's extent — which is exactly what is not loaded at the moment this is asked —
        /// and would pass an interior that happened to overlap the map. The zone label is
        /// already recorded by both writers, is drawn from the zone database for a base-world
        /// position, and is the overlay's FILE NAME for an interior, so the two populations do
        /// not overlap.</para>
        ///
        /// <para>AN EMPTY ZONE IS ACCEPTED, deliberately. It is what every checkpoint written
        /// before the zone label existed carries, and what a save written before a
        /// <c>ZoneManager</c> was resolvable carries. Refusing those would move the player of
        /// every older save to the lobby to fix a bug they do not have — the failure this is
        /// guarding against announces itself with a zone nobody has heard of, never with
        /// silence.</para>
        /// </summary>
        /// <param name="zone">The stored zone label.</param>
        /// <param name="isKnownBaseWorldZone">Answers whether the zone database holds that name.
        /// Null when no zone database is resolvable, which accepts everything — the caller has
        /// no way to judge and refusing on a missing dependency would strand the player.</param>
        public static bool IsUsableSpawn(string zone, Func<string, bool> isKnownBaseWorldZone,
                                         out string reason)
        {
            reason = null;

            if (string.IsNullOrEmpty(zone)) return true;
            if (isKnownBaseWorldZone == null) return true;
            if (isKnownBaseWorldZone(zone)) return true;

            reason = $"zone '{zone}' is not one of the base world's zones — it reads as a " +
                     "position recorded inside an interior, which is a different grid loaded " +
                     "at the origin. Spawning on it would put the player off the map.";
            return false;
        }
    }
}
