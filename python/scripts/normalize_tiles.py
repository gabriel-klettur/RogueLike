"""
normalize_tiles.py — Normalizes all tiles in Unity project to 32x32 RGBA.

Operations:
  1. resize_48_to_32:  Nearest-neighbor downscale 48x48 → 32x32
  2. resize_64_to_32:  Nearest-neighbor downscale 64x64 → 32x32
  3. upscale_16_to_32: Nearest-neighbor upscale 16x16 → 32x32
  4. slice_tileset:    Cut large tilesets into 32x32 individual tiles
  5. ensure_rgba:      Convert RGB/P/L → RGBA

Safety:
  - Creates backup of every file before modifying (*.bak alongside original)
  - --dry-run mode shows plan without touching files
  - --validate mode checks all tiles post-normalization
  - Logs every action to normalize_tiles_log.json

Usage:
  python normalize_tiles.py --dry-run
  python normalize_tiles.py --execute
  python normalize_tiles.py --validate
"""

import argparse
import hashlib
import json
import os
import shutil
import struct
import sys
import time
from pathlib import Path
from collections import defaultdict

# ---------------------------------------------------------------------------
# Paths
# ---------------------------------------------------------------------------
SCRIPT_DIR = Path(__file__).resolve().parent
UNITY_ART = Path(__file__).resolve().parents[2] / "unity" / "Valkur" / "Assets" / "_Project" / "Art"
TILES_DIR = UNITY_ART / "Tiles"
BACKUP_DIR = TILES_DIR / "_backups"
LOG_PATH = SCRIPT_DIR.parent / "data" / "cache" / "normalize_tiles_log.json"

TARGET_SIZE = 32
TARGET_MODE = "RGBA"


# ---------------------------------------------------------------------------
# Image reading (stdlib fallback for dry-run without Pillow)
# ---------------------------------------------------------------------------
def read_png_dimensions(filepath):
    """Read PNG width/height/mode without Pillow."""
    try:
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
            mode_map = {0: "L", 2: "RGB", 3: "P", 4: "LA", 6: "RGBA"}
            return w, h, mode_map.get(color_type, "?")
    except Exception:
        return None


# ---------------------------------------------------------------------------
# Classification
# ---------------------------------------------------------------------------
def classify_tiles():
    """Scan all image files in Tiles/ and classify by required action."""
    IMAGE_EXTS = {".png", ".jpg", ".jpeg", ".gif", ".bmp"}
    plan = {
        "resize_48_to_32": [],
        "resize_64_to_32": [],
        "upscale_16_to_32": [],
        "slice_tileset": [],
        "ensure_rgba_only": [],
        "already_ok": [],
        "unreadable": [],
    }

    for root, dirs, files in os.walk(TILES_DIR):
        # Skip backup directory
        if "_backups" in root:
            continue
        for fname in files:
            fp = Path(root) / fname
            ext = fp.suffix.lower()
            if ext == ".meta" or ext not in IMAGE_EXTS:
                continue

            info = read_png_dimensions(fp)
            if info is None:
                plan["unreadable"].append(str(fp))
                continue

            w, h, mode = info
            rel = str(fp.relative_to(TILES_DIR)).replace("\\", "/")

            entry = {
                "path": str(fp),
                "rel": rel,
                "w": w,
                "h": h,
                "mode": mode,
            }

            if w == TARGET_SIZE and h == TARGET_SIZE:
                if mode != TARGET_MODE:
                    plan["ensure_rgba_only"].append(entry)
                else:
                    plan["already_ok"].append(entry)
            elif w == 48 and h == 48:
                plan["resize_48_to_32"].append(entry)
            elif w == 64 and h == 64:
                plan["resize_64_to_32"].append(entry)
            elif w == 16 and h == 16:
                plan["upscale_16_to_32"].append(entry)
            elif w > 64 or h > 64:
                plan["slice_tileset"].append(entry)
            else:
                # Odd sizes — resize to 32x32
                plan["slice_tileset"].append(entry)

    return plan


# ---------------------------------------------------------------------------
# Dry run
# ---------------------------------------------------------------------------
def dry_run():
    """Show what would be done without modifying files."""
    plan = classify_tiles()

    print("=" * 70)
    print("  TILE NORMALIZATION — DRY RUN")
    print("=" * 70)
    print()

    total_changes = 0
    for action, items in plan.items():
        if action == "already_ok":
            print(f"  {action}: {len(items)} files (no changes)")
            continue
        if not items:
            continue
        total_changes += len(items)
        print(f"\n  {action}: {len(items)} files")
        for item in sorted(items, key=lambda x: x.get("rel", x) if isinstance(x, dict) else x):
            if isinstance(item, dict):
                print(f"    {item['rel']}  [{item['w']}x{item['h']} {item['mode']}]")
            else:
                print(f"    {item}")

    print(f"\n  TOTAL: {total_changes} files to modify, {len(plan['already_ok'])} already OK")
    print(f"\n  Run with --execute to apply changes.")
    return plan


# ---------------------------------------------------------------------------
# Backup
# ---------------------------------------------------------------------------
def backup_file(filepath):
    """Create backup of file before modification."""
    fp = Path(filepath)
    rel = fp.relative_to(TILES_DIR)
    backup_path = BACKUP_DIR / rel
    backup_path.parent.mkdir(parents=True, exist_ok=True)
    if not backup_path.exists():
        shutil.copy2(fp, backup_path)
    return str(backup_path)


# ---------------------------------------------------------------------------
# Normalization operations (require Pillow)
# ---------------------------------------------------------------------------
def _ensure_rgba(img):
    """Convert any image mode to RGBA."""
    if img.mode == "RGBA":
        return img
    if img.mode == "P":
        return img.convert("RGBA")
    if img.mode in ("RGB", "L", "LA"):
        return img.convert("RGBA")
    return img.convert("RGBA")


def resize_tile(filepath, target=TARGET_SIZE):
    """Resize a tile to target x target using nearest neighbor, ensure RGBA."""
    from PIL import Image
    backup_file(filepath)
    with Image.open(filepath) as img:
        img = _ensure_rgba(img)
        img = img.resize((target, target), Image.NEAREST)
        img.save(filepath, "PNG")
    return True


def ensure_rgba(filepath):
    """Convert to RGBA without resizing."""
    from PIL import Image
    backup_file(filepath)
    with Image.open(filepath) as img:
        if img.mode != "RGBA":
            img = _ensure_rgba(img)
            img.save(filepath, "PNG")
    return True


def slice_tileset(filepath, grid_size=TARGET_SIZE):
    """
    Slice a large tileset into individual grid_size x grid_size tiles.
    Saves slices alongside the original, removes the original tileset.
    Discards fully transparent or fully uniform tiles.
    """
    from PIL import Image
    backup_file(filepath)

    fp = Path(filepath)
    stem = fp.stem
    parent = fp.parent

    with Image.open(filepath) as img:
        img = _ensure_rgba(img)
        w, h = img.size
        cols = w // grid_size
        rows = h // grid_size

        if cols == 0 or rows == 0:
            # Image smaller than grid — just resize
            img = img.resize((grid_size, grid_size), Image.NEAREST)
            img.save(filepath, "PNG")
            return [str(filepath)]

        slices = []
        for row in range(rows):
            for col in range(cols):
                x = col * grid_size
                y = row * grid_size
                tile = img.crop((x, y, x + grid_size, y + grid_size))

                # Skip fully transparent tiles
                extrema = tile.getextrema()
                if len(extrema) >= 4:
                    alpha_min, alpha_max = extrema[3]
                    if alpha_max == 0:
                        continue

                # Skip fully uniform tiles (single color)
                colors = tile.getcolors(maxcolors=2)
                if colors is not None and len(colors) == 1:
                    # Single color tile — skip if transparent
                    _, pixel = colors[0]
                    if len(pixel) >= 4 and pixel[3] == 0:
                        continue

                slice_name = f"{stem}_r{row}_c{col}.png"
                slice_path = parent / slice_name
                tile.save(slice_path, "PNG")
                slices.append(str(slice_path))

        # Remove original tileset (backup already exists)
        fp.unlink()

        # Also remove the .meta file for the original (Unity will regenerate)
        meta_path = fp.with_suffix(fp.suffix + ".meta")
        if meta_path.exists():
            backup_file(str(meta_path))
            meta_path.unlink()

        return slices


def compute_file_hash(filepath):
    """SHA-256 hash of file contents for deduplication."""
    h = hashlib.sha256()
    with open(filepath, "rb") as f:
        for chunk in iter(lambda: f.read(8192), b""):
            h.update(chunk)
    return h.hexdigest()


# ---------------------------------------------------------------------------
# Execute
# ---------------------------------------------------------------------------
def execute():
    """Run the full normalization pipeline."""
    from PIL import Image

    plan = classify_tiles()
    log = {
        "timestamp": time.strftime("%Y-%m-%d %H:%M:%S"),
        "actions": [],
        "summary": {},
    }

    print("=" * 70)
    print("  TILE NORMALIZATION — EXECUTING")
    print("=" * 70)

    # Create backup directory
    BACKUP_DIR.mkdir(parents=True, exist_ok=True)

    # 1. Resize 48→32
    items = plan["resize_48_to_32"]
    print(f"\n  [1/5] Resizing 48x48 → 32x32 ({len(items)} files)...")
    for item in items:
        resize_tile(item["path"])
        log["actions"].append({"action": "resize_48_to_32", "file": item["rel"]})
    print(f"    Done: {len(items)} files resized.")

    # 2. Resize 64→32
    items = plan["resize_64_to_32"]
    print(f"\n  [2/5] Resizing 64x64 → 32x32 ({len(items)} files)...")
    for item in items:
        resize_tile(item["path"])
        log["actions"].append({"action": "resize_64_to_32", "file": item["rel"]})
    print(f"    Done: {len(items)} files resized.")

    # 3. Upscale 16→32
    items = plan["upscale_16_to_32"]
    print(f"\n  [3/5] Upscaling 16x16 → 32x32 ({len(items)} files)...")
    for item in items:
        resize_tile(item["path"])
        log["actions"].append({"action": "upscale_16_to_32", "file": item["rel"]})
    print(f"    Done: {len(items)} files upscaled.")

    # 4. Slice tilesets
    items = plan["slice_tileset"]
    print(f"\n  [4/5] Slicing tilesets ({len(items)} files)...")
    total_slices = 0
    for item in items:
        slices = slice_tileset(item["path"])
        total_slices += len(slices)
        log["actions"].append({
            "action": "slice_tileset",
            "file": item["rel"],
            "original_size": f"{item['w']}x{item['h']}",
            "slices_created": len(slices),
        })
    print(f"    Done: {len(items)} tilesets sliced → {total_slices} individual tiles.")

    # 5. Ensure RGBA
    items = plan["ensure_rgba_only"]
    print(f"\n  [5/5] Converting to RGBA ({len(items)} files)...")
    for item in items:
        ensure_rgba(item["path"])
        log["actions"].append({"action": "ensure_rgba", "file": item["rel"]})
    print(f"    Done: {len(items)} files converted.")

    # Summary
    total_modified = (
        len(plan["resize_48_to_32"])
        + len(plan["resize_64_to_32"])
        + len(plan["upscale_16_to_32"])
        + len(plan["slice_tileset"])
        + len(plan["ensure_rgba_only"])
    )
    log["summary"] = {
        "total_modified": total_modified,
        "already_ok": len(plan["already_ok"]),
        "resize_48_to_32": len(plan["resize_48_to_32"]),
        "resize_64_to_32": len(plan["resize_64_to_32"]),
        "upscale_16_to_32": len(plan["upscale_16_to_32"]),
        "slice_tileset": len(plan["slice_tileset"]),
        "slices_created": total_slices,
        "ensure_rgba": len(plan["ensure_rgba_only"]),
    }

    print(f"\n  SUMMARY:")
    print(f"    Modified: {total_modified}")
    print(f"    Already OK: {len(plan['already_ok'])}")
    print(f"    Slices created: {total_slices}")
    print(f"    Backups in: {BACKUP_DIR}")

    # Save log
    LOG_PATH.parent.mkdir(parents=True, exist_ok=True)
    with open(LOG_PATH, "w", encoding="utf-8") as f:
        json.dump(log, f, indent=2, ensure_ascii=False)
    print(f"    Log saved to: {LOG_PATH}")

    print(f"\n  Run --validate to verify all tiles are 32x32 RGBA.")
    print(f"  Run --dedup to remove duplicate tiles.")


# ---------------------------------------------------------------------------
# Deduplication
# ---------------------------------------------------------------------------
def dedup():
    """Remove duplicate tiles (same pixel content) after slicing."""
    print("=" * 70)
    print("  TILE DEDUPLICATION")
    print("=" * 70)

    hashes = {}
    duplicates = []

    for root, dirs, files in os.walk(TILES_DIR):
        if "_backups" in root:
            continue
        for fname in files:
            fp = Path(root) / fname
            if fp.suffix.lower() != ".png":
                continue
            h = compute_file_hash(fp)
            if h in hashes:
                duplicates.append((str(fp), hashes[h]))
            else:
                hashes[h] = str(fp)

    print(f"\n  Unique tiles: {len(hashes)}")
    print(f"  Duplicates found: {len(duplicates)}")

    if duplicates:
        print(f"\n  Removing {len(duplicates)} duplicate files...")
        for dup_path, original_path in duplicates:
            dup = Path(dup_path)
            # Backup before removing
            backup_file(str(dup))
            dup.unlink()
            # Remove .meta too
            meta = dup.with_suffix(dup.suffix + ".meta")
            if meta.exists():
                meta.unlink()
            print(f"    Removed: {dup.relative_to(TILES_DIR)} (dup of {Path(original_path).relative_to(TILES_DIR)})")

        print(f"\n  Done: {len(duplicates)} duplicates removed.")
    else:
        print("  No duplicates found.")


# ---------------------------------------------------------------------------
# Validation
# ---------------------------------------------------------------------------
def validate():
    """Check that ALL tiles in Tiles/ are 32x32 RGBA."""
    print("=" * 70)
    print("  TILE VALIDATION")
    print("=" * 70)

    ok = 0
    errors = []

    for root, dirs, files in os.walk(TILES_DIR):
        if "_backups" in root:
            continue
        for fname in files:
            fp = Path(root) / fname
            if fp.suffix.lower() not in (".png", ".jpg", ".jpeg", ".gif", ".bmp"):
                continue

            info = read_png_dimensions(fp)
            rel = str(fp.relative_to(TILES_DIR)).replace("\\", "/")

            if info is None:
                errors.append(f"  UNREADABLE: {rel}")
                continue

            w, h, mode = info
            if w != TARGET_SIZE or h != TARGET_SIZE:
                errors.append(f"  WRONG SIZE: {rel} [{w}x{h}] (expected {TARGET_SIZE}x{TARGET_SIZE})")
            elif mode != TARGET_MODE:
                errors.append(f"  WRONG MODE: {rel} [{mode}] (expected {TARGET_MODE})")
            else:
                ok += 1

    print(f"\n  OK: {ok} tiles are {TARGET_SIZE}x{TARGET_SIZE} {TARGET_MODE}")

    if errors:
        print(f"  ERRORS: {len(errors)}")
        for e in sorted(errors):
            print(f"    {e}")
    else:
        print("  ALL TILES PASS VALIDATION ✓")

    return len(errors) == 0


# ---------------------------------------------------------------------------
# Main
# ---------------------------------------------------------------------------
if __name__ == "__main__":
    parser = argparse.ArgumentParser(description="Normalize Unity tiles to 32x32 RGBA")
    group = parser.add_mutually_exclusive_group(required=True)
    group.add_argument("--dry-run", action="store_true", help="Show plan without modifying files")
    group.add_argument("--execute", action="store_true", help="Execute normalization")
    group.add_argument("--validate", action="store_true", help="Validate all tiles are 32x32 RGBA")
    group.add_argument("--dedup", action="store_true", help="Remove duplicate tiles")

    args = parser.parse_args()

    if args.dry_run:
        dry_run()
    elif args.execute:
        execute()
    elif args.validate:
        validate()
    elif args.dedup:
        dedup()
