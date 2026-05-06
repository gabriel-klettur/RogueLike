#!/usr/bin/env python3
"""
Tile size audit + auto-fix tool for the Valkur Unity project.

Scans every PNG under ``unity/Valkur/Assets/_Project/Resources/Tiles`` and
verifies that each sprite's pixel dimensions are compatible with the canonical
tile size (32x32 px @ PPU=32 = 1 world unit = 1 cell).

Default: produce a report (``--audit``).
With ``--fix``: downscales oversized tiles to TARGET_SIZE using NEAREST
resampling (preserves pixel-art crispness) and restores PPU=32 in the .meta.

Run from repo root:
    python tools/atlas/audit_tile_sizes.py --audit
    python tools/atlas/audit_tile_sizes.py --fix
"""

from __future__ import annotations

import argparse
import json
import re
import sys
from dataclasses import dataclass, asdict
from pathlib import Path
from typing import Iterable

from PIL import Image

REPO_ROOT = Path(__file__).resolve().parents[2]
TILES_DIR = REPO_ROOT / "unity" / "Valkur" / "Assets" / "_Project" / "Resources" / "Tiles"
TARGET_SIZE = 32          # canonical tile size in pixels
TARGET_PPU  = 32          # canonical pixels-per-unit
WARN_SIZE   = 64          # tiles larger than this are reported
EXCLUDE_DIRS = ("_backups", "_raw", "_source")  # backup / source folders are exempt
PPU_REGEX   = re.compile(r"^(\s*spritePixelsToUnits:\s*)(\d+)\s*$", re.MULTILINE)


@dataclass
class TileEntry:
    rel_path: str
    width: int
    height: int
    current_ppu: int
    status: str   # "ok" | "oversized" | "non_square" | "missing_meta"
    action: str   # "" | "downscale" | "restore_ppu" | "manual_review"

    def as_dict(self) -> dict:
        return asdict(self)


def read_meta_ppu(meta_path: Path) -> int | None:
    if not meta_path.exists():
        return None
    text = meta_path.read_text(encoding="utf-8", errors="replace")
    m = PPU_REGEX.search(text)
    return int(m.group(2)) if m else None


def write_meta_ppu(meta_path: Path, new_ppu: int) -> bool:
    text = meta_path.read_text(encoding="utf-8", errors="replace")
    new_text, count = PPU_REGEX.subn(rf"\g<1>{new_ppu}", text, count=1)
    if count == 0 or new_text == text:
        return False
    meta_path.write_text(new_text, encoding="utf-8", newline="\n")
    return True


def classify(width: int, height: int, current_ppu: int | None) -> tuple[str, str]:
    if current_ppu is None:
        return "missing_meta", "manual_review"
    if width != height:
        return "non_square", "manual_review"
    if width > WARN_SIZE:
        return "oversized", "downscale"
    if current_ppu != TARGET_PPU:
        # not oversized but PPU drifted from canonical
        return "ok", "restore_ppu"
    return "ok", ""


def iter_tiles() -> Iterable[Path]:
    if not TILES_DIR.exists():
        print(f"[ERROR] Tiles dir not found: {TILES_DIR}", file=sys.stderr)
        sys.exit(2)
    for p in sorted(TILES_DIR.rglob("*.png")):
        # Skip files inside backup / source folders
        if any(part in EXCLUDE_DIRS for part in p.parts):
            continue
        yield p


def audit() -> list[TileEntry]:
    entries: list[TileEntry] = []
    for png in iter_tiles():
        meta = png.with_suffix(png.suffix + ".meta")
        try:
            with Image.open(png) as im:
                w, h = im.size
        except Exception as e:
            print(f"[WARN] Cannot open {png.name}: {e}", file=sys.stderr)
            continue
        ppu = read_meta_ppu(meta)
        status, action = classify(w, h, ppu)
        entries.append(TileEntry(
            rel_path=str(png.relative_to(REPO_ROOT)).replace("\\", "/"),
            width=w, height=h,
            current_ppu=ppu if ppu is not None else -1,
            status=status, action=action,
        ))
    return entries


def downscale(png_path: Path, target: int) -> tuple[int, int]:
    """Downscale a square PNG to ``target``x``target`` using NEAREST resampling.

    Preserves transparency; loses no pixel-art crispness when the source is a
    clean integer multiple of the target (e.g. 1024 → 32 = 32x downscale).
    """
    with Image.open(png_path) as im:
        original = im.size
        # Convert to RGBA to ensure alpha survives
        if im.mode != "RGBA":
            im = im.convert("RGBA")
        resized = im.resize((target, target), Image.Resampling.NEAREST)
        resized.save(png_path, format="PNG", optimize=True)
    return original


def fix(entries: list[TileEntry]) -> dict:
    summary = {"downscaled": [], "ppu_restored": [], "skipped": []}
    for e in entries:
        path = REPO_ROOT / e.rel_path
        meta = path.with_suffix(path.suffix + ".meta")

        if e.action == "downscale":
            try:
                original = downscale(path, TARGET_SIZE)
                changed_meta = write_meta_ppu(meta, TARGET_PPU) if meta.exists() else False
                summary["downscaled"].append({
                    "path": e.rel_path,
                    "from": f"{original[0]}x{original[1]}",
                    "to":   f"{TARGET_SIZE}x{TARGET_SIZE}",
                    "ppu_restored": changed_meta,
                })
            except Exception as ex:
                summary["skipped"].append({"path": e.rel_path, "reason": str(ex)})

        elif e.action == "restore_ppu":
            if write_meta_ppu(meta, TARGET_PPU):
                summary["ppu_restored"].append({"path": e.rel_path,
                                                "old_ppu": e.current_ppu,
                                                "new_ppu": TARGET_PPU})

        elif e.action == "manual_review":
            summary["skipped"].append({"path": e.rel_path,
                                       "reason": f"{e.status} ({e.width}x{e.height}, ppu={e.current_ppu})"})
    return summary


def print_report(entries: list[TileEntry]) -> None:
    by_status: dict[str, list[TileEntry]] = {}
    for e in entries:
        by_status.setdefault(e.status, []).append(e)

    total = len(entries)
    print(f"\n=== Valkur Tile Audit ({total} PNGs scanned) ===")
    for status in ("oversized", "non_square", "missing_meta", "ok"):
        bucket = by_status.get(status, [])
        if not bucket:
            continue
        print(f"\n[{status.upper()}] ({len(bucket)})")
        for e in bucket if status != "ok" else []:
            print(f"  {e.width:>4}x{e.height:<4}  PPU={e.current_ppu:<5}  {e.rel_path}  -> {e.action or 'noop'}")
        if status == "ok":
            wrong_ppu = [e for e in bucket if e.action == "restore_ppu"]
            if wrong_ppu:
                print(f"  ({len(wrong_ppu)} have wrong PPU and will be restored to {TARGET_PPU})")
                for e in wrong_ppu:
                    print(f"    PPU={e.current_ppu:<5} {e.rel_path}")


def main() -> int:
    parser = argparse.ArgumentParser(description="Valkur tile size auditor / fixer")
    parser.add_argument("--fix", action="store_true",
                        help="Apply fixes (downscale + restore PPU). Default: audit-only.")
    parser.add_argument("--json", type=Path, default=None,
                        help="Write JSON report to this path.")
    args = parser.parse_args()

    entries = audit()
    print_report(entries)

    summary: dict | None = None
    if args.fix:
        summary = fix(entries)
        print("\n=== FIX SUMMARY ===")
        print(f"  Downscaled:   {len(summary['downscaled'])}")
        print(f"  PPU restored: {len(summary['ppu_restored'])}")
        print(f"  Skipped:      {len(summary['skipped'])}")
        for s in summary["downscaled"]:
            print(f"    [DOWN] {s['path']}  {s['from']} -> {s['to']}  ppu_restored={s['ppu_restored']}")
        for s in summary["ppu_restored"]:
            print(f"    [PPU ] {s['path']}  {s['old_ppu']} -> {s['new_ppu']}")
        for s in summary["skipped"]:
            print(f"    [SKIP] {s['path']}  ({s['reason']})")

    if args.json:
        payload = {
            "tiles_dir": str(TILES_DIR.relative_to(REPO_ROOT)).replace("\\", "/"),
            "target_size": TARGET_SIZE,
            "target_ppu":  TARGET_PPU,
            "entries":     [e.as_dict() for e in entries],
        }
        if summary is not None:
            payload["fix_summary"] = summary
        args.json.write_text(json.dumps(payload, indent=2), encoding="utf-8")
        print(f"\nReport written to {args.json}")

    issues = sum(1 for e in entries if e.action in ("downscale", "manual_review"))
    return 0 if (args.fix or issues == 0) else 1


if __name__ == "__main__":
    sys.exit(main())
