using System;

namespace Valkur.Gameplay.World.Generation
{
    /// <summary>
    /// Written beside a baked world's overlays. Its presence is what lets Seed World overwrite
    /// that slot on the next bake; a slot without it was made by a person and is never touched.
    /// It also records the settings the world was built from, so a bake can be reproduced.
    /// </summary>
    [Serializable]
    public sealed class SeedWorldMarker
    {
        public const int CurrentFormat = 1;

        public int format = CurrentFormat;
        public int seed;

        /// <summary>
        /// True when the slot holds NO ground: every zone is generated from <see cref="settingsJson"/>
        /// when the player comes near, and only zones somebody edited are on disk. False (and absent
        /// in markers written before phase 5) means every zone was baked to an overlay file.
        /// </summary>
        public bool live;

        /// <summary>Zone side in tiles the world was planned with; 0 in older markers means 50.</summary>
        public int zoneSize;
        public string bakedAtUtc;
        public string settingsJson;
    }
}
