using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;
using Valkur.Gameplay.World;

namespace Valkur.Gameplay.Buildings
{
    /// <summary>
    /// Pure-static helper that, given a list of placed BuildingObject instances,
    /// returns the subset matching a (templateId, scope) filter for the Erase tool.
    ///
    /// Two scopes are supported:
    ///   1. By Zone        — match where Template.templateId == templateId AND
    ///                       ZoneName == zoneId (OrdinalIgnoreCase per CLAUDE.md zone rule).
    ///   2. By Tiles Area  — match where Template.templateId == templateId AND
    ///                       tilemap.WorldToCell(b.transform.position) is contained
    ///                       in a precomputed flood-fill set of cells.
    ///
    /// Stateless and side-effect-free so it is fully testable in EditMode without
    /// any scene fixtures beyond the BuildingObject + Tilemap inputs.
    /// </summary>
    public static class BuildingsEraseMatcher
    {
        public static List<BuildingObject> MatchesByZone(
            IReadOnlyList<BuildingObject> all, int templateId, string zoneId)
        {
            var result = new List<BuildingObject>();
            if (all == null) return result;
            for (int i = 0; i < all.Count; i++)
            {
                var b = all[i];
                if (b == null || b.Template == null) continue;
                if (b.Template.templateId != templateId) continue;
                if (!string.Equals(b.ZoneName, zoneId, StringComparison.OrdinalIgnoreCase)) continue;
                result.Add(b);
            }
            return result;
        }

        public static List<BuildingObject> MatchesByTilesArea(
            IReadOnlyList<BuildingObject> all, int templateId,
            HashSet<Vector3Int> areaCells, Tilemap tilemap)
        {
            var result = new List<BuildingObject>();
            if (all == null || areaCells == null || tilemap == null) return result;
            for (int i = 0; i < all.Count; i++)
            {
                var b = all[i];
                if (b == null || b.Template == null) continue;
                if (b.Template.templateId != templateId) continue;
                var cell = tilemap.WorldToCell(b.transform.position);
                if (!areaCells.Contains(cell)) continue;
                result.Add(b);
            }
            return result;
        }
    }
}
