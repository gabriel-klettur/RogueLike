using System.Collections.Generic;
using UnityEngine;

namespace Valkur.Data.WorldGen
{
    /// <summary>
    /// Fills a town's layout with buildings: a centrepiece and stalls on the plaza, lamps at its
    /// corners and along the main streets, and houses and shops fronting every street.
    ///
    /// <para><b>No building overlaps a street, the plaza's edge, another building or unwalkable
    /// ground, by construction.</b> Every candidate rectangle is tested against one occupancy set
    /// before it is placed, and a placed building also reserves a one-tile ring around itself so
    /// two houses never touch — that ring is what keeps a passage between neighbours that the
    /// collision grid would otherwise seal.</para>
    ///
    /// <para><b>Shops cluster at the centre, houses spread outward</b> — within 45 % of the radius a
    /// lot is a shop more often than not, beyond it mostly a house. That is the single rule that
    /// makes a generated town read as having a high street.</para>
    /// </summary>
    public static class WorldTownLots
    {
        private const int LotSalt = 8;
        private const float ShopCoreFraction = 0.45f;
        private const double ShopChanceInCore = 0.6;
        private const double ShopChanceOutside = 0.15;
        private const int LampSpacing = 8;
        private const int OptionTries = 4;

        public static List<WorldTownPlacement> Place(WorldTown town, WorldClimate climate, HashSet<Vector2Int> riverTiles,
                                                      IReadOnlyList<WorldTownBuildingOption> options)
        {
            var placements = new List<WorldTownPlacement>();
            if (town == null || options == null || options.Count == 0) return placements;

            var byKind = new Dictionary<WorldTownPieceKind, List<WorldTownBuildingOption>>();
            foreach (var o in options)
            {
                if (!byKind.TryGetValue(o.Kind, out var list)) byKind[o.Kind] = list = new List<WorldTownBuildingOption>();
                list.Add(o);
            }

            var ctx = new Context(town, climate, riverTiles, placements,
                new System.Random(WorldSeed.Derive(climate.Settings.seed, LotSalt + town.Index * 31)), byKind);

            PlacePlaza(ctx);
            if (town.IsStart) PlaceAltar(ctx);
            // Main arms first: they are the high street, and they should get first pick of the lots.
            for (int i = 0; i < town.Streets.Count; i++) FrontStreet(ctx, town.Streets[i]);
            for (int i = 0; i < 4 && i < town.Streets.Count; i++) LightStreet(ctx, town.Streets[i]);

            return placements;
        }

        private sealed class Context
        {
            public readonly WorldTown Town;
            public readonly WorldClimate Climate;
            public readonly HashSet<Vector2Int> Rivers;
            public readonly List<WorldTownPlacement> Placements;
            public readonly System.Random Rng;
            public readonly Dictionary<WorldTownPieceKind, List<WorldTownBuildingOption>> ByKind;
            public readonly HashSet<Vector2Int> Occupied = new HashSet<Vector2Int>();

            public Context(WorldTown town, WorldClimate climate, HashSet<Vector2Int> rivers,
                           List<WorldTownPlacement> placements, System.Random rng,
                           Dictionary<WorldTownPieceKind, List<WorldTownBuildingOption>> byKind)
            {
                Town = town; Climate = climate; Rivers = rivers; Placements = placements; Rng = rng; ByKind = byKind;
            }
        }

        private static void PlacePlaza(Context ctx)
        {
            var plaza = ctx.Town.Plaza;
            var c = ctx.Town.Center;

            if (TryPick(ctx, WorldTownPieceKind.Centerpiece, out var centre))
                TryPlace(ctx, centre, new RectInt(c.x - centre.WidthTiles / 2, c.y - centre.HeightTiles / 2,
                    centre.WidthTiles, centre.HeightTiles), onPlaza: true);

            // Two stalls in opposite corners of the plaza, leaving the crossing of the arms clear.
            if (TryPick(ctx, WorldTownPieceKind.Stall, out var stallA))
                TryPlace(ctx, stallA, new RectInt(plaza.xMin, plaza.yMax - stallA.HeightTiles, stallA.WidthTiles, stallA.HeightTiles), onPlaza: true);
            if (TryPick(ctx, WorldTownPieceKind.Stall, out var stallB))
                TryPlace(ctx, stallB, new RectInt(plaza.xMax - stallB.WidthTiles, plaza.yMin, stallB.WidthTiles, stallB.HeightTiles), onPlaza: true);

            if (TryPick(ctx, WorldTownPieceKind.Lamp, out var lamp))
            {
                TryPlace(ctx, lamp, new RectInt(plaza.xMin - 1 - lamp.WidthTiles, plaza.yMax + 1, lamp.WidthTiles, lamp.HeightTiles), onPlaza: false);
                TryPlace(ctx, lamp, new RectInt(plaza.xMax + 1, plaza.yMax + 1, lamp.WidthTiles, lamp.HeightTiles), onPlaza: false);
                TryPlace(ctx, lamp, new RectInt(plaza.xMin - 1 - lamp.WidthTiles, plaza.yMin - 1 - lamp.HeightTiles, lamp.WidthTiles, lamp.HeightTiles), onPlaza: false);
                TryPlace(ctx, lamp, new RectInt(plaza.xMax + 1, plaza.yMin - 1 - lamp.HeightTiles, lamp.WidthTiles, lamp.HeightTiles), onPlaza: false);
            }
        }

        /// <summary>
        /// The altar goes before any house, fronting a main arm as close to the plaza as it fits:
        /// a spirit revived there should be a few steps from where the run began, and letting the
        /// houses take the lots first would leave an 8x8 arch no room anywhere near the centre.
        /// </summary>
        private static void PlaceAltar(Context ctx)
        {
            if (!TryPick(ctx, WorldTownPieceKind.Altar, out var altar)) return;
            var c = ctx.Town.Center;
            for (int i = 0; i < 4 && i < ctx.Town.Streets.Count; i++)
            {
                var street = ctx.Town.Streets[i];
                bool horizontal = street.width >= street.height;
                int start = horizontal ? street.xMin : street.yMin;
                int end = horizontal ? street.xMax : street.yMax;
                int length = end - start;

                // Along the arm from the plaza end outward.
                bool fromLow = horizontal ? Mathf.Abs(street.xMin - c.x) < Mathf.Abs(street.xMax - c.x)
                                          : Mathf.Abs(street.yMin - c.y) < Mathf.Abs(street.yMax - c.y);
                for (int step = 0; step < length; step++)
                {
                    int along = fromLow ? start + step : end - step - (horizontal ? altar.WidthTiles : altar.HeightTiles);
                    for (int side = 0; side < 2; side++)
                    {
                        RectInt rect = horizontal
                            ? (side == 0 ? new RectInt(along, street.yMax + 1, altar.WidthTiles, altar.HeightTiles)
                                         : new RectInt(along, street.yMin - 1 - altar.HeightTiles, altar.WidthTiles, altar.HeightTiles))
                            : (side == 0 ? new RectInt(street.xMax + 1, along, altar.WidthTiles, altar.HeightTiles)
                                         : new RectInt(street.xMin - 1 - altar.WidthTiles, along, altar.WidthTiles, altar.HeightTiles));
                        if (TryPlace(ctx, altar, rect, onPlaza: false)) return;
                    }
                }
            }
        }

        private static void FrontStreet(Context ctx, RectInt street)
        {
            bool horizontal = street.width >= street.height;
            for (int side = 0; side < 2; side++)
            {
                int along = horizontal ? street.xMin : street.yMin;
                int end = horizontal ? street.xMax : street.yMax;
                while (along < end)
                {
                    int advance = 1;
                    for (int t = 0; t < OptionTries; t++)
                    {
                        var kind = ChooseLotKind(ctx, horizontal ? new Vector2Int(along, street.y) : new Vector2Int(street.x, along));
                        if (!TryPick(ctx, kind, out var o)) continue;

                        RectInt rect;
                        if (horizontal)
                            rect = side == 0
                                ? new RectInt(along, street.yMax + 1, o.WidthTiles, o.HeightTiles)
                                : new RectInt(along, street.yMin - 1 - o.HeightTiles, o.WidthTiles, o.HeightTiles);
                        else
                            rect = side == 0
                                ? new RectInt(street.xMax + 1, along, o.WidthTiles, o.HeightTiles)
                                : new RectInt(street.xMin - 1 - o.WidthTiles, along, o.WidthTiles, o.HeightTiles);

                        if (TryPlace(ctx, o, rect, onPlaza: false))
                        {
                            advance = (horizontal ? o.WidthTiles : o.HeightTiles) + 1;
                            break;
                        }
                    }
                    along += advance;
                }
            }
        }

        private static void LightStreet(Context ctx, RectInt street)
        {
            if (!TryPick(ctx, WorldTownPieceKind.Lamp, out var lamp)) return;
            bool horizontal = street.width >= street.height;
            int end = horizontal ? street.xMax : street.yMax;
            for (int along = (horizontal ? street.xMin : street.yMin) + 2; along < end; along += LampSpacing)
            {
                var rect = horizontal
                    ? new RectInt(along, street.yMax, lamp.WidthTiles, lamp.HeightTiles)
                    : new RectInt(street.xMax, along, lamp.WidthTiles, lamp.HeightTiles);
                TryPlace(ctx, lamp, rect, onPlaza: false);
            }
        }

        private static WorldTownPieceKind ChooseLotKind(Context ctx, Vector2Int at)
        {
            float d = Mathf.Sqrt((at - ctx.Town.Center).sqrMagnitude);
            double shopChance = d <= ctx.Town.Radius * ShopCoreFraction ? ShopChanceInCore : ShopChanceOutside;
            return ctx.Rng.NextDouble() < shopChance ? WorldTownPieceKind.Shop : WorldTownPieceKind.House;
        }

        private static bool TryPick(Context ctx, WorldTownPieceKind kind, out WorldTownBuildingOption option)
        {
            option = default;
            if (!ctx.ByKind.TryGetValue(kind, out var list) || list.Count == 0)
            {
                // A catalogue with no shops still gets a town: fall back to houses, never to nothing.
                if (kind != WorldTownPieceKind.Shop || !ctx.ByKind.TryGetValue(WorldTownPieceKind.House, out list) || list.Count == 0)
                    return false;
            }
            option = list[ctx.Rng.Next(list.Count)];
            return true;
        }

        private static bool TryPlace(Context ctx, WorldTownBuildingOption option, RectInt rect, bool onPlaza)
        {
            if (!Fits(ctx, rect, onPlaza)) return false;

            ctx.Placements.Add(new WorldTownPlacement(ctx.Town.Index, option, rect));
            // Plaza pieces reserve only themselves; a street building also its ring, so neighbours never touch.
            int ring = onPlaza ? 0 : 1;
            for (int y = rect.yMin - ring; y < rect.yMax + ring; y++)
                for (int x = rect.xMin - ring; x < rect.xMax + ring; x++)
                    ctx.Occupied.Add(new Vector2Int(x, y));
            return true;
        }

        private static bool Fits(Context ctx, RectInt rect, bool onPlaza)
        {
            var plaza = ctx.Town.Plaza;
            for (int y = rect.yMin; y < rect.yMax; y++)
                for (int x = rect.xMin; x < rect.xMax; x++)
                {
                    var t = new Vector2Int(x, y);
                    if (ctx.Occupied.Contains(t)) return false;
                    if (onPlaza)
                    {
                        if (!plaza.Contains(t)) return false;
                    }
                    else
                    {
                        if (ctx.Town.StreetTiles.Contains(t)) return false;
                        if (!ctx.Town.Contains(t, 4)) return false;
                    }
                    if (!WorldTowns.IsBuildable(ctx.Climate, ctx.Rivers, t)) return false;
                }
            return true;
        }
    }
}
