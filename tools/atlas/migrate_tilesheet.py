"""
migrate_tilesheet.py — Migrates a large tilesheet PNG into Valkur's tile system.

Pipeline:
  1. Validate the source PNG dimensions are multiples of cell size (default 32).
  2. Move and rename the source PNG into Art/Tiles/_source/<dest>/<snake_case>.png
     (the _source/ folder is exempt from ValkurAssetPostprocessor's 64px guard).
  3. Slice the PNG into N row*col individual cell PNGs, written to
     Resources/Tiles/<dest>/<prefix>_r{rr}_c{cc}.png (PPU=32 will be applied
     automatically by the postprocessor on Unity refresh).
  4. Compute SHA-256 of every cell. Assign uniqueId in first-seen order.
  5. Write _manifest.json next to the slices so the Tile Editor can detect
     a tilesheet category and render the new "tileset view" with dedup toggle.

The script never deletes existing slices outside the destination folder. The
source PNG IS deleted from its original location once the move succeeds (so
the git status stays clean).

Usage:
  python tools/atlas/migrate_tilesheet.py \\
    --source "unity/Valkur/Assets/_Project/Art/Buildings/castles/SNES - Secret of Mana - Maps - Pandora Castle (Exterior).png" \\
    --dest-name castle_pandora \\
    --dest-source-name pandora_castle_exterior.png \\
    --slice-prefix pandora \\
    --execute

  # dry-run shows the plan without writing anything:
  python tools/atlas/migrate_tilesheet.py --source "..." --dest-name castle_pandora --dry-run
"""

import argparse
import hashlib
import json
import shutil
import struct
import sys
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[2]
UNITY_ASSETS = REPO_ROOT / "unity" / "Valkur" / "Assets" / "_Project"
ART_TILES_SOURCE = UNITY_ASSETS / "Art" / "Tiles" / "_source"
RESOURCES_TILES = UNITY_ASSETS / "Resources" / "Tiles"

DEFAULT_CELL = 32
SCHEMA_VERSION = 1


def rel(path: Path) -> str:
    """Repo-relative display path, falling back to the absolute one.

    A source PNG staged outside the repo (an export written to a temp dir, say)
    is a legitimate input -- the migration copies it in -- but Path.relative_to
    raises on it, which used to kill the run inside the very first banner line,
    after nothing had been written. Display must never be able to fail a job.
    """
    try:
        return str(path.relative_to(REPO_ROOT))
    except ValueError:
        return str(path)


def read_png_dimensions(filepath: Path):
    """Read PNG width/height/color_type without Pillow.

    Returns (width, height, color_type_int) or None on failure.
    """
    with open(filepath, "rb") as f:
        sig = f.read(8)
        if sig[:4] != b"\x89PNG":
            return None
        length = struct.unpack(">I", f.read(4))[0]
        chunk_type = f.read(4)
        if chunk_type != b"IHDR":
            return None
        data = f.read(length)
        w, h = struct.unpack(">II", data[:8])
        color_type = data[9]
        return w, h, color_type


def slice_and_manifest(source_png: Path, out_dir: Path, prefix: str, cell: int,
                      manifest_source_name: str, dry_run: bool):
    """Slice source_png into cell-sized tiles in out_dir + write manifest."""
    from PIL import Image

    img = Image.open(source_png)
    if img.mode != "RGBA":
        img = img.convert("RGBA")

    w, h = img.size
    cols = w // cell
    rows = h // cell
    if w % cell != 0 or h % cell != 0:
        raise ValueError(
            f"Source PNG {source_png.name} is {w}x{h}; not a multiple of cell={cell}.")

    print(f"  Source: {source_png.name}  ({w}x{h}, {cols} cols x {rows} rows = {cols*rows} cells)")

    cells_meta = []
    uniques = []
    hash_to_unique_id = {}

    if not dry_run:
        out_dir.mkdir(parents=True, exist_ok=True)

    for r in range(rows):
        for c in range(cols):
            x = c * cell
            y = r * cell
            tile = img.crop((x, y, x + cell, y + cell))

            # Hash the raw pixel bytes (after RGBA normalization) for content
            # equality. Saving and re-hashing the file would also work but
            # would double the I/O.
            tile_bytes = tile.tobytes()
            h_hex = hashlib.sha256(tile_bytes).hexdigest()

            # Determine if the cell is fully transparent (skip-paint hint).
            extrema = tile.getextrema()
            is_transparent = (
                len(extrema) >= 4 and extrema[3] == (0, 0)
            )

            if h_hex not in hash_to_unique_id:
                unique_id = len(uniques)
                hash_to_unique_id[h_hex] = unique_id
                file_name = f"{prefix}_r{r:02d}_c{c:02d}"
                uniques.append({"id": unique_id, "file": file_name, "hash": h_hex})
            else:
                unique_id = hash_to_unique_id[h_hex]

            file_name = f"{prefix}_r{r:02d}_c{c:02d}"
            slice_path = out_dir / f"{file_name}.png"

            if not dry_run:
                tile.save(slice_path, "PNG")

            cells_meta.append({
                "r": r,
                "c": c,
                "file": file_name,
                "uniqueId": unique_id,
                "transparent": is_transparent,
            })

    manifest = {
        "schemaVersion": SCHEMA_VERSION,
        "source": manifest_source_name,
        "cellPx": cell,
        "cols": cols,
        "rows": rows,
        "cells": cells_meta,
        "uniques": uniques,
    }

    manifest_path = out_dir / "_manifest.json"
    if not dry_run:
        with open(manifest_path, "w", encoding="utf-8") as f:
            json.dump(manifest, f, indent=2, ensure_ascii=False)

    print(f"  Wrote {len(cells_meta)} cell PNGs ({len(uniques)} unique tiles) + _manifest.json")
    print(f"  Output dir: {rel(out_dir)}")
    return manifest


def move_source_png(source_png: Path, dest_dir: Path, dest_name: str, dry_run: bool):
    """Move and rename the source PNG into Art/Tiles/_source/<dest>/."""
    target = dest_dir / dest_name
    print(f"  Move source: {rel(source_png)} -> {rel(target)}")
    if dry_run:
        return target
    dest_dir.mkdir(parents=True, exist_ok=True)
    shutil.move(str(source_png), str(target))

    # Also clean up any orphan .meta from the original location.
    orig_meta = source_png.with_suffix(source_png.suffix + ".meta")
    if orig_meta.exists():
        orig_meta.unlink()
        print(f"  Removed orphan meta: {rel(orig_meta)}")

    return target



def rebuild_pack_manifest(pack_dir: Path, dry_run: bool):
    """Merge every ``<pack>/*_slices/_manifest.json`` into one at the pack root.

    A pack may hold several sheets of the SAME terrain pair (grass_rock already
    ships three). The Tile Editor reads exactly one manifest per category --
    ``TileCatalog.BuildFromResources`` probes ``Tiles/<cat>/_manifest`` at the
    category ROOT while ``Resources.LoadAll<Sprite>`` under it recurses -- so a
    multi-sheet pack with only per-sheet manifests falls back to the legacy flat
    list and silently loses the grid view, the (r, c) coordinates and the dedup
    toggle. Merging is what keeps a variant pack looking like one sheet.

    Sheets stack VERTICALLY in sorted subfolder order: sheet k takes rows
    [offset, offset + rows_k). Column counts must agree across sheets, which
    they do for any pack cut from same-sized sources; a mismatch raises rather
    than writing a misaligned grid.

    uniqueId is re-derived across the WHOLE pack from the per-cell hashes the
    per-sheet manifests already carry, so the dedup toggle collapses the filler
    cells (pure sand, pure water) that every variant of a blob template repeats
    -- exactly what that toggle exists for. No PNG is re-read.

    Idempotent: it reads only the per-sheet manifests and fully rewrites the
    root one, so re-running after adding a fourth sheet is the whole update.
    """
    subs = sorted(d for d in pack_dir.iterdir()
                  if d.is_dir() and (d / "_manifest.json").exists())
    if not subs:
        return None

    per_sheet = [json.loads((d / "_manifest.json").read_text(encoding="utf-8")) for d in subs]

    cols = per_sheet[0]["cols"]
    for sub, m in zip(subs, per_sheet):
        if m["cols"] != cols:
            raise ValueError(
                f"{pack_dir.name}: sheet '{sub.name}' has {m['cols']} cols but the pack "
                f"is {cols}. Merging would misalign the grid.")

    row_offset = 0
    cells = []
    uniques = []
    hash_to_id = {}
    sources = []

    for sub, m in zip(subs, per_sheet):
        # A cell that is not itself a "unique" shares another cell's hash, so
        # recover it through the uniqueId rather than re-hashing the PNG.
        id_to_hash = {u["id"]: u["hash"] for u in m.get("uniques", [])}
        for cell in m["cells"]:
            h = id_to_hash.get(cell["uniqueId"])
            if h is None:
                raise ValueError(
                    f"{sub.name}/_manifest.json: cell '{cell['file']}' references "
                    f"uniqueId {cell['uniqueId']}, which the manifest does not define.")
            if h not in hash_to_id:
                hash_to_id[h] = len(uniques)
                uniques.append({"id": hash_to_id[h], "file": cell["file"], "hash": h})
            cells.append({
                "r": cell["r"] + row_offset,
                "c": cell["c"],
                "file": cell["file"],
                "uniqueId": hash_to_id[h],
                "transparent": cell["transparent"],
            })
        row_offset += m["rows"]
        sources.append(m.get("source", sub.name))

    merged = {
        "schemaVersion": SCHEMA_VERSION,
        "source": " + ".join(sources),
        "cellPx": per_sheet[0]["cellPx"],
        "cols": cols,
        "rows": row_offset,
        "cells": cells,
        "uniques": uniques,
    }
    print(f"  Merged {len(subs)} sheet manifest(s) -> {pack_dir.name}/_manifest.json "
          f"({cols} x {row_offset} grid, {len(cells)} cells, {len(uniques)} unique)")
    if not dry_run:
        with open(pack_dir / "_manifest.json", "w", encoding="utf-8") as f:
            json.dump(merged, f, indent=2, ensure_ascii=False)
    return merged


def main():
    parser = argparse.ArgumentParser(
        description="Migrate a large tilesheet PNG into the Valkur tile system.")
    parser.add_argument("--source", required=True,
                       help="Path to the source PNG (relative or absolute).")
    parser.add_argument("--dest-name", required=True,
                       help="Category name (folder under Resources/Tiles/ AND under Art/Tiles/_source/). "
                            "Must be snake_case, e.g. castle_pandora.")
    parser.add_argument("--dest-source-name", required=True,
                       help="Snake_case filename for the moved source, e.g. pandora_castle_exterior.png")
    parser.add_argument("--slice-prefix", required=True,
                       help="Prefix for sliced cell filenames, e.g. 'pandora' -> pandora_r00_c00.png")
    parser.add_argument("--slices-subdir", default=None,
                       help="Optional subfolder under Resources/Tiles/<dest-name>/ for this "
                            "sheet's slices, e.g. 'dirt_sand2_slices'. Use it when one pack is "
                            "cut from several sheets of the same terrain pair; the per-sheet "
                            "manifests are then merged into one at the pack root so the Tile "
                            "Editor still sees a single grid.")
    parser.add_argument("--cell", type=int, default=DEFAULT_CELL,
                       help=f"Cell size in pixels (default {DEFAULT_CELL}).")
    group = parser.add_mutually_exclusive_group(required=True)
    group.add_argument("--dry-run", action="store_true", help="Show plan without writing files.")
    group.add_argument("--execute", action="store_true", help="Apply the migration.")
    args = parser.parse_args()

    source_path = Path(args.source)
    if not source_path.is_absolute():
        source_path = (REPO_ROOT / source_path).resolve()
    if not source_path.exists():
        print(f"ERROR: source PNG not found: {source_path}", file=sys.stderr)
        return 1

    info = read_png_dimensions(source_path)
    if info is None:
        print(f"ERROR: could not read PNG header: {source_path}", file=sys.stderr)
        return 1
    w, h, _ = info
    if w % args.cell != 0 or h % args.cell != 0:
        print(f"ERROR: source {w}x{h} is not a multiple of cell={args.cell}.", file=sys.stderr)
        return 1

    if not args.dest_name.replace("_", "").isalnum() or args.dest_name != args.dest_name.lower():
        print(f"ERROR: --dest-name must be snake_case lowercase: {args.dest_name!r}", file=sys.stderr)
        return 1

    dest_source_dir = ART_TILES_SOURCE / args.dest_name
    if args.slices_subdir is not None and (
            not args.slices_subdir.replace("_", "").isalnum()
            or args.slices_subdir != args.slices_subdir.lower()):
        print(f"ERROR: --slices-subdir must be snake_case lowercase: {args.slices_subdir!r}",
              file=sys.stderr)
        return 1

    pack_dir = RESOURCES_TILES / args.dest_name
    dest_resources_dir = pack_dir / args.slices_subdir if args.slices_subdir else pack_dir

    print("=" * 70)
    print("  TILESHEET MIGRATION " + ("(DRY-RUN)" if args.dry_run else "(EXECUTE)"))
    print("=" * 70)
    print(f"  Source PNG:   {rel(source_path)}")
    print(f"  Dimensions:   {w}x{h} px")
    print(f"  Cell:         {args.cell}x{args.cell}")
    print(f"  Cols x Rows:  {w // args.cell} x {h // args.cell}  ({(w // args.cell) * (h // args.cell)} cells)")
    print(f"  Dest source:  {rel((dest_source_dir / args.dest_source_name))}")
    print(f"  Dest slices:  {rel(dest_resources_dir)}/")
    print()

    # Pillow is required only for the actual slice; defer the import so dry-run
    # can run on machines without Pillow installed.
    try:
        from PIL import Image  # noqa: F401
    except ImportError:
        if args.execute:
            print("ERROR: Pillow is required for --execute. pip install pillow", file=sys.stderr)
            return 2
        print("  (Pillow not installed; skipping slice preview in dry-run.)")

    # Step 1: slice + manifest. Do this BEFORE moving so we can re-run safely
    # if the user aborts halfway.
    total_steps = 3 if args.slices_subdir else 2
    print(f"[1/{total_steps}] Slicing source PNG and writing manifest...")
    slice_and_manifest(
        source_png=source_path,
        out_dir=dest_resources_dir,
        prefix=args.slice_prefix,
        cell=args.cell,
        manifest_source_name=Path(args.dest_source_name).stem,
        dry_run=args.dry_run,
    )

    # Step 2: move source PNG into _source/. (After slicing, so if the slice
    # fails we still have the original in place.)
    print(f"\n[2/{total_steps}] Moving source PNG into _source/...")
    move_source_png(
        source_png=source_path,
        dest_dir=dest_source_dir,
        dest_name=args.dest_source_name,
        dry_run=args.dry_run,
    )

    if args.slices_subdir:
        print(f"\n[3/{total_steps}] Merging per-sheet manifests into the pack root...")
        if args.dry_run:
            print("  (dry-run: merge skipped -- it reads manifests that were not written.)")
        else:
            rebuild_pack_manifest(pack_dir, dry_run=False)

    if args.dry_run:
        print("\n  Dry-run complete. Re-run with --execute to apply.")
    else:
        print("\n  Migration complete.")
        print("  Next: open Unity, refresh assets, then check the F8 Tile Editor for the new tab.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
