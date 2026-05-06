"""
Unity Asset Audit Script — Scans unity/Valkur/Assets/_Project/Art/
and generates a detailed per-file report for atlas normalization.
Uses only stdlib (no Pillow) — reads PNG/GIF/JPG headers directly.
"""
import os
import struct
import json
from pathlib import Path
from collections import defaultdict

UNITY_ART_DIR = Path(__file__).resolve().parents[2] / "unity" / "Valkur" / "Assets" / "_Project" / "Art"


def read_png_info(filepath):
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
            bit_depth = data[8]
            color_type = data[9]
            mode_map = {0: "L", 2: "RGB", 3: "P", 4: "LA", 6: "RGBA"}
            mode = mode_map.get(color_type, "?")
            return {"w": w, "h": h, "bit_depth": bit_depth, "mode": mode}
    except Exception:
        return None


def read_gif_info(filepath):
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
    try:
        with open(filepath, "rb") as f:
            data = f.read()
            for marker in [b"\xff\xc0", b"\xff\xc2"]:
                idx = data.find(marker)
                if idx != -1:
                    h, w = struct.unpack(">HH", data[idx + 5 : idx + 9])
                    return {"w": w, "h": h, "bit_depth": 8, "mode": "RGB"}
        return None
    except Exception:
        return None


def read_image_info(filepath):
    ext = filepath.suffix.lower()
    if ext == ".png":
        return read_png_info(filepath)
    elif ext == ".gif":
        return read_gif_info(filepath)
    elif ext in (".jpg", ".jpeg"):
        return read_jpg_info(filepath)
    return None


def is_pow2(n):
    return n > 0 and (n & (n - 1)) == 0


def scan_all_images():
    IMAGE_EXTS = {".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".avif", ".tiff"}
    results = []
    for root, dirs, files in os.walk(UNITY_ART_DIR):
        for fname in files:
            fp = Path(root) / fname
            ext = fp.suffix.lower()
            if ext == ".meta":
                continue
            if ext not in IMAGE_EXTS:
                continue
            rel = str(fp.relative_to(UNITY_ART_DIR)).replace("\\", "/")
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


def generate_report(images):
    by_cat = defaultdict(list)
    for img in images:
        by_cat[img["category"]].append(img)

    for cat in sorted(by_cat.keys()):
        imgs = by_cat[cat]
        print()
        print("=" * 70)
        print("  " + cat.upper() + " (" + str(len(imgs)) + " images)")
        print("=" * 70)

        # Dimensions
        dim_groups = defaultdict(list)
        for img in imgs:
            key = str(img["w"]) + "x" + str(img["h"])
            dim_groups[key].append(img["path"])
        print("\n  Dimensions:")
        for dim, paths in sorted(dim_groups.items(), key=lambda x: -len(x[1])):
            print("    " + dim + ": " + str(len(paths)) + " files")
            if len(paths) <= 5:
                for p in paths:
                    print("      - " + p)

        # Color modes
        modes = defaultdict(int)
        for img in imgs:
            modes[img["mode"]] += 1
        print("\n  Color modes: " + str(dict(modes)))

        # Extensions
        exts = defaultdict(int)
        for img in imgs:
            exts[img["ext"]] += 1
        print("  Extensions: " + str(dict(exts)))

        # Size range
        sizes = [img["size_kb"] for img in imgs]
        if sizes:
            print("  Size range: " + str(min(sizes)) + " KB - " + str(max(sizes)) + " KB (total: " + str(round(sum(sizes)/1024, 1)) + " MB)")

    # Global summary
    print()
    print("=" * 70)
    print("  GLOBAL SUMMARY")
    print("=" * 70)
    print("  Total images: " + str(len(images)))
    total_mb = round(sum(i["size_kb"] for i in images) / 1024, 1)
    print("  Total size: " + str(total_mb) + " MB")

    pow2_count = sum(1 for i in images if is_pow2(i["w"]) and is_pow2(i["h"]) and i["w"] > 0)
    non_pow2 = [i for i in images if not (is_pow2(i["w"]) and is_pow2(i["h"])) and i["w"] > 0]
    print("  Power-of-2 dimensions: " + str(pow2_count))
    print("  Non-power-of-2 dimensions: " + str(len(non_pow2)))

    # Top unique dimensions
    all_dims = defaultdict(int)
    for img in images:
        key = str(img["w"]) + "x" + str(img["h"])
        all_dims[key] += 1
    print("\n  Top 20 unique dimensions:")
    for dim, count in sorted(all_dims.items(), key=lambda x: -x[1])[:20]:
        parts = dim.split("x")
        p2 = "POW2" if all(is_pow2(int(d)) for d in parts if int(d) > 0) else "non-POW2"
        print("    " + dim + ": " + str(count) + " files [" + p2 + "]")

    # Per-file action needed
    print()
    print("=" * 70)
    print("  PER-FILE NORMALIZATION ACTIONS")
    print("=" * 70)

    actions = {
        "resize_to_32": [],
        "slice_tileset": [],
        "resize_to_64": [],
        "resize_to_128": [],
        "auto_crop_resize": [],
        "ensure_rgba": [],
        "rename_extension": [],
        "rename_spaces": [],
        "ok_no_change": [],
        "exclude": [],
    }

    for img in images:
        cat = img["category"]
        w, h = img["w"], img["h"]
        mode = img["mode"]
        path = img["path"]
        ext_orig = Path(path).suffix

        # Check naming issues
        if ext_orig != ext_orig.lower():
            actions["rename_extension"].append(img)
        if " " in path:
            actions["rename_spaces"].append(img)

        # Check alpha
        needs_rgba = (img["ext"] == ".png" and mode in ("RGB", "L"))

        # Category-specific rules
        if cat == "Tiles":
            if w == 32 and h == 32:
                if needs_rgba:
                    actions["ensure_rgba"].append(img)
                else:
                    actions["ok_no_change"].append(img)
            elif w > 64 or h > 64:
                actions["slice_tileset"].append(img)
            else:
                actions["resize_to_32"].append(img)

        elif cat == "Characters":
            # Sprite sheets are fine as-is
            if needs_rgba:
                actions["ensure_rgba"].append(img)
            else:
                actions["ok_no_change"].append(img)

        elif cat == "NPC":
            if w > 256 or h > 256:
                actions["auto_crop_resize"].append(img)
            elif needs_rgba:
                actions["ensure_rgba"].append(img)
            else:
                actions["ok_no_change"].append(img)

        elif cat == "Buildings":
            if w > 256 or h > 256:
                actions["auto_crop_resize"].append(img)
            elif needs_rgba:
                actions["ensure_rgba"].append(img)
            else:
                actions["ok_no_change"].append(img)

        elif cat == "Items":
            if w > 64 or h > 64:
                actions["resize_to_64"].append(img)
            elif needs_rgba:
                actions["ensure_rgba"].append(img)
            else:
                actions["ok_no_change"].append(img)

        elif cat == "UI":
            if w > 128 or h > 128:
                actions["auto_crop_resize"].append(img)
            elif needs_rgba:
                actions["ensure_rgba"].append(img)
            else:
                actions["ok_no_change"].append(img)

        elif cat == "Spells":
            if w > 128 or h > 128:
                actions["resize_to_128"].append(img)
            elif needs_rgba:
                actions["ensure_rgba"].append(img)
            else:
                actions["ok_no_change"].append(img)

        elif cat == "VFX":
            # particles are 256x256, keep as-is
            if needs_rgba:
                actions["ensure_rgba"].append(img)
            else:
                actions["ok_no_change"].append(img)

        else:
            actions["ok_no_change"].append(img)

    for action_name, items in actions.items():
        if not items:
            continue
        print()
        print("  --- " + action_name.upper() + " (" + str(len(items)) + " files) ---")
        for item in sorted(items, key=lambda x: x["path"]):
            dim = str(item["w"]) + "x" + str(item["h"])
            print("    " + item["path"] + "  [" + dim + " " + item["mode"] + " " + str(item["size_kb"]) + "KB]")

    return images


if __name__ == "__main__":
    print("Scanning Unity Art assets at:", UNITY_ART_DIR)
    images = scan_all_images()
    generate_report(images)

    output_path = Path(__file__).resolve().parents[1] / "cache" / "atlas" / "unity_asset_audit.json"
    output_path.parent.mkdir(parents=True, exist_ok=True)
    with open(output_path, "w", encoding="utf-8") as f:
        json.dump(images, f, indent=2, ensure_ascii=False)
    print("\nRaw data saved to: " + str(output_path))
