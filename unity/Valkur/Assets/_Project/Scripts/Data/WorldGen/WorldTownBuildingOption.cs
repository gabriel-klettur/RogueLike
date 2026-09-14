namespace Valkur.Data.WorldGen
{
    /// <summary>
    /// One building a town may use, reduced to what layout needs: its id and how many TILES its
    /// sprite covers. The planner is pure, so Gameplay translates catalogue templates into these.
    /// </summary>
    public readonly struct WorldTownBuildingOption
    {
        public readonly int TemplateId;
        public readonly int WidthTiles;
        public readonly int HeightTiles;
        public readonly WorldTownPieceKind Kind;

        public WorldTownBuildingOption(int templateId, int widthTiles, int heightTiles, WorldTownPieceKind kind)
        {
            TemplateId = templateId;
            WidthTiles = widthTiles < 1 ? 1 : widthTiles;
            HeightTiles = heightTiles < 1 ? 1 : heightTiles;
            Kind = kind;
        }
    }
}
