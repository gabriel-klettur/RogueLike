using System;
using System.Collections.Generic;

namespace Valkur.Gameplay.World
{
    /// <summary>
    /// What one playthrough did to one placed building.
    ///
    /// <para>The key is (slot, zone, instanceId) and all three are load-bearing. The instance
    /// id alone is not unique across map slots, and the zone is what makes a record readable
    /// by a human debugging a save. The slot is carried in the RECORD rather than expressed
    /// as a directory so one run can hold damage for several slots without a lookup having to
    /// open several files.</para>
    /// </summary>
    [Serializable]
    public class WorldDamageRecord
    {
        /// <summary>Map Editor slot this building belongs to, as <c>MapEditorActiveSlot</c> spells it.</summary>
        public string slot;

        /// <summary>Zone name, for readability and for scoping a partial restore.</summary>
        public string zone;

        /// <summary>The building's own instance id, stable across sessions.</summary>
        public int instanceId;

        /// <summary>
        /// Durability left. Written even for an untouched building once anything about it is
        /// recorded, because a half-chopped tree that came back full would silently undo the
        /// player's work — which is the same defect as not persisting at all, only harder to
        /// notice.
        /// </summary>
        public int durability = -1;

        /// <summary>Charges left on a Deplete-mode node. -1 when the building has no charge model.</summary>
        public int charges = -1;

        /// <summary>True once it has been broken. A stump, not a tree.</summary>
        public bool destroyed;

        /// <summary>
        /// When it comes back, as seconds of <c>DateTime.UtcNow</c> ticks converted to a Unix
        /// timestamp. Zero means never.
        ///
        /// <para>Wall-clock rather than <c>Time.time</c>, which restarts at zero every Play
        /// session — a regrow expressed in session time would either fire the instant the
        /// player reloads or never fire at all, depending on the sign of the comparison.</para>
        /// </summary>
        public double regrowAtUnix;

        /// <summary>
        /// When a SPENT Deplete-mode node refills, on the same wall clock.
        ///
        /// <para>Deliberately not the same field as <see cref="regrowAtUnix"/>. A shipped tree
        /// is BOTH destructible and harvestable, so one field would carry two meanings on the
        /// same record and the two clocks would overwrite each other — a felled tree's regrow
        /// deadline replaced by a harvest deadline it does not have, or the reverse.</para>
        /// </summary>
        public double nodeRegrowAtUnix;
    }

    /// <summary>
    /// The serialized document. A wrapper class rather than a bare list because
    /// <c>JsonUtility</c> cannot serialize a top-level array, and because a schema number has
    /// to live somewhere a migration can read it.
    /// </summary>
    [Serializable]
    public class WorldDamageFile
    {
        /// <summary>Bumped whenever the record shape changes in a way a reader must know about.</summary>
        public int schema = 1;

        public List<WorldDamageRecord> records = new List<WorldDamageRecord>();
    }
}
