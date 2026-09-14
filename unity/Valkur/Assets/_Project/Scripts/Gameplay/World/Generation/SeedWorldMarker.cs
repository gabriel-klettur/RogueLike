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
        public string bakedAtUtc;
        public string settingsJson;
    }
}
