from __future__ import annotations

import json
import os
from typing import Any, Dict, List

# Use engine config paths
try:
    import roguelike_engine.config.config as cfg
except Exception:
    cfg = None  # type: ignore


def _read_json(path: str) -> Any:
    try:
        with open(path, "r", encoding="utf-8-sig") as f:
            return json.load(f)
    except FileNotFoundError:
        return None


def main() -> int:
    base = getattr(cfg, "DATA_DIR", os.path.join(os.path.dirname(__file__), os.pardir, "data"))
    sp_path = os.path.join(base, "spawners", "spawners_instances.json")
    bi_path = os.path.join(base, "buildings", "buildings_instances.json")

    sp: List[Dict] = _read_json(sp_path) or []
    bi: List[Dict] = _read_json(bi_path) or []

    if not isinstance(sp, list) or not isinstance(bi, list):
        print("[sanity] Missing or invalid JSON files.")
        return 2

    by_id: Dict[int, Dict] = {}
    for e in bi:
        try:
            by_id[int(e.get("id"))] = e
        except Exception:
            continue

    errors: List[str] = []
    checked = 0

    for inst in sp:
        try:
            sid = inst.get("id")
            vis = inst.get("visuals") if isinstance(inst, dict) else None
            if not isinstance(vis, dict):
                continue
            for k, v in vis.items():
                try:
                    if isinstance(v, dict):
                        iid = int(v.get("instance_id") or v.get("id") or v.get("building_instance_id"))
                        sc = v.get("scale") if isinstance(v.get("scale"), (list, tuple)) else None
                    else:
                        iid = int(v)
                        sc = None
                except Exception:
                    errors.append(f"invalid visual ref for spawner {sid} key {k}: {v}")
                    continue
                b = by_id.get(int(iid))
                if not b:
                    errors.append(f"missing building_instance id={iid} for spawner {sid} key {k}")
                    continue
                # Flags present
                if not bool(b.get("spawner_visual", False)):
                    errors.append(f"building {iid} missing spawner_visual root flag")
                ov = b.get("overrides") or {}
                if not bool((ov or {}).get("_is_spawner_visual", False)):
                    errors.append(f"building {iid} missing overrides._is_spawner_visual flag")
                # Spawn linkage matches
                ss = str(sid) if sid is not None else None
                if ss:
                    if str(b.get("spawn_id") or "") != ss:
                        errors.append(f"building {iid} spawn_id mismatch: {b.get('spawn_id')} != {ss}")
                    if str(b.get("spawner_instance_id") or "") != ss:
                        errors.append(f"building {iid} spawner_instance_id mismatch: {b.get('spawner_instance_id')} != {ss}")
                    if str((ov or {}).get("spawner_instance_id") or "") != ss:
                        errors.append(f"building {iid} overrides.spawner_instance_id mismatch: {(ov or {}).get('spawner_instance_id')} != {ss}")
                # Scale if provided in visuals
                if sc is not None:
                    try:
                        cur_sc = ov.get("scale")
                        cur_sc_t = (int(cur_sc[0]), int(cur_sc[1])) if isinstance(cur_sc, (list, tuple)) and len(cur_sc) == 2 else None
                    except Exception:
                        cur_sc_t = None
                    if cur_sc_t != tuple(sc):
                        errors.append(f"building {iid} scale mismatch: {cur_sc_t} != {tuple(sc)}")
                checked += 1
        except Exception:
            continue

    if errors:
        print("[sanity] FAIL")
        for e in errors[:100]:
            print(" -", e)
        print(f"[sanity] total_errors={len(errors)} checked={checked}")
        return 1
    print(f"[sanity] OK checked={checked}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
