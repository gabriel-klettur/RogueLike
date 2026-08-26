#!/usr/bin/env python3
"""Classification table for the second wave of prop / building sheets.

Turns the hand-written table below into the metadata JSON that
``build_building_props.py`` consumes. One row per crop:

    index  name  category  split_ratio  target_height_tiles  [flags]

``split_ratio`` is the fraction of the sprite that renders as CANOPY, over the
player (``BuildingObject.Assembly`` computes the footprint as
``spriteH * (1 - splitRatio)``). The ladder matches the first wave:

    0.0   flat on the ground - puddles, grates, scattered papers, bedrolls
    0.3   knee high          - buckets, sacks, pots, books
    0.45  waist high         - fences, benches, chests, carts
    0.6   shoulder high      - racks, tombstones, workbenches
    0.8   tall               - lamp posts, banners, statues, tents
    0.85  very tall          - buildings, towers, portal arches

Flags: ``!solid`` marks something the player walks over; ``=category`` overrides
the sheet's default category; ``@Preset`` gives the LightPresetCatalog key the
fixture emits (Lamp / Torch / Magic / Candle), with ``@Preset:0.6`` moving the
flame up the sprite (fraction of its height; the default 0.75 suits a lamp post
but not a floor circle).
"""

from __future__ import annotations

import json
import os
import sys
from collections import Counter

REPO_ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..", ".."))
RESOURCES_BUILDINGS = os.path.join(
    REPO_ROOT, "unity", "Valkur", "Assets", "_Project", "Resources", "Buildings")
OUT = os.path.join(REPO_ROOT, "tools", "atlas", "generated",
                   "building_props_metadata_wave2.json")

TABLE: dict[str, tuple[str, str]] = {}


def sheet(name: str, default_category: str, rows: str) -> None:
    TABLE[name] = (default_category, rows)


# -- props_military_camp_sheet ------------------------------------------------
sheet("props_military_camp_sheet", "military", """
0   banner_standard_tall          0.8   3.6
1   banner_standard_short         0.8   3.4
2   flag_pole_pennant             0.8   3.3
3   banner_gold_trim              0.8   2.9
4   watchtower_wooden             0.85  6.2
5   gatehouse_guard               0.85  4.8
6   sentry_box                    0.8   4.4
7   checkpoint_barrier            0.45  2.0
8   palisade_stake_wall           0.6   2.2
9   barricade_spiked_beam         0.45  1.8
10  barricade_caltrop_cross       0.45  2.0
11  barricade_sandbag_stakes      0.45  1.8
12  barricade_banner_wall         0.6   2.2
13  spear_rack_ground             0.6   2.6
14  bell_gantry                   0.8   3.4
15  training_dummy_target         0.6   3.2
16  training_dummy_armored        0.6   3.2
17  archery_target_round          0.6   2.8
18  archery_target_board          0.6   2.8
19  weapon_rack_swords            0.6   2.8
20  spear_rack_standing           0.6   3.0
21  bow_rack                      0.6   2.8
22  brazier_tripod_lit            0.8   3.2   @Torch:0.72
23  brazier_bowl_lit              0.8   2.8   @Torch:0.72
24  signal_horn_stand             0.8   2.6
25  command_tent_large            0.8   4.0
26  supply_tent_small             0.8   3.2
27  war_table                     0.45  2.4
28  crate_stack_military          0.45  2.0
29  barrel_pair_camp              0.3   1.4
30  barrel_trio_camp              0.3   1.5
31  grain_sack_single             0.3   1.1
32  grain_sack_pair               0.3   1.1
33  supply_pile_sacks_logs        0.3   1.1
34  supply_cart_loaded            0.45  2.2
35  supply_cart_empty             0.45  1.6
36  ammo_crates_open              0.3   1.4
37  shield_heater_red             0.45  1.6
38  shield_kite_blue              0.45  1.5
39  spear_pennant_single          0.6   2.4
40  spear_plain                   0.6   2.4
41  spear_pennant_pair            0.6   2.8
""")

# -- props_graveyard_sheet ----------------------------------------------------
sheet("props_graveyard_sheet", "graveyard", """
0   headstone_round_mossy         0.6   1.7
1   headstone_cracked             0.6   1.9
2   grave_cross_celtic_tall       0.6   2.3
3   headstone_arched_cross        0.6   2.1
4   grave_cross_stone             0.6   2.1
5   headstone_skull_candles       0.6   2.2   @Candle:0.35
6   headstone_leaning_mossy       0.6   2.0
7   headstone_gothic_arch         0.6   2.5
8   grave_cross_wooden            0.6   1.9
9   grave_cross_celtic_small      0.6   2.0
10  sarcophagus_flowers           0.45  1.8   @Candle:0.45
11  sarcophagus_skull             0.45  1.8
12  sarcophagus_plain             0.45  1.6
13  mausoleum_small               0.85  3.4   @Candle:0.25
14  graveyard_arch_gate           0.85  3.4   @Lamp:0.55
15  iron_gate_double              0.8   2.4
16  iron_fence_section            0.6   2.1
17  gate_pillar_lantern           0.8   2.6   @Lamp:0.78
18  iron_fence_short              0.6   1.6
19  grave_marker_lantern          0.8   2.6   @Candle:0.62
20  statue_mourner_hooded         0.8   3.4   @Candle:0.2
21  statue_angel_praying          0.8   3.2   @Candle:0.2
22  statue_knight_kneeling        0.8   3.2   @Candle:0.2
23  statue_mourner_kneeling       0.8   3.0   @Candle:0.2
24  grave_altar_candles           0.6   2.5   @Candle:0.6
25  shrine_saint_niche            0.8   3.1   @Candle:0.3
26  grave_candles_melted          0.3   2.1   @Candle:0.75
27  candelabra_grave_tall         0.6   2.8   @Candle:0.85
28  memorial_bell                 0.8   2.4
29  brazier_grave_lit             0.8   2.7   @Torch:0.78
30  funeral_wreath                0.3   1.4
31  flower_arrangement_lilies     0.3   1.3
32  grave_candle_flowers          0.3   1.1   @Candle:0.7
33  urn_flowers_white             0.45  1.5
34  urn_flowers_red               0.45  1.5
35  grave_flowers_violets         0.0   0.9   !solid
36  grave_flowers_lilies          0.3   1.2
37  flower_pot_roses              0.3   1.2
38  bench_stone_graveyard         0.45  1.2
39  stone_slab_broken             0.0   1.0   !solid
40  stone_rubble_pile             0.3   1.1
41  dead_tree_bare                0.85  3.4
42  tree_stump_mossy              0.3   1.4
43  mossy_rocks_cluster           0.0   0.9   !solid
44  grave_lantern_stone           0.8   2.4   @Lamp:0.72
45  lamp_post_graveyard           0.8   2.9   @Lamp:0.8
46  fresh_grave_shovel            0.3   2.0
47  grave_open_skeleton           0.0   1.0   !solid
48  headstone_rubble              0.6   1.8
49  headstone_broken_rocks        0.6   1.6
50  skull_stone_ground            0.0   0.9   !solid
""")

# -- props_arcane_sheet -------------------------------------------------------
sheet("props_arcane_sheet", "arcane", """
0   crystal_cluster_blue          0.6   2.0   @Magic:0.5
1   crystal_cluster_purple        0.6   2.0   @Magic:0.5
2   crystal_cluster_red           0.6   1.9   @Magic:0.5
3   crystal_cluster_green         0.6   1.9   @Magic:0.5
4   crystal_shards_blue           0.45  1.6
5   crystal_shards_purple         0.45  1.6
6   crystal_shards_red            0.45  1.5
7   crystal_shards_green          0.45  1.5
8   rune_shard_floating           0.6   1.8   @Magic:0.5
9   rune_stone_levitating         0.45  1.6   @Magic:0.5
10  rune_rubble_glowing           0.3   1.3
11  -                             -     -
12  obelisk_rune_tall             0.85  3.6   @Magic:0.6
13  obelisk_rune_blue             0.85  3.4   @Magic:0.6
14  obelisk_rune_purple           0.85  3.8   @Magic:0.6
15  pedestal_crystal_blue         0.8   3.4   @Magic:0.82
16  pedestal_gem_purple           0.8   3.4   @Magic:0.82
17  pedestal_gem_red              0.8   3.4   @Magic:0.82
18  pedestal_gem_green            0.8   3.1   @Magic:0.82
19  orb_stand_blue                0.8   2.8   @Magic:0.8
20  orb_stand_purple              0.8   2.8   @Magic:0.8
21  orb_stand_red                 0.8   2.6   @Magic:0.8
22  orb_stand_green               0.8   2.8   @Magic:0.8
23  armillary_sphere              0.8   3.1   @Magic:0.5
24  rune_stone_spiral_glow        0.6   3.2   @Magic:0.55
25  spellbook_closed_red          0.3   1.0
26  spellbook_open_blue           0.3   1.0   @Magic:0.6
27  spellbook_open_purple         0.3   1.2   @Magic:0.6
28  spellbook_closed_green        0.3   1.0
29  mana_shard_purple             0.45  1.6   @Magic:0.5
30  scroll_map_open               0.3   1.0
31  scroll_sealed                 0.3   1.1
32  scroll_rune_open              0.3   1.0
33  candle_single_lit             0.3   1.4   @Candle:0.85
34  candle_cluster_lit            0.3   1.6   @Candle:0.8
35  candle_cluster_blue           0.3   1.6   @Magic:0.8
36  candelabra_arcane             0.6   2.8   @Candle:0.85
37  lantern_arcane_blue           0.8   2.8   @Magic:0.62
38  lantern_arcane_purple         0.8   2.8   @Magic:0.62
39  lantern_hook_arcane           0.8   2.8   @Magic:0.5
40  brazier_soulflame_blue        0.8   3.2   @Magic:0.78
41  brazier_soulflame_purple      0.8   3.6   @Magic:0.78
42  brazier_soulflame_red         0.8   3.1   @Torch:0.78
43  brazier_soulflame_green       0.8   3.0   @Magic:0.78
44  mana_lantern_cage_blue        0.8   3.2   @Magic:0.55
45  mana_lantern_cage_purple      0.8   3.1   @Magic:0.55
46  mana_lantern_cage_red         0.8   3.1   @Magic:0.55
47  scribe_desk_arcane            0.45  2.4   @Candle:0.7
48  alchemy_table_potions         0.45  2.0
49  enchanting_desk               0.45  2.2   @Magic:0.55
50  summoning_circle_purple       0.0   1.2   !solid  @Magic:0.4
51  summoning_circle_blood        0.0   1.4   !solid  @Torch:0.4
52  teleport_pad_blue             0.0   1.0   !solid  @Magic:0.4
53  mana_shards_small             0.3   1.2
54  mana_crystal_glowing          0.45  1.2   @Magic:0.5
55  rune_stone_spiral_blue        0.6   2.0   @Magic:0.55
56  rune_stone_spiral_purple      0.6   2.0   @Magic:0.55
57  rune_stone_spiral_green       0.6   2.0   @Magic:0.55
58  portal_arch_arcane            0.85  4.0   @Magic:0.55
59  mana_pylon_pair               0.8   2.6   @Magic:0.7
60  shrine_idol_arcane            0.85  3.4   @Magic:0.45
61  mana_font_crystal             0.6   2.4   @Magic:0.6
62  crystal_ruin_arch             0.6   2.2   @Magic:0.45
63  crystal_crate_blue            0.3   1.5   @Magic:0.6
64  crystal_crates_barrel         0.45  1.8
65  crystal_shelf_rack            0.6   2.2   @Magic:0.55
66  chest_arcane_open             0.45  1.5   @Magic:0.6
67  ruin_pillar_crystal           0.6   2.2
68  ruin_books_scattered          0.0   1.0   !solid
69  rune_disc_stone               0.6   2.2   @Magic:0.55
""")

# -- props_blacksmith_sheet ---------------------------------------------------
sheet("props_blacksmith_sheet", "blacksmith", """
0   forge_full_workshop           0.85  4.2   @Torch:0.4
1   forge_stone_stool             0.85  4.2   @Torch:0.4
2   forge_domed_bucket            0.85  4.0   @Torch:0.35
3   bellows_large                 0.45  2.2
4   anvil_stump_hammer            0.6   2.4
5   anvil_stump                   0.6   2.2
6   workbench_tools               0.45  2.0
7   workbench_plans               0.45  2.1
8   grindstone_wheel              0.6   2.2
9   spear_rack_forge              0.6   2.6
10  armor_stand_plate             0.8   3.0
11  helmet_stand_plumed           0.8   2.8
12  tool_rack_wall                0.6   2.2
13  tong_rack                     0.6   2.4
14  polearm_rack_banner           0.6   3.0
15  sword_crate_upright           0.45  2.4
16  ore_crate_raw                 0.3   1.6
17  ingot_stack_steel             0.3   1.4
18  ingot_bundle_strapped         0.3   1.4
19  coal_crate                    0.3   1.6
20  coal_pile                     0.0   1.2   !solid
21  coal_bucket_wooden            0.3   1.4
22  firewood_bundle_strapped      0.3   1.4
23  quench_barrel_water           0.3   1.6
24  water_bucket_metal            0.3   1.1
25  ore_cart_loaded               0.45  2.0
26  scrap_metal_pile              0.0   1.4   !solid
27  chain_pile                    0.0   1.0   !solid
28  blacksmith_apron              0.45  2.0
29  chest_tools_open              0.45  1.8
30  hammer_sledge                 0.45  1.6
31  hammer_set                    0.45  1.6
32  hammer_and_chisel             0.45  1.6
33  hammer_small                  0.3   1.2
34  tongs_long                    0.45  1.8
35  tongs_flat                    0.45  1.8
36  tongs_curved                  0.45  1.7
37  pincers_small                 0.3   1.3
38  pliers_small                  0.3   1.3
""")

# -- props_village_domestic_sheet ---------------------------------------------
sheet("props_village_domestic_sheet", "domestic", """
0   clothesline_colored           0.8   2.4
1   clothesline_sheets            0.8   2.4
2   clothesline_shirts            0.8   2.4
3   laundry_basket_folded         0.3   1.1
4   laundry_basket_linen          0.3   1.1
5   bucket_wooden_water           0.3   1.2
6   bucket_metal_pail             0.3   1.2
7   broom_straw                   0.6   2.4
8   mop_cloth                     0.6   2.4
9   firewood_stack_round          0.3   1.3
10  firewood_basket               0.3   1.4
11  flower_barrel_red             0.3   1.4
12  flower_box_window             0.3   1.2
13  potted_lavender_tall          0.3   1.5
14  potted_basil_pot              0.3   1.2
15  potted_herbs_pair             0.3   1.4
16  table_cloth_flowers           0.45  1.9
17  chair_wooden_back             0.45  1.7
18  stool_round_three_leg         0.3   1.0
19  bench_wooden_long             0.45  1.3
20  bench_stone_footed            0.45  1.1
21  basket_wicker_square          0.3   1.2
22  basket_wicker_handle          0.3   1.3
23  amphora_terracotta            0.45  1.5
24  jug_painted_blue              0.3   1.1
25  vase_ceramic_blue             0.3   1.1
26  jug_red_clay                  0.3   1.1
27  bottle_green_glass            0.3   1.2
28  bottle_amber_glass            0.3   1.2
29  bottle_blue_glass             0.3   1.0
30  flask_corked                  0.3   0.9
31  crate_wooden_open             0.3   1.3
32  chest_wooden_banded           0.3   1.2
33  crate_tomatoes_red            0.3   1.3
34  grain_sack_open               0.3   1.5
35  grain_sack_stack              0.3   1.3
36  food_basket_bread             0.3   1.3
37  linen_stack_folded            0.3   1.1
38  rug_rolls_colored             0.3   1.4
39  rug_stack_folded              0.3   1.2
40  toy_sword_and_cart            0.3   1.0
41  toy_cart_wooden               0.3   1.0
42  -                             -     -
43  rocking_horse                 0.45  1.9
44  dog_house                     0.45  1.8
45  birdcage_stand                0.8   2.6
46  washtub_board                 0.3   1.7
47  drying_rack_linens            0.6   1.9
48  cushion_stack                 0.3   1.2
49  yarn_basket                   0.3   1.4
50  toy_blocks                    0.3   0.7
51  toy_ball                      0.3   0.6
""")

# -- props_bandit_hideout_sheet -----------------------------------------------
sheet("props_bandit_hideout_sheet", "bandit", """
0   crate_broken_planks           0.3   1.4
1   barrel_smashed                0.3   1.4
2   bottles_green_spilled         0.0   0.8   !solid
3   bottles_broken_pile           0.0   1.2   !solid
4   bottle_green_fallen           0.0   0.6   !solid
5   bottles_green_pair            0.3   1.0
6   bottles_spilled_cups          0.0   0.7   !solid
7   bottle_amber_cups             0.3   1.0
8   barrel_spilled_loot           0.3   1.4
9   papers_scattered              0.0   1.0   !solid
10  coin_sack_spilled             0.3   1.2
11  chair_broken                  0.45  1.7
12  bench_broken                  0.45  1.4
13  table_broken                  0.45  1.3
14  plank_debris_small            0.0   0.5   !solid
15  plank_debris_crossed          0.0   0.6   !solid
16  campfire_lit                  0.3   1.4   @Torch:0.5
17  campfire_cold                 0.0   1.0   !solid
18  bedroll_rags                  0.0   1.3   !solid
19  bedroll_blanket               0.0   1.3   !solid
20  pallet_wooden                 0.0   1.0   !solid
21  chain_coil                    0.0   1.0   !solid
22  shackles_chained              0.0   1.3   !solid
23  cage_small_wooden             0.45  1.8
24  cage_tall_iron                0.8   2.9
25  wanted_poster_board           0.8   2.6
26  -                             -     -
27  treasure_map_torn             0.3   1.6
28  chest_iron_banded             0.45  1.4
29  chest_loot_open               0.45  1.5
30  cart_wrecked                  0.45  1.8
31  -                             -     -
32  cart_wheel_upright            0.45  1.5
33  cart_wheel_broken             0.0   1.4   !solid
34  barricade_planks_nailed       0.6   2.0
35  palisade_broken               0.6   1.9
36  barricade_camp_banner         0.6   2.0
37  graffiti_eye_red              0.6   1.8
38  graffiti_skull_runes          0.6   1.8
39  graffiti_cross_red            0.6   1.8
40  rat_and_bones                 0.0   1.0   !solid
41  sewer_grate_round             0.0   1.0   !solid
42  bucket_slop_green             0.3   1.4
43  potato_sack_spilled           0.3   1.4
44  crate_food_stolen             0.3   1.5
45  rags_pile_colored             0.0   1.2   !solid
46  loot_stash_crates             0.45  1.8
47  backpack_bedroll              0.45  1.8
""")

# -- props_water_and_plumbing_sheet -------------------------------------------
sheet("props_water_and_plumbing_sheet", "water", """
0   well_roofed_red               0.85  3.6
1   well_windlass                 0.8   3.0
2   water_pump_hand               0.8   3.0
3   water_pump_fountain           0.8   2.8
4   fountain_tiered_stone         0.85  3.4
5   fountain_lion_wall            0.85  3.2
6   trough_stone_spout            0.45  2.0
7   trough_wooden_frame           0.45  1.8
8   water_barrel_pipe             0.45  2.6
9   rain_catcher_barrel           0.8   3.0
10  roof_gutter_pipe              0.8   2.6
11  gutter_downspout              0.6   2.4
12  pipe_wall_vertical            0.8   2.8
13  aqueduct_channel_stone        0.3   1.8
14  aqueduct_channel_wood         0.3   1.8
15  aqueduct_channel_mossy        0.3   1.8
16  footbridge_wooden             0.3   1.8   !solid
17  bridge_stone_arch             0.45  1.8   !solid
18  grate_drain_rect              0.0   1.0   !solid
19  grate_drain_round             0.0   1.0   !solid
20  grate_sewer_square            0.0   1.0   !solid
21  manhole_cover_stone           0.0   1.0   !solid
22  sewer_outlet_round            0.45  2.0
23  sewer_outlet_arch             0.6   2.2
24  culvert_outlet_stone          0.6   2.4
25  well_pulley_bucket            0.8   3.0
26  well_pulley_double            0.8   3.0
27  cistern_hatch_wooden          0.0   1.4   !solid
28  waterfall_stone_steps         0.45  2.2
29  puddle_water                  0.0   0.7   !solid
30  pipe_valve_leaking            0.45  1.5
31  puddle_water_small            0.0   0.5   !solid
32  standpipe_water               0.8   2.8
""")

# -- props_statues_and_monuments_sheet ----------------------------------------
sheet("props_statues_and_monuments_sheet", "statues", """
0   statue_king_crowned           0.85  4.4
1   statue_queen_orb              0.85  4.2
2   statue_knight_shield          0.85  4.6
3   statue_knight_sword_aloft     0.85  4.8
4   statue_wizard_staff           0.85  4.4
5   statue_priest_reliquary       0.85  4.7
6   statue_knight_equestrian      0.85  5.2
7   statue_lion_couchant          0.8   3.4
8   statue_griffin                0.8   4.2
9   statue_eagle_wings            0.8   3.8
10  memorial_cross_wreath         0.8   3.8   @Candle:0.35
11  memorial_obelisk_ivy          0.85  3.8
12  monument_obelisk_banners      0.85  3.8
13  column_winged_victory         0.85  3.8
14  column_ruined_ivy             0.6   2.8
15  fountain_grand_tiered         0.85  3.8
16  fountain_cherub               0.8   3.0
17  fountain_round_jet            0.6   2.7
18  sundial_stone_round           0.3   2.1
19  sundial_gnomon_gold           0.3   2.1
20  clock_post_ornate             0.85  3.6   @Lamp:0.82
21  monument_bell_arch            0.8   3.0
22  eternal_flame_pedestal        0.8   3.6   @Torch:0.8
23  banner_pair_pedestal          0.8   3.6
24  heraldry_monument_shield      0.8   3.4
25  pedestal_crest_stone          0.6   2.8
26  pedestal_laurel_round         0.6   2.6
27  pedestal_stone_broken         0.6   2.6
28  ruin_column_fallen            0.0   1.4   !solid
29  planter_stone_flowers         0.45  2.2
30  bench_stone_carved            0.45  1.9
31  stanchion_chain_gold          0.45  1.6
""")

# -- props_quest_and_portals_sheet --------------------------------------------
sheet("props_quest_and_portals_sheet", "quest", """
0   quest_board_blue_roof         0.85  4.2
1   notice_board_red_roof         0.8   3.0
2   bounty_board_wanted           0.8   3.2
3   quest_board_sealed            0.8   3.3
4   quest_board_dagger            0.8   3.4
5   lectern_scroll_candle         0.8   3.4   @Candle:0.85
6   banner_guild_lion             0.8   4.2
7   signpost_guild_crest          0.8   3.2
8   signpost_duel_medallion       0.8   3.5
9   chest_guild_closed            0.45  1.8
10  chest_guild_open_gold         0.45  2.1
11  chest_iron_locked             0.45  1.7
12  chest_offering_draped         0.45  1.8
13  donation_box_pedestal         0.8   3.1   @Candle:0.72
14  pedestal_relic_banner         0.8   3.0
15  lectern_quill_book            0.8   3.2
16  shrine_reaper_sword           0.85  3.3   @Torch:0.4
17  statue_angel_crystal          0.85  3.4   @Magic:0.55
18  portal_arch_stone_blue        0.85  3.4   @Magic:0.55
19  portal_ring_crystals          0.8   2.8   @Magic:0.5
20  crystal_altar_gold            0.8   3.2   @Magic:0.7
21  mana_pylon_ringed             0.8   3.2   @Magic:0.78
22  mana_pylon_tall               0.8   3.6   @Magic:0.82
23  orb_pillar_blue               0.8   3.4   @Magic:0.85
24  rune_stone_purple             0.6   3.0   @Magic:0.6
25  teleport_pad_ringed           0.0   1.4   !solid  @Magic:0.4
26  mana_crystal_floating         0.45  1.2   @Magic:0.5
27  orb_pillar_chained            0.8   3.1   @Magic:0.85
28  guild_bell_roofed             0.85  4.0
29  signpost_bell_banner          0.8   3.4
30  signpost_lantern_cross        0.8   3.4   @Lamp:0.6
31  scribe_desk_guild             0.45  3.0
32  map_table_candle              0.45  3.0   @Candle:0.75
33  mailbox_stone                 0.6   3.0
34  pedestal_guild_medallion      0.8   3.4
35  reliquary_chained_stone       0.8   3.3   @Magic:0.5
""")

# -- buildings_village_shops_and_props_sheet ----------------------------------
# Mixed sheet: the buildings go to houses/shops, the bottom rows join the
# first wave's own categories.
sheet("buildings_village_shops_and_props_sheet", "houses", """
0   house_inn_stone_large         0.85  6.2
1   building_bank_tall            0.85  6.6
2   shop_gem_two_storey           0.85  5.4   =shops
3   shop_market_awning            0.85  5.8   =shops
4   tower_mage_round              0.85  5.6
5   house_timber_blue_shutters    0.85  4.8
6   shop_grocer_produce           0.85  4.8   =shops
7   shop_smithy_dark_roof         0.85  5.1   =shops
8   house_timber_blue_roof        0.85  4.4
9   house_timber_red_large        0.85  5.0
10  shop_butcher_awning           0.85  5.2   =shops
11  house_cottage_slate           0.85  3.8
12  house_cottage_red_roof        0.85  3.8
13  house_cottage_thatched        0.85  3.4
14  house_cottage_green_roof      0.85  3.7
15  house_cottage_chimney         0.85  3.7
16  chapel_stone_small            0.85  3.6
17  house_guard_post_blue         0.85  3.2
18  house_workshop_red            0.85  4.0
19  stall_produce_blue_awning     0.8   2.4   =market
20  stall_meat_red_awning         0.8   2.2   =market
21  cart_bread_vendor             0.45  1.5   =market
22  well_village_stone            0.6   2.0   =props
23  signpost_inn_lantern          0.8   2.4   =signs   @Lamp:0.68
24  notice_board_village          0.6   2.0   =signs
25  bench_wooden_plain            0.45  1.1   =props
26  flower_planter_wooden         0.3   1.3   =nature
27  tree_cherry_blossom_small     0.85  3.2   =nature
28  tree_apple_small              0.85  3.2   =nature
29  tree_pine_slim                0.85  3.5   =nature
30  lamp_post_village             0.8   2.9   =lights  @Lamp:0.85
31  hedge_tall_green              0.6   2.8   =nature
32  crate_wooden_small            0.3   1.0   =props
33  -                             -     -
34  barrel_pair_small             0.3   1.1   =props
35  crate_wooden_wide             0.3   1.0   =props
36  fence_picket_white            0.45  1.1   =props
37  clothesline_short             0.6   1.6   =props
38  barrel_wooden_single          0.3   1.0   =props
39  -                             -     -
40  fence_picket_low_white        0.45  0.9   =props
41  cart_flower_vendor            0.45  1.5   =market
""")

# -- buildings_houses_set -----------------------------------------------------
sheet("buildings_houses_set", "houses", """
0   house_cottage_woodpile        0.85  4.6
1   house_narrow_tall             0.85  5.6
2   house_manor_timber            0.85  5.6
3   house_semi_detached           0.85  4.8
4   house_tenement_narrow         0.85  6.2
5   house_manor_crest             0.85  5.8
6   house_workshop_slate          0.85  5.0
7   house_farmhouse_woodpile      0.85  4.4
""")

# -- buildings_town_large_set_a / _b ------------------------------------------
# The two sheets draw the same eight subjects as independent renders (95% of
# pixels differ), so both ship and the suffix says which take a placement uses.
sheet("buildings_town_large_set_a", "houses", """
0   shop_blacksmith_forge_a       0.85  6.0   =shops  @Torch:0.28
1   tavern_three_storey_a         0.85  6.2   =shops
2   shop_bakery_awning_a          0.85  5.2   =shops
3   shop_alchemist_tower_a        0.85  5.8   =shops
4   guildhall_banners_a           0.85  6.2
5   shop_carpenter_mill_a         0.85  4.6   =shops
6   house_townhouse_grand_a       0.85  6.2
7   house_tenement_laundry_a      0.85  5.6
""")

sheet("buildings_town_large_set_b", "houses", """
0   shop_blacksmith_forge_b       0.85  6.0   =shops  @Torch:0.28
1   tavern_three_storey_b         0.85  6.2   =shops
2   shop_bakery_awning_b          0.85  5.2   =shops
3   shop_alchemist_tower_b        0.85  5.8   =shops
4   guildhall_banners_b           0.85  6.2
5   shop_carpenter_mill_b         0.85  4.6   =shops
6   house_townhouse_grand_b       0.85  6.2
7   house_tenement_laundry_b      0.85  5.6
""")


def parse() -> list[dict]:
    items: list[dict] = []
    for sheet_name, (default_category, rows) in TABLE.items():
        for line in rows.strip().splitlines():
            parts = line.split()
            if not parts:
                continue
            index, name = int(parts[0]), parts[1]
            if name == "-":                       # deliberately dropped crop
                continue

            item = {
                "sheet": sheet_name,
                "index": index,
                "name": name,
                "category": default_category,
                "solid": True,
                "split_ratio": float(parts[2]),
                "target_height_tiles": float(parts[3]),
            }
            for flag in parts[4:]:
                if flag == "!solid":
                    item["solid"] = False
                elif flag.startswith("="):
                    item["category"] = flag[1:]
                elif flag.startswith("@"):
                    key, _, offset = flag[1:].partition(":")
                    item["light_preset"] = key
                    item["light_offset_y"] = float(offset) if offset else 0.75
                else:
                    raise SystemExit(f"{sheet_name}#{index}: unknown flag {flag!r}")
            items.append(item)
    return items


def collisions(items: list[dict]) -> list[str]:
    """Names that would silently overwrite a sprite the first wave shipped."""
    clashes = []
    for it in items:
        existing = os.path.join(RESOURCES_BUILDINGS, it["category"], f"{it['name']}.png")
        if os.path.exists(existing):
            clashes.append(f"{it['category']}/{it['name']} already exists "
                           f"({it['sheet']}#{it['index']} would overwrite it)")
    return clashes


def main() -> int:
    items = parse()
    clashes = collisions(items)
    if clashes:
        for c in clashes:
            print(f"ERROR {c}", file=sys.stderr)
        return 1

    payload = {
        "generator": "tools/atlas/wave2/classify.py",
        "sheets": sorted(TABLE),
        "items": items,
    }
    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    with open(OUT, "w", encoding="utf-8") as fh:
        json.dump(payload, fh, indent=2)

    by_cat = Counter(i["category"] for i in items)
    lit = sum(1 for i in items if i.get("light_preset"))
    walkover = sum(1 for i in items if not i["solid"])
    print(f"{len(items)} items across {len(TABLE)} sheets -> {OUT}")
    for cat, n in sorted(by_cat.items()):
        print(f"  {cat:12s} {n}")
    print(f"  ({lit} emit light, {walkover} walk-over)")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
