using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;
using Valkur.Data;

namespace Valkur.Gameplay.World
{
    /// <summary>
    /// Answers "is this cell open water" for a painted tilemap.
    ///
    /// <para>WHY THIS IS NOT LIKE A TREE. Chopping and mining hang off a
    /// <c>BuildingObject</c> — a placed instance with an id, a transform and a lifetime, which
    /// is what lets a harvest node be a component and be held in a registry. Water has none of
    /// that: it is painted tilemap cells, and a shoreline can be thousands of them. There is
    /// nothing to attach a component to and nothing worth registering one per cell.</para>
    ///
    /// <para>So the question is answered by LOOKING at the tilemap instead, and the only thing
    /// this class has to get right is which sprites count as water.</para>
    /// </summary>
    public static class WaterTileIndex
    {
        /// <summary>
        /// The terrain names that can be fished. Both ship in <c>TerrainCatalog</c> —
        /// <c>GetUniqueTerrains</c> returns grass, dirt, rock, ocean, water, sand, stone, lava.
        /// </summary>
        [Valkur.Core.SelfHealingStatic("Immutable table of string literals. It holds no Unity " +
            "objects, is never written after initialisation, and cannot therefore carry a " +
            "destroyed reference or a stale registration into the next Play session — which " +
            "is what the reset rule exists to prevent. A `static readonly` array also cannot " +
            "be reset in a way the IL scanner recognises: it accepts stsfld or field.Clear(), " +
            "and Array.Clear passes the field as an ARGUMENT, so satisfying the ratchet would " +
            "mean dropping readonly and reassigning a table that never changes.")]
        private static readonly string[] WaterTerrains = { "water", "ocean" };

        // Domain Reload is OFF, so a set built against sprites from the previous session would
        // carry into the next one holding unloaded objects. Assigning null is a plain stsfld,
        // the only reset shape DomainReloadStaticResetTests recognises.
        private static HashSet<Sprite> _waterSprites;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => _waterSprites = null;

        /// <summary>How many distinct sprites are known to be open water. 0 before the first query.</summary>
        public static int KnownWaterSpriteCount => _waterSprites?.Count ?? 0;

        /// <summary>Drop the cached set — for tests, and after a catalog edit.</summary>
        public static void Invalidate() => _waterSprites = null;

        /// <summary>
        /// True when <paramref name="sprite"/> is a fully-water tile.
        /// </summary>
        public static bool IsWater(Sprite sprite)
        {
            if (sprite == null) return false;
            EnsureBuilt();
            return _waterSprites.Contains(sprite);
        }

        /// <summary>
        /// The nearest open-water cell to <paramref name="worldPosition"/>, searched outward
        /// over a square of <paramref name="radiusCells"/>. Returns false when there is none.
        ///
        /// <para>Searched rather than indexed on purpose. An index would have to be rebuilt
        /// every time the Tile Editor painted a cell or a map slot was swapped, and the query
        /// is tiny: a radius of 3 is 49 cells, each one a <c>GetSprite</c> and a set lookup,
        /// run once per frame for one player.</para>
        /// </summary>
        public static bool TryFindNearestWaterCell(Tilemap tilemap, Vector2 worldPosition,
            int radiusCells, out Vector3Int cell, out Vector2 cellCentre)
        {
            cell = default;
            cellCentre = default;
            if (tilemap == null) return false;

            EnsureBuilt();
            if (_waterSprites.Count == 0) return false;

            Vector3Int origin = tilemap.WorldToCell(worldPosition);
            float bestSqr = float.MaxValue;
            bool found = false;

            for (int dy = -radiusCells; dy <= radiusCells; dy++)
            for (int dx = -radiusCells; dx <= radiusCells; dx++)
            {
                var probe = new Vector3Int(origin.x + dx, origin.y + dy, origin.z);
                var sprite = tilemap.GetSprite(probe);
                if (sprite == null || !_waterSprites.Contains(sprite)) continue;

                // Measured to the CELL CENTRE, not to the cell's corner: the prompt and the
                // cast both aim at the water, and a corner is up to a cell away from it.
                Vector2 centre = tilemap.GetCellCenterWorld(probe);
                float sqr = (centre - worldPosition).sqrMagnitude;
                if (sqr >= bestSqr) continue;

                bestSqr = sqr;
                cell = probe;
                cellCentre = centre;
                found = true;
            }

            return found;
        }

        // ── Building the set ───────────────────────────────────────────────────────

        private static void EnsureBuilt()
        {
            if (_waterSprites != null) return;
            _waterSprites = new HashSet<Sprite>();

            var catalog = Resources.Load<TerrainCatalog>("TerrainCatalog");
            if (catalog == null)
            {
                Debug.LogWarning("[WaterTileIndex] No TerrainCatalog under Resources — nothing " +
                                 "will be recognised as water.");
                return;
            }

            foreach (var ruleset in catalog.Rulesets)
            {
                if (ruleset == null) continue;
                bool primaryIsWater = IsWaterTerrain(ruleset.TerrainPrimary);
                bool secondaryIsWater = IsWaterTerrain(ruleset.TerrainSecondary);
                if (!primaryIsWater && !secondaryIsWater) continue;

                CollectPureWaterVariants(ruleset, primaryIsWater, secondaryIsWater);
            }
        }

        /// <summary>
        /// Take only the variants of the slot that is ENTIRELY water, never every variant of a
        /// ruleset that merely contains some.
        ///
        /// <para>This is where the auto-tile polarity trap lives, and getting it wrong is
        /// silent. <c>rock_water</c> is a transition sheet: most of its sixteen slots are part
        /// rock and part water, and a cell wearing one of them is a SHORE, not something a line
        /// can be cast into. Adding the whole ruleset would make every rock cell beside a lake
        /// fishable.</para>
        ///
        /// <para>Which slot is the pure one depends on which side of the transition water is
        /// on, and that is not guessable — <c>TerrainTileResolver</c> keys corner slots by the
        /// SECONDARY terrain, so a fully-secondary cell is <c>CornerFull</c> and a
        /// fully-primary one is <c>CornerNone</c>. Both cases are handled explicitly rather
        /// than assumed.</para>
        /// </summary>
        private static void CollectPureWaterVariants(TilesetRuleset ruleset,
            bool primaryIsWater, bool secondaryIsWater)
        {
            // A base sheet declares no secondary at all, so every one of its variants is the
            // primary terrain and all of them are water.
            if (!ruleset.IsTransition)
            {
                if (!primaryIsWater) return;
                foreach (Blob16Slot slot in System.Enum.GetValues(typeof(Blob16Slot)))
                    Add(ruleset.GetVariants(slot));
                foreach (Corner16Slot slot in System.Enum.GetValues(typeof(Corner16Slot)))
                    Add(ruleset.GetVariants(slot));
                return;
            }

            // Corner16 keys its slots by the secondary terrain.
            Corner16Slot pure = secondaryIsWater ? Corner16Slot.CornerFull : Corner16Slot.CornerNone;
            Add(ruleset.GetVariants(pure));

            // Blob16 sheets carry the primary terrain in their fully-connected slot; a
            // transition sheet whose PRIMARY is the water contributes that one.
            if (primaryIsWater) Add(ruleset.GetVariants(Blob16Slot.Isolated));
        }

        private static void Add(Sprite[] variants)
        {
            if (variants == null) return;
            foreach (var s in variants) if (s != null) _waterSprites.Add(s);
        }

        private static bool IsWaterTerrain(string terrain)
        {
            if (string.IsNullOrEmpty(terrain)) return false;
            foreach (var w in WaterTerrains)
                if (string.Equals(terrain, w, System.StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }
    }
}
