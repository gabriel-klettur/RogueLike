using UnityEngine;

namespace Valkur.Core
{
    /// <summary>
    /// Central sorting layer and Z-layer configuration.
    /// Maps to Python's config_z_layer.py Z_LAYERS and map/model/layer.py Layer enum.
    /// 
    /// Unity sorting strategy:
    /// - SortingLayers define broad render groups (Background, Ground, Entities, Overhead, UI).
    /// - Within each SortingLayer, sortingOrder provides fine-grained control.
    /// - For entities on the same layer, Y-position determines draw order (lower Y = behind).
    /// </summary>
    public static class SortingConfig
    {
        // --- Sorting Layer Names (must match Tags & Layers > Sorting Layers in Unity) ---
        public const string LAYER_BACKGROUND = "Background";
        public const string LAYER_GROUND = "Ground";
        public const string LAYER_FLOOR_DECALS = "FloorDecals";
        public const string LAYER_OBJECTS_LOW = "ObjectsLow";
        public const string LAYER_WALLS_BOTTOM = "WallsBottom";
        public const string LAYER_ENTITIES = "Entities";
        public const string LAYER_DECORATIONS = "Decorations";
        public const string LAYER_WALLS_TOP = "WallsTop";
        public const string LAYER_OBJECTS_HIGH = "ObjectsHigh";
        public const string LAYER_PROJECTILES = "Projectiles";
        public const string LAYER_VFX = "VFX";
        public const string LAYER_OVERHEAD = "Overhead";
        /// <summary>
        /// Entities (Player, NPCs) whose <see cref="Gameplay.World.Layering.VisualLayerOccupant.CurrentVisualLayer"/>
        /// equals 8 (OverheadDetails) render here so they appear in front of every
        /// painted tilemap layer. Sits strictly between <see cref="LAYER_OVERHEAD"/>
        /// and <see cref="LAYER_UI_WORLD"/> so in-world UI (health bars, mana bars)
        /// still draws above the elevated player sprite.
        /// </summary>
        public const string LAYER_ENTITIES_OVERHEAD = "EntitiesOverhead";
        /// <summary>
        /// Entities on visual layer 7 (ObjectsHigh). Sits strictly between <c>PropsL7</c> and
        /// <see cref="LAYER_PROJECTILES"/>. It exists because the slot this visual layer used
        /// to borrow — Projectiles — is one the ambient light deliberately skips (spell art is
        /// emissive), so a lit character climbing to layer 7 rendered BLACK. Nobody had seen
        /// it: no shipped map paints a layer-jump tile, so nothing has ever climbed there.
        /// </summary>
        public const string LAYER_ENTITIES_HIGH = "EntitiesHigh";
        /// <summary>
        /// One slot per painted visual layer for BUILDINGS, <c>PropsL0..PropsL8</c>. Each sits
        /// directly ABOVE the tiles of its own layer and directly BELOW the tiles of the next,
        /// so a building half whose Z names layer N draws over everything painted at N and
        /// under everything painted above it — decided by sorting-LAYER comparison, by name.
        ///
        /// <para>The pair around the entity slot is what keeps the split ratio meaningful:
        /// <c>PropsL4</c> is below <see cref="LAYER_ENTITIES"/> and <c>PropsL6</c> above it, so
        /// with the default Z the player walks between a tree trunk and its crown exactly as
        /// before. <c>PropsL8</c> is below <see cref="LAYER_ENTITIES_OVERHEAD"/> for the same
        /// reason one layer up.</para>
        ///
        /// <para>Before this ladder every building lived on WallsBottom/WallsTop whatever it
        /// stood on, and since <c>WallsTop</c> is below the layer-7 and layer-8 tile slots, no
        /// building could draw over a wall painted up there at any Z. The nine were INSERTED
        /// into the sorting-layer list without moving or renaming an existing entry.</para>
        /// </summary>
        public const string LAYER_PROPS_L0 = "PropsL0";
        public const string LAYER_PROPS_L1 = "PropsL1";
        public const string LAYER_PROPS_L2 = "PropsL2";
        public const string LAYER_PROPS_L3 = "PropsL3";
        public const string LAYER_PROPS_L4 = "PropsL4";
        public const string LAYER_PROPS_L5 = "PropsL5";
        public const string LAYER_PROPS_L6 = "PropsL6";
        public const string LAYER_PROPS_L7 = "PropsL7";
        public const string LAYER_PROPS_L8 = "PropsL8";
        public const string LAYER_PROPS_L9 = "PropsL9";
        public const string LAYER_PROPS_L10 = "PropsL10";
        public const string LAYER_PROPS_L11 = "PropsL11";
        public const string LAYER_PROPS_L12 = "PropsL12";
        public const string LAYER_PROPS_L13 = "PropsL13";
        public const string LAYER_PROPS_L14 = "PropsL14";
        public const string LAYER_PROPS_L15 = "PropsL15";


        /// <summary>
        /// Visual layers 9..15: the tiers added when the ladder was grown to Unity's own
        /// ceiling. Each carries the same three slots as a layer below it — a TILE slot, a
        /// <c>PropsL{N}</c> for buildings and an <c>EntityL{N}</c> for whatever stands there.
        ///
        /// <para>They are named by INDEX while 0..8 keep names like <c>Ground</c> and
        /// <c>WallsTop</c>, and that split is deliberate rather than untidy: the first nine
        /// were given meanings by the Python build this game was ported from and those names
        /// are written into every shipped overlay file, so renaming them is a data migration.
        /// The new ones have no assigned meaning yet — an index is the honest name for a tier
        /// whose job the author has not decided.</para>
        /// </summary>
        public const string LAYER_TIER_9 = "Tier9";
        public const string LAYER_TIER_10 = "Tier10";
        public const string LAYER_TIER_11 = "Tier11";
        public const string LAYER_TIER_12 = "Tier12";
        public const string LAYER_TIER_13 = "Tier13";
        public const string LAYER_TIER_14 = "Tier14";
        public const string LAYER_TIER_15 = "Tier15";

        public const string LAYER_ENTITY_L9 = "EntityL9";
        public const string LAYER_ENTITY_L10 = "EntityL10";
        public const string LAYER_ENTITY_L11 = "EntityL11";
        public const string LAYER_ENTITY_L12 = "EntityL12";
        public const string LAYER_ENTITY_L13 = "EntityL13";
        public const string LAYER_ENTITY_L14 = "EntityL14";
        public const string LAYER_ENTITY_L15 = "EntityL15";

        public const string LAYER_UI_WORLD = "UI_World";
        public const string LAYER_OVERLAY = "Overlay";

        // --- Entity Z-Layer base orders (within Entities sorting layer) ---
        // Maps to Python's Z_LAYERS dict. Used as base sortingOrder before Y-offset.
        public const int Z_BACKGROUND = 0;
        public const int Z_GROUND = 100;
        public const int Z_LOW_OBJECT = 200;
        public const int Z_BUILDING_LOW = 300;
        public const int Z_ENTITY = 400;
        public const int Z_BUILDING_HIGH = 500;
        public const int Z_SKY = 600;
        public const int Z_UI = 1000;

        /// <summary>
        /// World units of Y per unit of sortingOrder. 100 gives hundredth-of-a-unit granularity
        /// between two entities standing close together, which is what the Y-sort is for.
        /// </summary>
        public const int Y_SORT_SCALE = 100;

        /// <summary>
        /// Order reserved above and below the Y term for everything that is added to it:
        /// <see cref="Z_UI"/> is the largest Z base (1000) and callers nudge by ±1
        /// (<c>BuildingObject</c>'s canopy, <c>BuildingSilhouetteOutline</c>'s outline), so the
        /// Y term must leave room for both or the SUM wraps even when the Y term did not.
        /// </summary>
        public const int SORT_ORDER_HEADROOM = 1024;

        /// <summary>
        /// <b>Unity truncates <c>SpriteRenderer.sortingOrder</c> to a 16-bit short IN THE
        /// PROPERTY SETTER.</b> Measured on this project: writing 32768 reads back -32768,
        /// 65536 reads back 0, 100000 reads back -31072. So an order past this is not merely
        /// "very high" — it comes back NEGATIVE, and the sprite draws behind everything it was
        /// meant to be in front of, silently.
        /// </summary>
        public const int MAX_SORT_ORDER = short.MaxValue;

        /// <summary>Mirror of <see cref="MAX_SORT_ORDER"/> at the bottom of the short range.</summary>
        public const int MIN_SORT_ORDER = short.MinValue;

        /// <summary>
        /// How far from the world origin a renderer may sit on Y before the Y-sort runs out of
        /// order to express it: <c>(32767 - 1024) / 100 = 317</c> world units.
        ///
        /// <para>This became load-bearing when the building Z stopped being a multiplied tier:
        /// <c>sortingOrder</c> now carries the Y term and nothing else, so the Y span IS the
        /// budget. Measured against the shipped world — zones at <c>offset_y</c> 0..100 tiles,
        /// 50 tiles tall, i.e. 0..150 units — the headroom is about 2x, and the zone database
        /// auto-expands. A zone row placed past this line would not degrade: its buildings
        /// would wrap to a large POSITIVE order and render in front of the entire world.
        /// <c>WorldLayerCeilingTests</c> asserts the shipped database stays inside it.</para>
        /// </summary>
        public const int MAX_SAFE_WORLD_Y = (MAX_SORT_ORDER - SORT_ORDER_HEADROOM) / Y_SORT_SCALE;

        // Warn once per session rather than once per frame: YToSortingOrder runs in LateUpdate
        // for every Y-sorted entity in the scene.
        private static bool _warnedOutOfBand;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetSortingConfigStatics() => _warnedOutOfBand = false;

        /// <summary>
        /// Convert a world Y position to a sortingOrder offset.
        /// Higher worldY (further up on screen / "deeper" in 2D top-down) gets a more-negative
        /// order so it draws BEHIND entities at lower worldY.
        ///
        /// <para>CLAMPED to the short window minus <see cref="SORT_ORDER_HEADROOM"/>. Clamping
        /// is the strictly better failure: past the budget every renderer out there sorts as
        /// EQUAL to its neighbours — locally wrong, bounded, and still monotone up to the line —
        /// where wrapping puts the furthest thing in the world in front of everything else. The
        /// warning fires once so the cause is on screen rather than inferred.</para>
        /// </summary>
        public static int YToSortingOrder(float worldY)
        {
            int order = -(int)(worldY * Y_SORT_SCALE);
            int limit = MAX_SORT_ORDER - SORT_ORDER_HEADROOM;
            if (order > limit || order < -limit)
            {
                if (!_warnedOutOfBand)
                {
                    _warnedOutOfBand = true;
                    Debug.LogWarning(
                        $"[SortingConfig] World Y {worldY:F1} is outside the Y-sort budget of " +
                        $"±{MAX_SAFE_WORLD_Y} units. Unity truncates sortingOrder to a 16-bit " +
                        "short in the setter, so the order is clamped here — depth beyond this " +
                        "line is flat. Move the zone closer to the origin, or lower " +
                        "SortingConfig.Y_SORT_SCALE (which costs Y-sort granularity everywhere).");
                }
                order = order > limit ? limit : -limit;
            }
            return order;
        }

        /// <summary>
        /// Compute final sortingOrder for an entity given its Z-layer base and Y position.
        /// </summary>
        public static int ComputeSortingOrder(int zLayerBase, float worldY)
        {
            return zLayerBase + YToSortingOrder(worldY);
        }

        /// <summary>
        /// How many painted visual layers the world has. THE one literal: every other place
        /// that used to spell this number now derives from it — <c>CollisionTagMap.LayerCount</c>,
        /// <c>WorldCollisionLayers.LayerCount</c>, <c>VisualLayerOccupant.MaxLayer</c>,
        /// <c>VisualLayerProbe.LayerCount</c> and <c>LayerJumpMap.MaxTarget</c> were five
        /// independent copies of "9", the shape this project already got burned by with
        /// <c>SpriteTintStack.LAYER_COUNT</c>.
        ///
        /// <para>It lives in Core because <c>TilemapLayerSetup.TilemapLayer</c> — the enum that
        /// morally owns it — is in Gameplay, and Core may not reference Gameplay. The two are
        /// pinned against each other by <c>WorldLayerCeilingTests</c>, in both directions, the
        /// same way <c>LoadoutStateSheets.state</c> answers the same assembly constraint.</para>
        ///
        /// <para><b>Growing it is bounded by Unity, not by taste.</b> Each visual layer needs a
        /// <c>WorldL{N}</c> PHYSICS layer, and Unity has 32 of those in total; measured on this
        /// project 7 are free, so the ceiling is <c>9 + 7 = 16</c> visual layers and spending
        /// them all leaves no room for a future gameplay layer. It also needs a <c>PropsL{N}</c>
        /// sorting layer inserted at the right rung of the ladder, and one more bit in
        /// <c>CollisionTagMap</c>'s int mask (cap 31). The test asserts every one of those
        /// joins rather than trusting whoever bumps this number to remember them.</para>
        /// </summary>
        public const int VISUAL_LAYER_COUNT = 16;

        /// <summary>Highest painted visual layer. Derived — see <see cref="VISUAL_LAYER_COUNT"/>.</summary>
        public const int MAX_VISUAL_LAYER = VISUAL_LAYER_COUNT - 1;

        /// <summary>
        /// Where the halves of a building sit when nobody has authored a Z: the footprint just
        /// above the WallsBottom tiles (layer 4) and BELOW the entity slot, the canopy just
        /// above the WallsTop tiles (layer 6) and above it. That is the sandwich the split
        /// ratio exists for, and the depth every already-placed building rendered at before Z
        /// meant a layer.
        /// </summary>
        public const int DEFAULT_PROP_Z_BOTTOM = 4;
        public const int DEFAULT_PROP_Z_TOP = 6;

        /// <summary>
        /// The sorting layer the TILEMAP of visual layer <paramref name="visualLayer"/> renders
        /// on, or <c>null</c> for a layer that draws nothing (layer 2 is Collision, which is
        /// baked into physics and never rendered).
        ///
        /// <para>It lives here rather than as a switch inside <c>TilemapLayerSetup</c> because
        /// that switch had a hand-mirrored copy in the day/night wiring fixture, and a ladder of
        /// sixteen layers is sixteen chances for the two to disagree. One resolver, three
        /// readers — the tilemap builder, the ambient-coverage fixture and the building Z
        /// ladder test — so a new tier cannot be lit in one and dark in another.</para>
        /// </summary>
        public static string TileSortingLayer(int visualLayer)
        {
            switch (visualLayer)
            {
                case 0: return LAYER_GROUND;
                case 1: return LAYER_FLOOR_DECALS;
                case 2: return null;                 // Collision — physics only, never drawn
                case 3: return LAYER_OBJECTS_LOW;
                case 4: return LAYER_WALLS_BOTTOM;
                case 5: return LAYER_DECORATIONS;
                case 6: return LAYER_WALLS_TOP;
                case 7: return LAYER_OBJECTS_HIGH;
                case 8: return LAYER_OVERHEAD;
                case 9: return LAYER_TIER_9;
                case 10: return LAYER_TIER_10;
                case 11: return LAYER_TIER_11;
                case 12: return LAYER_TIER_12;
                case 13: return LAYER_TIER_13;
                case 14: return LAYER_TIER_14;
                case 15: return LAYER_TIER_15;
                default: return null;
            }
        }

        /// <summary>
        /// The sorting layer for a building half whose Z is <paramref name="z"/>: the slot that
        /// lives directly above the tiles painted on visual layer <paramref name="z"/> and
        /// directly below those of layer <c>z + 1</c>. Clamped and never null — a building
        /// always has somewhere to be drawn, and an out-of-range Z lands at the nearest end
        /// rather than on Default, which would put it behind the whole world.
        /// </summary>
        public static string PropSortingLayer(int z)
        {
            if (z < 0) z = 0;
            else if (z > MAX_VISUAL_LAYER) z = MAX_VISUAL_LAYER;
            switch (z)
            {
                case 0: return LAYER_PROPS_L0;
                case 1: return LAYER_PROPS_L1;
                case 2: return LAYER_PROPS_L2;
                case 3: return LAYER_PROPS_L3;
                case 4: return LAYER_PROPS_L4;
                case 5: return LAYER_PROPS_L5;
                case 6: return LAYER_PROPS_L6;
                case 7: return LAYER_PROPS_L7;
                case 8: return LAYER_PROPS_L8;
                case 9: return LAYER_PROPS_L9;
                case 10: return LAYER_PROPS_L10;
                case 11: return LAYER_PROPS_L11;
                case 12: return LAYER_PROPS_L12;
                case 13: return LAYER_PROPS_L13;
                case 14: return LAYER_PROPS_L14;
                default: return LAYER_PROPS_L15;
            }
        }
    }
}
