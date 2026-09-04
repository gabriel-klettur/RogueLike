#!/usr/bin/env python3
"""Classification table for the coastal / fishing building wave.

Turns the hand-written table below into the metadata JSON that
``build_building_props.py`` consumes. One row per crop:

    index  name  category  split_ratio  target_height_tiles  [flags]

``split_ratio`` is the fraction of the sprite that renders as CANOPY, over the
player (``BuildingObject.Assembly`` computes the footprint as
``spriteH * (1 - splitRatio)``). The ladder matches every earlier wave:

    0.0   flat on the ground - shells, tracks, ripples, tidepools
    0.3   knee high          - buckets, baskets, crates, seaweed
    0.45  waist high         - barrels, stacked crates, tables, anchors
    0.6   shoulder high      - drying racks, piers, rowboats
    0.8   tall               - signposts, hoists, boats, cranes
    0.85  very tall          - huts, sea stacks, lighthouses

Flags: ``!solid`` marks something the player walks over; ``=category`` overrides
the sheet's default category; ``@Preset`` gives the LightPresetCatalog key the
fixture emits (Lamp / Torch / Magic / Candle), with ``@Preset:0.6`` moving the
flame up the sprite as a fraction of its height.

The eleven single-object sheets each carry exactly one crop at index 0 — the
slicer segments by alpha, so a sheet holding one building simply returns one box.

Sizes are chosen against the player, who is 2 tiles tall. A boat that a player
can stand beside reads at 4-5 tiles; a lighthouse has to tower, so it takes 10.
Nothing here is measured from the source pixels, which are all 1536x1024
regardless of what the object is meant to be.

Run:  python tools/atlas/wave7/classify_coastal.py
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
                   "building_props_metadata_coastal.json")

TABLE: dict[str, tuple[str, str]] = {}


def sheet(name: str, default_category: str, rows: str) -> None:
    TABLE[name] = (default_category, rows)


# -- shore_fishing_props_sheet ------------------------------------------------
# Gear a fishing camp leaves on the sand, plus the shore it is left on.
sheet("shore_fishing_props_sheet", "props", """
0   fishing_net_pile              0.3   1.3
1   fish_crates_barrel            0.45  1.4
2   rope_coils                    0.0   0.6   !solid
3   fish_baskets_traps            0.3   1.2
4   shore_water_buckets           0.3   1.0
5   driftwood_bundle              0.3   0.9   =nature
6   tidepool_rocks                0.3   1.3   =nature
7   seashell_scatter              0.0   0.5   !solid =nature
8   seaweed_pile                  0.3   0.9   =nature
9   wicker_fish_trap              0.3   1.0
10  fish_signpost                 0.8   2.0   =signs
11  shore_campfire                0.3   1.0   =lights @Torch:0.30
12  ship_anchor                   0.45  1.4
13  fishing_tackle_box            0.3   0.8
14  net_float_buoys               0.3   0.9
15  driftwood_planks              0.0   0.7   !solid =nature
""")

# -- fishing_station_props_sheet ----------------------------------------------
# The working half of a harbour: drying, salting, gutting, hauling.
sheet("fishing_station_props_sheet", "props", """
0   fish_drying_rack              0.6   2.0
1   fish_hanging_bundle           0.6   1.4
2   fish_crate_open               0.3   0.9
3   fish_crates_stacked           0.45  1.6
4   fish_basket_large             0.3   1.1
5   salt_sacks                    0.3   1.1
6   net_drying_stand              0.6   2.0
7   net_roll_floats               0.3   1.0
8   fishing_rods_leaning          0.6   1.8
9   fishing_tackle_set            0.0   0.8   !solid
10  fish_cleaning_table           0.45  1.6   =market
11  fish_cutting_board            0.0   0.7   !solid
12  dock_winch_hoist              0.8   2.2
13  harbour_buoys                 0.3   0.9
14  lobster_trap                  0.3   1.0
15  fish_salting_barrel           0.45  1.4
""")

# -- tidepool_debris_props_sheet ----------------------------------------------
# What the tide leaves behind. Nearly all of it is ground decal, so the split
# ratio is 0 and the player walks over it — a shell the player collides with
# reads as a bug, not as detail.
sheet("tidepool_debris_props_sheet", "nature", """
0   tidepool_rocks_large          0.3   1.4
1   shore_pebbles                 0.0   0.7   !solid
2   seashells_sand                0.0   0.5   !solid
3   conch_shell                   0.0   0.7   !solid
4   starfish_shells               0.0   0.5   !solid
5   kelp_strand                   0.0   0.5   !solid
6   seaweed_clump                 0.3   0.8
7   driftwood_root                0.3   1.0
8   driftwood_branches            0.0   0.7   !solid
9   fish_skeleton                 0.0   0.5   !solid
10  shore_bone_pile               0.0   0.6   !solid
11  sand_crab_holes               0.0   0.4   !solid
12  bird_tracks_sand              0.0   0.4   !solid
13  tidepool_small                0.0   0.9   !solid
14  sand_dune_small               0.0   1.0   !solid
15  shore_flotsam                 0.0   0.6   !solid
""")

# -- shore_fauna_props_sheet --------------------------------------------------
# Living scenery. Every one is walk-over: these are ambience, and a seagull that
# blocks a doorway is worse than no seagull.
sheet("shore_fauna_props_sheet", "nature", """
0   crab_single                   0.0   0.5   !solid
1   crab_large                    0.0   0.7   !solid
2   crab_pair                     0.0   0.6   !solid
3   crab_burrow                   0.0   0.6   !solid
4   seagull_standing              0.3   1.1   !solid
5   seagull_walking               0.3   1.0   !solid
6   seagull_feeding               0.3   0.9   !solid
7   seagull_resting               0.0   0.7   !solid
8   sandpiper_single              0.0   0.7   !solid
9   sandpiper_pair                0.0   0.6   !solid
10  fish_jumping                  0.0   1.0   !solid =water
11  fish_splash                   0.0   0.8   !solid =water
12  fish_shoal_shallows           0.0   1.2   !solid =water
13  sea_snail                     0.0   0.5   !solid
14  shore_bird_tracks             0.0   0.4   !solid
15  sand_ripples                  0.0   0.5   !solid
""")

# -- sea_rocks_props_sheet ----------------------------------------------------
# Landmark geology. These are the only nature props tall enough to need a
# building's split ratio.
sheet("sea_rocks_props_sheet", "nature", """
0   sea_stack_tall                0.85  6.0
1   sea_stack_twin                0.85  5.0
2   sea_arch                      0.85  5.0
3   rock_spire_cluster            0.8   3.0
4   rock_platform_flat            0.45  2.0
5   sea_stack_medium              0.85  4.5
6   sea_stack_slim                0.85  4.0
""")

# -- Single-object sheets -----------------------------------------------------
# One crop each, at index 0.

sheet("fishing_boat_wooden", "water", """
0   fishing_boat_wooden           0.8   4.0   @Lamp:0.62
""")

sheet("fishing_trawler_wooden", "water", """
0   fishing_trawler_wooden        0.8   5.5   @Lamp:0.55
""")

sheet("fishing_trawler_steel", "water", """
0   fishing_trawler_steel         0.8   5.0   @Lamp:0.50
""")

sheet("rowboat_beached", "water", """
0   rowboat_beached               0.6   2.5
""")

sheet("dock_pier_small", "water", """
0   dock_pier_small               0.6   3.0   @Lamp:0.72
""")

sheet("dock_pier_large", "water", """
0   dock_pier_large               0.6   4.0   @Lamp:0.60
""")

sheet("dock_crane", "water", """
0   dock_crane                    0.8   4.5   @Lamp:0.70
""")

sheet("fisherman_hut", "houses", """
0   fisherman_hut                 0.85  5.0   @Lamp:0.45
""")

sheet("fishmonger_house_dock", "houses", """
0   fishmonger_house_dock         0.85  5.5   @Lamp:0.50
""")

sheet("lighthouse_small", "houses", """
0   lighthouse_small              0.85  7.0   @Lamp:0.85
""")

sheet("lighthouse_large", "houses", """
0   lighthouse_large              0.85  10.0  @Lamp:0.88
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
    """Names that would silently overwrite a sprite an earlier wave shipped."""
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
        "generator": "tools/atlas/wave7/classify_coastal.py",
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
