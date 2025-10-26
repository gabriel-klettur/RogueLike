from __future__ import annotations

"""
Audit and reconcile item icon assets with the SQLite items table.

Features (dry-run by default):
- Check that all image basenames in assets/items are unique (case-insensitive).
- Verify that every icon path referenced by the DB exists on disk.
- Detect unregistered images (present in assets/items but not referenced by DB).
- Optional --apply: insert stub items for unregistered images with icon_small set.

Usage:
    python -m scripts.database.audit_items_assets [--apply] [--assets-dir assets/items] [--db data/roguelike.sqlite3]

Exit codes:
- 0: No errors; duplicates=0; missing_paths=0
- 2: Duplicated basenames detected
- 3: DB references missing images
- 4: Both duplicates and missing references detected

Notes:
- Paths are compared as POSIX-like relative paths (e.g., 'assets/items/Alchemy/health_potion.png').
- Basename uniqueness is checked case-insensitively to avoid cross-platform issues.
"""

import argparse
import json
import re
import sqlite3
from dataclasses import dataclass
from pathlib import Path
from typing import Dict, Iterable, List, Set, Tuple

ASSETS_DEFAULT = Path("assets/items")
DB_DEFAULT = Path("data/roguelike.sqlite3")

IMG_EXTS = {".png", ".jpg", ".jpeg", ".webp", ".bmp", ".gif"}


@dataclass(frozen=True)
class DbRef:
    item_id: str
    path: str  # normalized posix relative path
    column: str  # icon_small|icon_large|icon_json[i]


@dataclass
class AuditReport:
    duplicates: Dict[str, List[str]]  # basename_lower -> [relpaths]
    missing_paths: List[DbRef]
    unregistered_images: List[str]  # relpaths in assets not referenced by DB

    def has_issues(self) -> bool:
        return bool(self.duplicates or self.missing_paths)


def _norm_relpath(p: Path, root: Path) -> str:
    try:
        rel = p.relative_to(root.parent if root.name == "items" else Path("."))
    except Exception:
        rel = p
    # Always return posix with forward slashes
    return str(rel.as_posix())


def scan_assets_items(root: Path) -> Tuple[Dict[str, List[str]], Set[str]]:
    """Scan assets/items and return:
    - by_basename: basename_lower -> [relpaths]
    - all_relpaths: set of relpaths
    """
    root = root.resolve()
    if not root.exists():
        return {}, set()
    by_basename: Dict[str, List[str]] = {}
    all_relpaths: Set[str] = set()
    for p in root.rglob("*"):
        if not p.is_file():
            continue
        if p.suffix.lower() not in IMG_EXTS:
            continue
        # Build normalized relpath from repo root (assets/...)
        rel = p.relative_to(Path.cwd()).as_posix() if p.is_relative_to(Path.cwd()) else _norm_relpath(p, root)
        if not rel.startswith("assets/"):
            # Force assets/ prefix if path is within assets
            try:
                idx = rel.lower().rfind("assets/")
                if idx >= 0:
                    rel = rel[idx:]
            except Exception:
                pass
        all_relpaths.add(rel)
        key = p.name.lower()
        by_basename.setdefault(key, []).append(rel)
    return by_basename, all_relpaths


def _iter_db_icon_refs(con: sqlite3.Connection) -> Iterable[DbRef]:
    cur = con.cursor()
    rows = cur.execute("SELECT id, icon_small, icon_large, icon_json FROM items").fetchall()
    for (iid, icon_small, icon_large, icon_json) in rows:
        if icon_small:
            yield DbRef(str(iid), str(icon_small), "icon_small")
        if icon_large:
            yield DbRef(str(iid), str(icon_large), "icon_large")
        if icon_json:
            try:
                lst = json.loads(icon_json)
                if isinstance(lst, list):
                    for i, val in enumerate(lst):
                        if isinstance(val, str) and val:
                            yield DbRef(str(iid), val, f"icon_json[{i}]")
            except Exception:
                # ignore invalid JSON
                pass


def _normalize_db_path(path: str) -> str:
    # Normalize slashes and strip leading './'
    p = path.replace("\\", "/").lstrip("./")
    # Ensure paths are relative from repo root (contain 'assets/')
    if "/assets/" in "/" + p:
        p = p[p.index("assets/") :]
    return p


def load_db_icon_paths(db_path: Path) -> Tuple[List[DbRef], Set[str]]:
    con = sqlite3.connect(db_path)
    try:
        refs: List[DbRef] = [DbRef(r.item_id, _normalize_db_path(r.path), r.column) for r in _iter_db_icon_refs(con)]
    finally:
        con.close()
    paths: Set[str] = {r.path for r in refs}
    return refs, paths


def audit(assets_dir: Path, db_path: Path) -> AuditReport:
    by_base, assets_paths = scan_assets_items(assets_dir)
    refs, db_paths = load_db_icon_paths(db_path)

    # Duplicates: basenames with >1 distinct relpaths
    duplicates = {k: sorted(v) for k, v in by_base.items() if len(set(v)) > 1}

    # Missing: DB paths that do not exist on disk
    missing: List[DbRef] = []
    for r in refs:
        if r.path not in assets_paths:
            # Attempt relaxed check by basename match inside assets
            # but still record as missing for strictness
            missing.append(r)

    # Unregistered: assets not referenced anywhere by DB
    unregistered = sorted(p for p in assets_paths if p in assets_paths.difference(db_paths))

    return AuditReport(duplicates=duplicates, missing_paths=missing, unregistered_images=unregistered)


def _to_item_id(basename: str, existing_ids: Set[str]) -> str:
    name = basename
    if "." in name:
        name = name[: name.rfind(".")]
    # slugify: keep letters, digits and underscore
    slug = re.sub(r"[^a-zA-Z0-9_]+", "_", name).strip("_").lower()
    if not slug:
        slug = "asset_item"
    base = slug
    i = 1
    while slug in existing_ids:
        slug = f"{base}_{i}"
        i += 1
    return slug


def apply_fixes_insert_stubs(db_path: Path, assets: Iterable[str]) -> Tuple[int, List[str]]:
    """Insert stub items for the given asset relpaths if id does not exist.
    Returns (inserted_count, inserted_ids)
    """
    con = sqlite3.connect(db_path)
    try:
        cur = con.cursor()
        # Load existing ids
        existing_ids = {row[0] for row in cur.execute("SELECT id FROM items").fetchall()}
        inserted = 0
        inserted_ids: List[str] = []
        for rel in assets:
            basename = Path(rel).name
            item_id = _to_item_id(basename, existing_ids)
            if item_id in existing_ids:
                # Already present (path may be different); skip insert
                continue
            cur.execute(
                """
                INSERT INTO items (
                    id, name, description, stackable, max_stack, z_layer, despawn_time,
                    equip_slot, rarity, level_requirement,
                    icon_small, icon_large, icon_json,
                    threshold, experience, effect, durability, damage, attack_speed, range,
                    crit_chance, crit_multiplier, weight, value, quest_id,
                    scale_editor, scale_map, scale_inventory
                ) VALUES (
                    ?, ?, ?, NULL, NULL, NULL, NULL,
                    NULL, NULL, NULL,
                    ?, NULL, NULL,
                    NULL, NULL, NULL, NULL, NULL, NULL, NULL,
                    NULL, NULL, NULL, NULL, NULL,
                    NULL, NULL, NULL
                )
                """,
                (
                    item_id,
                    basename,  # name
                    None,  # description
                    rel,   # icon_small
                ),
            )
            existing_ids.add(item_id)
            inserted += 1
            inserted_ids.append(item_id)
        con.commit()
        return inserted, inserted_ids
    finally:
        con.close()


def main() -> None:
    ap = argparse.ArgumentParser(description="Audit and reconcile item assets with DB")
    ap.add_argument("--assets-dir", default=str(ASSETS_DEFAULT), help="Root directory for item assets (default: assets/items)")
    ap.add_argument("--db", default=str(DB_DEFAULT), help="SQLite database path (default: data/roguelike.sqlite3)")
    ap.add_argument("--apply", action="store_true", help="Apply fixes: insert stub items for unregistered images")
    args = ap.parse_args()

    assets_dir = Path(args.assets_dir)
    db_path = Path(args.db)

    report = audit(assets_dir, db_path)

    # Print summary
    print("=== Audit Report ===")
    print(f"Assets dir: {assets_dir}")
    print(f"DB path   : {db_path}")
    print(f"Duplicate basenames: {len(report.duplicates)}")
    if report.duplicates:
        for base, paths in sorted(report.duplicates.items()):
            print(f"  {base} ->")
            for p in paths:
                print(f"    - {p}")
    print(f"Missing DB icon paths: {len(report.missing_paths)}")
    if report.missing_paths:
        for r in report.missing_paths[:50]:
            print(f"  [{r.item_id}] {r.column}: {r.path}")
        if len(report.missing_paths) > 50:
            print(f"  ... and {len(report.missing_paths) - 50} more")
    print(f"Unregistered images: {len(report.unregistered_images)}")
    if report.unregistered_images:
        for p in report.unregistered_images[:50]:
            print(f"  - {p}")
        if len(report.unregistered_images) > 50:
            print(f"  ... and {len(report.unregistered_images) - 50} more")

    exit_code = 0
    if report.duplicates and report.missing_paths:
        exit_code = 4
    elif report.duplicates:
        exit_code = 2
    elif report.missing_paths:
        exit_code = 3

    if args.apply:
        if report.duplicates:
            print("[WARN] --apply will not resolve duplicate basenames. Resolve duplicates first.")
        to_insert = report.unregistered_images
        if to_insert:
            inserted, ids = apply_fixes_insert_stubs(db_path, to_insert)
            print(f"Applied: inserted {inserted} stub items")
            if ids:
                for iid in ids[:50]:
                    print(f"  + {iid}")
                if len(ids) > 50:
                    print(f"  ... and {len(ids) - 50} more")
        else:
            print("No unregistered images to insert.")

    # Keep non-zero exit to integrate with CI checks
    raise SystemExit(exit_code)


if __name__ == "__main__":
    main()
