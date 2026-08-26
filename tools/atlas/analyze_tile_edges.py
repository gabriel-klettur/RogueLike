#!/usr/bin/env python3
"""Derive per-tile edge/corner terrain signatures straight from the pixels.

Why this exists
---------------
Valkur's auto-tile engine is complete (BitmaskCalculator -> RulesetSolver ->
TerrainTileResolver -> TerrainPainter) but every authored TilesetRuleset has
zero slot mappings, so nothing has ever auto-tiled. Filling those slots by hand
is ~16 drags x 12 packs, and it has to be redone for every pack imported later.

The alternative is to read the mapping out of the artwork. For each tile we
sample a band a few pixels INSIDE each edge (never the outermost row -- many
packs carry a dark outline that lies about the terrain), label it with the
pack's dominant material, and do the same for the four corners. Tiles that end
up with the same signature are variants of the same slot, which the solver
already picks between deterministically.

Deliberately NOT layout-based: two of the packs (tileset_1, rock_grass) name
their sprites with a flat index and carry no row/column, so any inference that
needs to understand the sheet layout fails exactly where it is most needed.

This pass only REPORTS. It writes no Unity assets and mutates nothing.

Usage:
    python tools/atlas/analyze_tile_edges.py
    python tools/atlas/analyze_tile_edges.py --pack tileset_1 --verbose
"""

from __future__ import annotations

import argparse
import json
import math
import os
import sys
from collections import Counter, defaultdict

try:
    from PIL import Image
except ImportError:
    sys.exit("Pillow required:  pip install Pillow")

REPO = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
TILES_ROOT = os.path.join(REPO, "unity", "Valkur", "Assets", "_Project", "Resources", "Tiles")
OUT_DIR = os.path.join(os.path.dirname(os.path.abspath(__file__)), "generated")

# ── Sampling geometry ────────────────────────────────────────────────────────
# Both are fractions of the tile size so 16px and 32px packs behave the same.
EDGE_INSET_FRAC = 0.125   # skip the outer 1/8 -- that is where outlines live
EDGE_BAND_FRAC = 0.125    # then sample the next 1/8 inward
CORNER_FRAC = 0.25        # corner probe is a quarter-tile square, also inset

# A region whose dominant material holds less than this share is "mixed":
# a real blend (dithered transition), not a clean terrain edge.
PURITY_MIN = 0.65

# Two palette clusters closer than this in RGB are the same material.
MATERIAL_MERGE_DIST = 60.0
# Clusters below this share of the pack's opaque pixels are noise, not terrain.
MATERIAL_MIN_SHARE = 0.02

ALPHA_OPAQUE = 128        # below this a pixel counts as transparent

# Pixel art draws terrain boundaries with a near-black outline. Measured on
# tileset_1, that outline was 21% of all pixels and clustered as its own
# "material", which then won the vote on any edge it crossed and made half the
# pack look unclassifiable. Dark pixels are treated as STRUCTURE and dropped
# from the vote -- but only while they are a minority of the region, so a tile
# that is genuinely filled with a dark terrain (deep water, obsidian) still
# classifies as that terrain instead of being emptied out.
DARK_LUMA = 55
STRUCTURE_MAX_SHARE = 0.60


def luma(r, g, b):
    return 0.299 * r + 0.587 * g + 0.114 * b


def rgb_dist(a, b):
    return math.sqrt((a[0] - b[0]) ** 2 + (a[1] - b[1]) ** 2 + (a[2] - b[2]) ** 2)


def load_tiles(pack_dir):
    """Every PNG under the pack, recursively, as (name, RGBA image)."""
    out = []
    for root, _dirs, files in os.walk(pack_dir):
        for fn in sorted(files):
            if not fn.lower().endswith(".png"):
                continue
            path = os.path.join(root, fn)
            try:
                img = Image.open(path).convert("RGBA")
            except Exception as exc:                      # noqa: BLE001
                print(f"    ! unreadable {fn}: {exc}")
                continue
            out.append((os.path.splitext(fn)[0], img))
    return out


def build_palette(tiles):
    """Cluster the pack's opaque colours into a handful of terrain materials.

    Quantise coarsely, then agglomerate nearby bins. A terrain pack settles on
    3-5 materials (grass / dirt / rock / water); a big architectural sheet will
    report many more, which is itself the signal that it is not a terrain pack.
    """
    bins = Counter()
    total = 0
    for _name, img in tiles:
        for r, g, b, a in img.getdata():
            if a < ALPHA_OPAQUE:
                continue
            bins[(r // 24, g // 24, b // 24)] += 1
            total += 1
    if total == 0:
        return [], 0

    # Bin centres, heaviest first, then merge anything close together.
    centres = []
    for (br, bg, bb), n in bins.most_common():
        c = (br * 24 + 12, bg * 24 + 12, bb * 24 + 12)
        for existing in centres:
            if rgb_dist(c, existing["rgb"]) < MATERIAL_MERGE_DIST:
                existing["count"] += n
                break
        else:
            centres.append({"rgb": c, "count": n})

    mats = [c for c in centres if c["count"] / total >= MATERIAL_MIN_SHARE]
    mats.sort(key=lambda c: -c["count"])
    for i, m in enumerate(mats):
        m["id"] = i
        m["share"] = m["count"] / total
        m["hex"] = "#%02x%02x%02x" % m["rgb"]
    return mats, total


def classify_region(img, box, mats):
    """Dominant material of a rectangle, plus its purity and transparency."""
    px = img.crop(box).getdata()
    counts = Counter()          # every opaque pixel
    lit_counts = Counter()      # opaque pixels that are not outline-dark
    opaque = 0
    clear = 0
    dark = 0
    for r, g, b, a in px:
        if a < ALPHA_OPAQUE:
            clear += 1
            continue
        opaque += 1
        best, bestd = None, 1e9
        for m in mats:
            d = rgb_dist((r, g, b), m["rgb"])
            if d < bestd:
                best, bestd = m["id"], d
        counts[best] += 1
        if luma(r, g, b) < DARK_LUMA:
            dark += 1
        else:
            lit_counts[best] += 1

    n = opaque + clear
    if n == 0:
        return {"material": None, "purity": 0.0, "alpha": 1.0, "structure": 0.0}
    if clear / n > 0.5:
        return {"material": "clear", "purity": clear / n, "alpha": clear / n, "structure": 0.0}
    if not counts:
        return {"material": None, "purity": 0.0, "alpha": clear / n, "structure": 0.0}

    # Outline crossing the band: vote on the lit pixels only. If the dark pixels
    # ARE the fill rather than a line, fall back to the full vote.
    dark_share = dark / opaque if opaque else 0.0
    if dark_share <= STRUCTURE_MAX_SHARE and lit_counts:
        mat, cnt = lit_counts.most_common(1)[0]
        purity = cnt / sum(lit_counts.values())
    else:
        mat, cnt = counts.most_common(1)[0]
        purity = cnt / opaque
    return {"material": mat, "purity": purity, "alpha": clear / n, "structure": dark_share}


def analyse_tile(img, mats):
    w, h = img.size
    inset = max(1, int(round(w * EDGE_INSET_FRAC)))
    band = max(1, int(round(w * EDGE_BAND_FRAC)))
    corner = max(2, int(round(w * CORNER_FRAC)))

    edges = {
        "N": (inset, inset, w - inset, inset + band),
        "S": (inset, h - inset - band, w - inset, h - inset),
        "W": (inset, inset, inset + band, h - inset),
        "E": (w - inset - band, inset, w - inset, h - inset),
    }
    corners = {
        "NW": (inset, inset, inset + corner, inset + corner),
        "NE": (w - inset - corner, inset, w - inset, inset + corner),
        "SW": (inset, h - inset - corner, inset + corner, h - inset),
        "SE": (w - inset - corner, h - inset - corner, w - inset, h - inset),
    }
    centre = (w // 3, h // 3, w - w // 3, h - h // 3)

    return {
        "edges": {k: classify_region(img, v, mats) for k, v in edges.items()},
        "corners": {k: classify_region(img, v, mats) for k, v in corners.items()},
        "centre": classify_region(img, centre, mats),
    }


def _sig(res, key, dirs):
    out = []
    for d in dirs:
        e = res[key][d]
        if e["material"] == "clear":
            out.append("_")
        elif e["material"] is None or e["purity"] < PURITY_MIN:
            out.append("?")
        else:
            out.append(str(e["material"]))
    return "".join(out)


def signature(res):
    """Compact edge signature, '?' where the region is too blended to call."""
    return _sig(res, "edges", ("N", "E", "S", "W"))


def corner_signature(res):
    """Corner signature — the model that actually fits transition sheets.

    A transition tile is routinely cut diagonally, so one EDGE can be half
    grass and half dirt. Forcing a single label onto that edge is not a
    measurement failure, it is the wrong question: the edge genuinely carries
    two terrains. Corners never straddle a diagonal cut, which is why
    corner-based (Wang) auto-tiling is the standard model for transition art.
    Reported alongside the edge signature so the data can say which model each
    pack actually supports rather than us assuming one.
    """
    return _sig(res, "corners", ("NW", "NE", "SE", "SW"))


def analyse_pack(pack_dir, name, verbose=False):
    tiles = load_tiles(pack_dir)
    if not tiles:
        return None

    sizes = Counter(img.size for _n, img in tiles)
    mats, total_px = build_palette(tiles)

    per_tile = []
    sig_counts = Counter()
    csig_counts = Counter()
    ambiguous = []
    camb = 0
    for tname, img in tiles:
        res = analyse_tile(img, mats)
        sig = signature(res)
        csig = corner_signature(res)
        sig_counts[sig] += 1
        csig_counts[csig] += 1
        if "?" in csig:
            camb += 1
        rec = {
            "tile": tname,
            "sig": sig,
            "corner_sig": csig,
            "edges": {d: res["edges"][d]["material"] for d in ("N", "E", "S", "W")},
            "purity": round(min(res["edges"][d]["purity"] for d in ("N", "E", "S", "W")), 3),
            "corners": {d: res["corners"][d]["material"] for d in ("NW", "NE", "SW", "SE")},
            "centre": res["centre"]["material"],
        }
        per_tile.append(rec)
        if "?" in sig:
            ambiguous.append(rec)

    edge_mats = set()
    for rec in per_tile:
        for v in rec["edges"].values():
            if isinstance(v, int):
                edge_mats.add(v)

    solid = [r for r in per_tile if len(set(r["edges"].values())) == 1 and "?" not in r["sig"]]
    two_mat = [r for r in per_tile
               if len({v for v in r["edges"].values() if isinstance(v, int)}) == 2 and "?" not in r["sig"]]

    return {
        "pack": name,
        "tiles": len(tiles),
        "tile_sizes": {f"{w}x{h}": n for (w, h), n in sizes.items()},
        "materials": [{"id": m["id"], "hex": m["hex"], "share": round(m["share"], 3)} for m in mats],
        "materials_on_edges": sorted(edge_mats),
        "distinct_signatures": len(sig_counts),
        "top_signatures": sig_counts.most_common(12),
        "distinct_corner_signatures": len(csig_counts),
        "top_corner_signatures": csig_counts.most_common(12),
        "ambiguous_corner_tiles": camb,
        "solid_tiles": len(solid),
        "two_material_tiles": len(two_mat),
        "ambiguous_tiles": len(ambiguous),
        "per_tile": per_tile if verbose else per_tile[:0],
    }


def verdict(rep):
    """One-line judgement per pack, so the summary is readable at a glance."""
    n = rep["tiles"]
    if n == 0:
        return "empty"
    amb = min(rep["ambiguous_tiles"], rep["ambiguous_corner_tiles"]) / n
    mats = len(rep["materials"])
    if mats > 8:
        return f"NOT a terrain pack ({mats} materials) — decorative/architectural"
    if amb > 0.5:
        return f"UNRELIABLE — {amb:.0%} of edges too blended to label"
    if len(rep["materials_on_edges"]) < 2:
        return "single-material — base terrain, no transitions visible"
    model = "corner" if rep["ambiguous_corner_tiles"] <= rep["ambiguous_tiles"] else "edge"
    sigs = rep["distinct_corner_signatures"] if model == "corner" else rep["distinct_signatures"]
    if amb > 0.2:
        return f"USABLE with review — {amb:.0%} ambiguous, best model={model}"
    return f"CLEAN — {sigs} distinct {model} signatures"


# Which analysed material is the ruleset's PRIMARY terrain, per pack.
#
# This cannot be inferred. The analyser orders materials by pixel share, so
# material 0 is just "the most common colour in the sheet" -- sometimes that is
# the primary terrain, sometimes the secondary. The engine's corner mask sets a
# bit when a corner IS the terrain being painted (the primary), so emitting slot
# keys in material order silently inverts every pack where material 0 is the
# secondary. Measured end-to-end: with the keys unmapped, a fully-grass field
# resolved to the all-sand tile on 59 of 81 cells.
#
# Verified by reading each palette against the terrains the ruleset declares:
PACK_PRIMARY_MATERIAL = {
    "sand_grass": 0,   # primary sand   -> #e4cc9c beige (mat 0); grass is #24840c
    "grass_rock": 0,   # primary grass  -> #54843c green (mat 0); rock  is #84846c
    "sand_rock":  0,   # primary sand   -> #cc9c54 tan   (mat 0); rock  is #3c3c3c
    "rock_water": 0,   # primary rock   -> #3c3c3c dark  (mat 0); water is #246cb4
    "grass_dirt": 1,   # primary grass  -> #549c3c green (mat 1); dirt  is #54543c (mat 0)

    # Blob-island packs cut from the 2026-08-26 Aseprite batch. Only dirt_sand
    # reads as a clean two-material sheet; grass_sand and sand_ocean_3 carry a
    # third (and fourth) material on their edges -- the soil rim under the grass,
    # the surf band and the cliff line under the water -- which the binary
    # corner16 model has no symbol for. They are deliberately left out here so
    # emit_rulesets skips them loudly instead of collapsing a third terrain into
    # whichever of the two it happens to sit closest to in RGB.
    "dirt_sand":  1,   # primary dirt   -> #54543c dark   (mat 1); sand is #e4b484 (mat 0)
}


def emit_rulesets(reports, path):
    """Write the slot mapping Unity needs, for packs the analysis calls clean.

    Only packs that classify every tile AND cover all 16 corner combinations are
    emitted. A partially-covered pack would produce a ruleset with holes, and a
    hole shows up in game as a missing tile at exactly the configuration the
    author is trying to paint -- worse than having no ruleset at all, because it
    looks like a bug rather than an unfinished pack.
    """
    out = {"schemaVersion": 1, "model": "corner16", "packs": {}}
    skipped = {}
    for rep in reports:
        tiles = rep.get("per_tile") or []
        if not tiles:
            skipped[rep["pack"]] = "no per-tile data (run with --verbose)"
            continue

        by_sig = defaultdict(list)
        for t in tiles:
            sig = t["corner_sig"]
            if "?" in sig or "_" in sig:
                continue
            by_sig[sig].append(t["tile"])

        mats = sorted({ch for sig in by_sig for ch in sig})
        if len(mats) < 2:
            skipped[rep["pack"]] = f"only {len(mats)} material(s) on corners — nothing to transition between"
            continue
        a, b = mats[0], mats[1]
        combos = ["".join(x) for x in __import__("itertools").product((a, b), repeat=4)]
        missing = [c for c in combos if c not in by_sig]
        if missing:
            skipped[rep["pack"]] = f"{len(missing)}/16 corner combinations have no tile: {missing}"
            continue

        primary_mat = PACK_PRIMARY_MATERIAL.get(rep["pack"])
        if primary_mat is None:
            skipped[rep["pack"]] = ("no entry in PACK_PRIMARY_MATERIAL — which material is the "
                                    "primary terrain cannot be guessed, and guessing inverts the pack")
            continue
        # Re-key from material order to ENGINE order. The engine indexes corner
        # slots by the SECONDARY terrain -- TerrainTileResolver.ResolveVariantForCell
        # calls CornerMask(grid, cell, ruleset.TerrainSecondary) -- so a '1' here has
        # to mean "this corner shows the secondary terrain".
        #
        # Measured, both ways round: keying by material order inverted grass_dirt
        # (whose primary, grass, is material 1) while leaving the other four right;
        # keying by the primary inverted all five. Neither is guessable from the
        # artwork, because the analyser orders materials by pixel share and that has
        # nothing to do with which terrain the ruleset calls primary.
        secondary_char = str(1 - primary_mat)

        def to_engine_key(sig):
            return "".join("1" if ch == secondary_char else "0" for ch in sig)

        engine_slots = {}
        for sig, names in by_sig.items():
            if sig not in combos:
                continue
            engine_slots.setdefault(to_engine_key(sig), []).extend(names)

        out["packs"][rep["pack"]] = {
            "materialA": a,
            "materialB": b,
            "primaryMaterial": primary_mat,
            "palette": {str(m["id"]): m["hex"] for m in rep["materials"]},
            # Corner order is NW,NE,SE,SW — clockwise from top-left. '1' means the
            # corner shows the PRIMARY terrain (already re-keyed from material order).
            "cornerOrder": "NW,NE,SE,SW",
            "keyMeaning": "1 = corner is the SECONDARY terrain (matches TerrainTileResolver)",
            "slots": engine_slots,
            "extraSignatures": {s: v for s, v in by_sig.items() if s not in combos},
        }

    with open(path, "w", encoding="utf-8") as fh:
        json.dump(out, fh, indent=2)
    print(f"\nwrote {path}")
    print(f"  emitted : {', '.join(out['packs']) or '(none)'}")
    for pack, why in skipped.items():
        print(f"  skipped : {pack} — {why}")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--pack", help="analyse a single pack folder")
    ap.add_argument("--verbose", action="store_true", help="dump every tile into the JSON")
    ap.add_argument("--emit-rulesets", action="store_true",
                    help="also write tile_rulesets.json for the Unity importer (implies --verbose)")
    args = ap.parse_args()
    if args.emit_rulesets:
        args.verbose = True

    if not os.path.isdir(TILES_ROOT):
        sys.exit(f"Tiles root not found: {TILES_ROOT}")

    packs = sorted(d for d in os.listdir(TILES_ROOT)
                   if os.path.isdir(os.path.join(TILES_ROOT, d)))
    if args.pack:
        packs = [p for p in packs if p == args.pack] or sys.exit(f"no such pack: {args.pack}")

    os.makedirs(OUT_DIR, exist_ok=True)
    reports = []
    for p in packs:
        print(f"\n=== {p}")
        rep = analyse_pack(os.path.join(TILES_ROOT, p), p, args.verbose)
        if rep is None:
            print("    (no PNGs)")
            continue
        rep["verdict"] = verdict(rep)
        reports.append(rep)
        print(f"    tiles       : {rep['tiles']}  sizes={rep['tile_sizes']}")
        print(f"    materials   : {len(rep['materials'])}  " +
              " ".join(f"{m['hex']}({m['share']:.0%})" for m in rep["materials"][:6]))
        print(f"    on edges    : {rep['materials_on_edges']}")
        print(f"    edge sigs   : {rep['distinct_signatures']} distinct, {rep['ambiguous_tiles']} ambiguous")
        print(f"    corner sigs : {rep['distinct_corner_signatures']} distinct, {rep['ambiguous_corner_tiles']} ambiguous")
        print(f"    solid/2-mat : {rep['solid_tiles']} / {rep['two_material_tiles']}")
        print(f"    VERDICT     : {rep['verdict']}")
        if rep["distinct_corner_signatures"] <= 24:
            print("    top corner  : " + ", ".join(f"{s}x{c}" for s, c in rep["top_corner_signatures"][:8]))

    out = os.path.join(OUT_DIR, "tile_edge_analysis.json")
    with open(out, "w", encoding="utf-8") as fh:
        json.dump(reports, fh, indent=2)
    print(f"\nwrote {out}")

    if args.emit_rulesets:
        emit_rulesets(reports, os.path.join(OUT_DIR, "tile_rulesets.json"))


if __name__ == "__main__":
    main()
