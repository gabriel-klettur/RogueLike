using UnityEngine;

namespace Valkur.Data.WorldGen
{
    /// <summary>A building placed in a town: which template, and the tile rectangle its sprite covers.</summary>
    public readonly struct WorldTownPlacement
    {
        public readonly int TownIndex;
        public readonly WorldTownBuildingOption Option;

        /// <summary>World tiles, origin at the map's bottom-left; the sprite's bottom edge is <c>yMin</c>.</summary>
        public readonly RectInt Rect;

        public WorldTownPlacement(int townIndex, WorldTownBuildingOption option, RectInt rect)
        {
            TownIndex = townIndex;
            Option = option;
            Rect = rect;
        }
    }
}
