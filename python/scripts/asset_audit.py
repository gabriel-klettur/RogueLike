"""
Asset Audit Script — Scans python/assets/ and generates a detailed report
of all image files: dimensions, format, color mode, size, and anomalies.
Uses only stdlib (no Pillow required) — reads PNG/GIF headers directly.
"""
import os
import struct
import json
from pathlib import Path
from collections import defaultdict

ASSETS_DIR = Path(__file__).resolve().parents[1] / "assets"


def read_png_info(filepath):
    """Read width, height, bit_depth, color_type from PNG IHDR chunk."""
    try:
        with open(filepath, "rb") as f:
            sig = f.read(8)
            if sig[:4] != b"\x89PNG":
                return None
            # IHDR chunk
            length = struct.unpack(">I", f.read(4))[0]
            chunk_type = f.read(4)
            if chunk_type != b"IHDR":
                return None
            data = f.read(length)
            w, h = struct.unpack(">II", data[:8])
            bit_depth = data[8]
            color_type = data[9]
            # color_type: 0=gray, 2=RGB, 3=palette, 4=gray+alpha, 6=RGBA
            mode_map = {0: "L", 2: "RGB", 3: "P", 4: "LA", 6: "RGBA"}
            mode = mode_map.get(color_type, "?")
            return {"w": w, "h": h, "bit_depth": bit_depth, "mode": mode}
    except Exception:
        return None


def read_gif_info(filepath):
    """Read width, height from GIF header."""
    try:
        with open(filepath, "rb") as f:
            sig = f.read(6)
            if sig[:3] != b"GIF":
                return None
            w, h = struct.unpack("<HH", f.read(4))
            return {"w": w, "h": h, "bit_depth": 8, "mode": "P"}
    except Exception:
        return None


def read_jpg_info(filepath):
    """Read width, height from JPEG SOF marker."""
    try:
        with open(filepath, "rb") as f:
            data = f.read()
            # Find SOF0 or SOF2 marker
            for marker in [b"\xff\xc0", b"\xff\xc2"]:
                idx = data.find(marker)
                if idx != -1:
                    # Skip marker (2) + length (2) + precision (1)
                    h, w = struct.unpack(">HH", data[idx + 5 : idx + 9])
                    return {"w": w, "h": h, "bit_depth": 8, "mode": "RGB"}
        return None
    except Exception:
        return None


def read_image_info(filepath):
    """Dispatch to format-specific reader based on extension."""
    ext = filepath.suffix.lower()
    if ext == ".png":
        return read_png_info(filepath)
    elif ext == ".gif":
        return read_gif_info(filepath)
    elif ext in (".jpg", ".jpeg"):
        return read_jpg_info(filepath)
    return None


def scan_all_images():
    """Walk assets dir and collect metadata for every image file."""
    IMAGE_EXTS = {".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".avif", ".tiff"}
    results = []

    # Define categories by top-level folder
    for root, dirs, files in os.walk(ASSETS_DIR):
        for fname in files:
            fp = Path(root) / fname
            ext = fp.suffix.lower()
            if ext not in IMAGE_EXTS:
                continue

            rel = str(fp.relative_to(ASSETS_DIR)).replace("\\", "/")
            category = rel.split("/")[0]
            size_bytes = fp.stat().st_size
            size_kb = round(size_bytes / 1024, 1)

            info = read_image_info(fp)
            if info:
                results.append({
                    "path": rel,
                    "category": category,
                    "ext": ext,
                    "w": info["w"],
                    "h": info["h"],
                    "bit_depth": info["bit_depth"],
                    "mode": info["mode"],
                    "size_kb": size_kb,
                })
            else:
                results.append({
                    "path": rel,
                    "category": category,
                    "ext": ext,
                    "w": 0,
                    "h": 0,
                    "bit_depth": 0,
                    "mode": "UNREADABLE",
                    "size_kb": size_kb,
                })
    return results


def is_pow2(n):
    return n > 0 and (n & (n - 1)) == 0


def generate_report(images):
    """Print comprehensive report."""

    # --- Per-category breakdown ---
    by_cat = defaultdict(list)
    for img in images:
        by_cat[img["category"]].append(img)

    for cat in sorted(by_cat.keys()):
        imgs = by_cat[cat]
        print()
        print("=" * 60)
        print(f"  {cat.upper()} ({len(imgs)} images)")
        print("=" * 60)

        # Dimensions
        dim_groups = defaultdict(list)
        for img in imgs:
            key = f"{img['w']}x{img['h']}"
            dim_groups[key].append(img["path"])
        print("\n  Dimensions:")
        for dim, paths in sorted(dim_groups.items(), key=lambda x: -len(x[1])):
            print(f"    {dim}: {len(paths)} files")
            if len(paths) <= 3:
                for p in paths:
                    print(f"      - {p}")

        # Color modes
        modes = defaultdict(int)
        for img in imgs:
            modes[img["mode"]] += 1
        print(f"\n  Color modes: {dict(modes)}")

        # Extensions
        exts = defaultdict(int)
        for img in imgs:
            exts[img["ext"]] += 1
        print(f"  Extensions: {dict(exts)}")

        # Size range
        sizes = [img["size_kb"] for img in imgs]
        if sizes:
            print(f"  Size range: {min(sizes)} KB - {max(sizes)} KB (total: {round(sum(sizes)/1024, 1)} MB)")

    # --- Global summary ---
    print()
    print("=" * 60)
    print("  GLOBAL SUMMARY")
    print("=" * 60)
    print(f"  Total images: {len(images)}")
    total_mb = round(sum(i["size_kb"] for i in images) / 1024, 1)
    print(f"  Total size: {total_mb} MB")

    # Power-of-2 check
    pow2_count = sum(1 for i in images if is_pow2(i["w"]) and is_pow2(i["h"]) and i["w"] > 0)
    non_pow2 = [i for i in images if not (is_pow2(i["w"]) and is_pow2(i["h"])) and i["w"] > 0]
    print(f"  Power-of-2 dimensions: {pow2_count}")
    print(f"  Non-power-of-2 dimensions: {len(non_pow2)}")

    # Top unique dimensions
    all_dims = defaultdict(int)
    for img in images:
        key = f"{img['w']}x{img['h']}"
        all_dims[key] += 1
    print("\n  Top 20 unique dimensions:")
    for dim, count in sorted(all_dims.items(), key=lambda x: -x[1])[:20]:
        p2 = "POW2" if all(is_pow2(int(d)) for d in dim.split("x") if int(d) > 0) else "non-POW2"
        print(f"    {dim}: {count} files [{p2}]")

    # --- TILESET ANALYSIS ---
    print()
    print("=" * 60)
    print("  TILESET ANALYSIS (tiles that need slicing)")
    print("=" * 60)
    tile_imgs = [i for i in images if i["category"] == "tiles"]
    tilesets_to_slice = []
    already_sliced = []
    for t in tile_imgs:
        if t["w"] > 64 or t["h"] > 64:
            cols = t["w"] // 32
            rows = t["h"] // 32
            tilesets_to_slice.append(t)
            print(f"  TILESET {t['path']}: {t['w']}x{t['h']} -> {cols}x{rows} grid = {cols*rows} potential tiles")
        elif t["w"] == 32 and t["h"] == 32:
            already_sliced.append(t)
    print(f"\n  Tilesets needing slice: {len(tilesets_to_slice)}")
    print(f"  Already 32x32 tiles: {len(already_sliced)}")

    # --- CHARACTER SPRITE SHEET ANALYSIS ---
    print()
    print("=" * 60)
    print("  CHARACTER SPRITE SHEET ANALYSIS")
    print("=" * 60)
    char_imgs = [i for i in images if i["category"] == "characters"]
    for c in char_imgs:
        if c["w"] >= 128 or c["h"] >= 128:
            cols128 = c["w"] // 128
            rows128 = c["h"] // 128
            print(f"  {c['path']}: {c['w']}x{c['h']} (128px grid: {cols128}x{rows128} = {cols128*rows128} frames)")

    # --- NPC/MONSTER SPRITES ---
    print()
    print("=" * 60)
    print("  NPC/MONSTER SPRITE ANALYSIS")
    print("=" * 60)
    npc_imgs = [i for i in images if i["category"] == "npc"]
    for n in npc_imgs:
        print(f"  {n['path']}: {n['w']}x{n['h']} mode={n['mode']} size={n['size_kb']}KB")
    if not npc_imgs:
        print("  (no readable images found in npc/)")

    # --- ANOMALIES ---
    print()
    print("=" * 60)
    print("  ANOMALIES & ISSUES")
    print("=" * 60)

    # Unreadable files
    unreadable = [i for i in images if i["mode"] == "UNREADABLE"]
    if unreadable:
        print(f"\n  Unreadable files ({len(unreadable)}):")
        for u in unreadable:
            print(f"    - {u['path']} ({u['ext']}, {u['size_kb']}KB)")

    # Very large files (>1MB)
    large = [i for i in images if i["size_kb"] > 1024]
    if large:
        print(f"\n  Very large files >1MB ({len(large)}):")
        for l in sorted(large, key=lambda x: -x["size_kb"]):
            print(f"    - {l['path']}: {round(l['size_kb']/1024, 1)}MB ({l['w']}x{l['h']})")

    # Mixed extensions (PNG vs png)
    mixed_ext = [i for i in images if i["ext"] != i["ext"].lower() or i["ext"] == ".png" and i["path"].endswith(".PNG")]
    # Actually check original filename
    mixed_case = []
    for i in images:
        orig_ext = Path(i["path"]).suffix
        if orig_ext != orig_ext.lower():
            mixed_case.append(i)
    if mixed_case:
        print(f"\n  Mixed-case extensions ({len(mixed_case)}):")
        for m in mixed_case[:10]:
            print(f"    - {m['path']}")

    # Non-RGBA PNGs (missing alpha channel)
    non_rgba_png = [i for i in images if i["ext"] == ".png" and i["mode"] not in ("RGBA", "LA", "P", "UNREADABLE")]
    if non_rgba_png:
        print(f"\n  PNGs without alpha channel ({len(non_rgba_png)}):")
        for n in non_rgba_png[:10]:
            print(f"    - {n['path']}: mode={n['mode']}")

    # Non-32x32 tiles in multi_tiles (should all be 32x32)
    multi_tiles = [i for i in tile_imgs if "multi_tiles/tiles/" in i["path"]]
    non_standard_multi = [i for i in multi_tiles if i["w"] != 32 or i["h"] != 32]
    if non_standard_multi:
        print(f"\n  Non-32x32 tiles in multi_tiles/ ({len(non_standard_multi)}):")
        for n in non_standard_multi[:10]:
            print(f"    - {n['path']}: {n['w']}x{n['h']}")

    # Files in download/ (likely not production assets)
    download_imgs = [i for i in images if i["category"] == "download"]
    if download_imgs:
        print(f"\n  Files in download/ (likely non-production): {len(download_imgs)}")

    # Files in AAA_in_process/ (work in progress)
    wip_imgs = [i for i in images if i["category"] == "AAA_in_process"]
    if wip_imgs:
        print(f"  Files in AAA_in_process/ (work in progress): {len(wip_imgs)}")

    return images


if __name__ == "__main__":
    print("Scanning", ASSETS_DIR, "...")
    images = scan_all_images()
    generate_report(images)

    # Save raw data as JSON for further processing
    output_path = Path(__file__).resolve().parents[1] / "data" / "cache" / "asset_audit.json"
    output_path.parent.mkdir(parents=True, exist_ok=True)
    with open(output_path, "w", encoding="utf-8") as f:
        json.dump(images, f, indent=2, ensure_ascii=False)
    print(f"\nRaw data saved to: {output_path}")
