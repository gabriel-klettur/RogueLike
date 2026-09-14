namespace Valkur.Data.WorldGen
{
    /// <summary>What a town lot may hold.</summary>
    public enum WorldTownPieceKind
    {
        House = 0,
        Shop = 1,
        /// <summary>The fountain or well at the centre of the plaza.</summary>
        Centerpiece = 2,
        Lamp = 3,
        Stall = 4,

        /// <summary>A tree in the wild. Placed by <see cref="WorldTrees"/>, never inside a town.</summary>
        Tree = 5,

        /// <summary>
        /// The resurrection altar. Exactly one, in the STARTING town, beside the main street nearest
        /// the plaza: a world with no altar leaves the death rescue as the only way back.
        /// </summary>
        Altar = 6,
    }
}
