from __future__ import annotations

import os
import sys
import json
import shutil
import tempfile
from typing import Dict, List


def _load(p: str):
    with open(p, "r", encoding="utf-8-sig") as f:
        return json.load(f)


def _dump(p: str, data):
    with open(p, "w", encoding="utf-8") as f:
        json.dump(data, f, ensure_ascii=False, indent=4)


def main() -> int:
    # Arrange: copy data dir into a temp sandbox
    repo_root = os.path.abspath(os.path.join(os.path.dirname(__file__), os.pardir))
    data_src = os.path.join(repo_root, "data")
    if not os.path.isdir(data_src):
        print("[test_preflight_persistence] data/ folder not found")
        return 2
    tmpdir = tempfile.mkdtemp(prefix="rl_preflight_")
    data_dst = os.path.join(tmpdir, "data")
    shutil.copytree(data_src, data_dst)

    # Make engine see the sandboxed data dir
    sys.path.insert(0, os.path.join(repo_root, "src"))
    import roguelike_engine.config.config as cfg
    old_data_dir = getattr(cfg, "DATA_DIR", None)
    cfg.DATA_DIR = data_dst

    try:
        # Corrupt: remove any referenced building instances to force creation
        sp_path = os.path.join(data_dst, "spawners", "spawners_instances.json")
        bi_path = os.path.join(data_dst, "buildings", "buildings_instances.json")
        sp = _load(sp_path)
        bi = _load(bi_path)
        if not isinstance(sp, list) or not isinstance(bi, list):
            print("[test_preflight_persistence] invalid JSON contents")
            return 2
        ref_ids = set()
        for inst in sp:
            vis = inst.get("visuals") if isinstance(inst, dict) else None
            if isinstance(vis, dict):
                for v in vis.values():
                    try:
                        if isinstance(v, dict):
                            rid = int(v.get("instance_id") or v.get("id") or v.get("building_instance_id"))
                        else:
                            rid = int(v)
                        ref_ids.add(rid)
                    except Exception:
                        continue
        if ref_ids:
            bi2 = []
            for e in bi:
                try:
                    if int(e.get("id")) in ref_ids:
                        # drop referenced ones to force preflight recreation
                        continue
                except Exception:
                    pass
                bi2.append(e)
            _dump(bi_path, bi2)

        # Act: run preflight
        from roguelike_game.ecs.systems.spawner.placement.visuals import preflight_validate_spawner_visuals
        updated = int(preflight_validate_spawner_visuals() or 0)
        # Assert: now, every visuals[*].instance_id exists in buildings_instances
        bi_post = _load(bi_path)
        by_id = {}
        for e in bi_post:
            try:
                by_id[int(e.get("id"))] = e
            except Exception:
                continue
        sp_post = _load(sp_path)
        missing = []
        flag_errors = []
        for inst in sp_post:
            vis = inst.get("visuals") if isinstance(inst, dict) else None
            if not isinstance(vis, dict):
                continue
            for k, v in vis.items():
                try:
                    iid = int(v.get("instance_id") if isinstance(v, dict) else v)
                except Exception:
                    continue
                be = by_id.get(iid)
                if not be:
                    missing.append((inst.get("id"), k, iid))
                    continue
                # schema flags
                if not bool(be.get("spawner_visual", False)):
                    flag_errors.append((iid, "spawner_visual"))
                ov = be.get("overrides") or {}
                if not bool((ov or {}).get("_is_spawner_visual", False)):
                    flag_errors.append((iid, "overrides._is_spawner_visual"))
        assert not missing, f"Missing building instances for visuals after preflight: {missing}"
        assert not flag_errors, f"Missing flags on building instances: {flag_errors}"
        print(f"[test_preflight_persistence] OK (updated={updated}, refs={len(ref_ids)})")
        return 0
    finally:
        cfg.DATA_DIR = old_data_dir
        shutil.rmtree(tmpdir, ignore_errors=True)


if __name__ == "__main__":
    raise SystemExit(main())
