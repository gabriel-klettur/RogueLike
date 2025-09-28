"""
SpawnerPositionPersistenceSystem: periodically persists the last known tile position of
NPCs spawned by spawners with max_active=1, keyed by spawner instance id.

On load, SpawnerPlacementSystem consumes this file to place NPCs exactly where they were
before closing the game.
"""
from __future__ import annotations

import json
import os
from typing import Dict

from roguelike_engine.config.config_tiles import TILE_SIZE
from roguelike_engine.config.map_config import global_map_settings
from roguelike_game.ecs.utils.position_utils import compute_foot_tile
from roguelike_game.ecs.systems.spawner.placement_utils import collect_blocked_tiles
import roguelike_engine.config.config as config


class SpawnerPositionPersistenceSystem:
    def __init__(self, perf_log=None):
        self.perf_log = perf_log
        self._frame_idx: int = 0
        # Write at most every N frames to reduce IO
        self._write_interval_frames: int = 120  # ~2s at 60 FPS
        self._dirty: bool = False
        self._cache: Dict[str, dict] | None = None

    def _positions_path(self) -> str:
        base = config.DATA_DIR
        return os.path.join(base, "spawners", "spawners_positions.json")

    def _load_cache(self) -> Dict[str, dict]:
        if self._cache is not None:
            return self._cache
        path = self._positions_path()
        try:
            with open(path, "r", encoding="utf-8-sig") as f:
                data = json.load(f)
            self._cache = data if isinstance(data, dict) else {}
        except FileNotFoundError:
            self._cache = {}
        except Exception:
            self._cache = {}
        return self._cache

    def _save_cache(self) -> None:
        if not self._dirty:
            return
        path = self._positions_path()
        os.makedirs(os.path.dirname(path), exist_ok=True)
        data = self._cache or {}
        try:
            with open(path, "w", encoding="utf-8") as f:
                json.dump(data, f, ensure_ascii=False, indent=2)
            self._dirty = False
        except Exception:
            # Never crash the loop on IO errors
            pass

    def flush(self) -> None:
        """Persist immediately any pending changes to disk.
        Safe to call multiple times; it will no-op if there are no changes.
        """
        try:
            self._save_cache()
        except Exception:
            pass

    def update(self, world, camera=None):
        self._frame_idx += 1
        comps = world.components
        self._load_cache()

        # Iterate all spawners
        for spawner_eid in world.get_entities_with('SpawnerConfig', 'SpawnerState'):
            cfg = comps['SpawnerConfig'][spawner_eid]
            st = comps['SpawnerState'][spawner_eid]

            # Only persist when we can uniquely attribute NPCs (max_active == 1)
            try:
                max_active = int((getattr(cfg, 'policy', {}) or {}).get('max_active', 0) or 0)
            except Exception:
                max_active = 0
            if max_active != 1:
                continue

            inst_id = getattr(cfg, 'instance_id', None)
            if not inst_id:
                continue

            # Find the active entity (if any)
            try:
                active_ids = list(getattr(st, 'active_entities', []) or [])
            except Exception:
                active_ids = []
            if not active_ids:
                continue
            npc_eid = active_ids[0]

            # Read its Position (pixels), convert to zone-local tile
            # Compute tile based on the feet position to match spawn calibration
            foot_tile = None
            try:
                foot_tile = compute_foot_tile(world, npc_eid, TILE_SIZE)
            except Exception:
                foot_tile = None
            if not foot_tile:
                # Fallback to top-left tile if sprite missing (should not happen for vendors)
                pos = comps.get('Position', {}).get(npc_eid)
                if pos is None:
                    continue
                try:
                    foot_tile = (int(pos.x) // TILE_SIZE, int(pos.y) // TILE_SIZE)
                except Exception:
                    continue
            gx, gy = int(foot_tile[0]), int(foot_tile[1])
            # If the tile is not walkable or is statically blocked, snap to nearest valid one
            try:
                solid, building = collect_blocked_tiles(world)
            except Exception:
                solid, building = (set(), set())
            mm = getattr(world, 'map_manager', None)
            def _is_ok(tx: int, ty: int) -> bool:
                if (tx, ty) in solid or (tx, ty) in building:
                    return False
                if mm and hasattr(mm, 'is_walkable'):
                    try:
                        if not mm.is_walkable(tx, ty):
                            return False
                    except Exception:
                        pass
                return True
            if not _is_ok(gx, gy):
                # Simple spiral without considering other NPCs (we are persisting our own)
                max_r = 10
                found = None
                for r in range(0, max_r + 1):
                    x0, x1 = gx - r, gx + r
                    y0, y1 = gy - r, gy + r
                    # top & bottom
                    for x in range(x0, x1 + 1):
                        if _is_ok(x, y0):
                            found = (x, y0); break
                        if _is_ok(x, y1):
                            found = (x, y1); break
                    if found:
                        break
                    # sides
                    for y in range(y0 + 1, y1):
                        if _is_ok(x0, y):
                            found = (x0, y); break
                        if _is_ok(x1, y):
                            found = (x1, y); break
                    if found:
                        break
                if found:
                    gx, gy = int(found[0]), int(found[1])
            zone = getattr(cfg, 'zone', 'lobby') or 'lobby'
            off_x, off_y = global_map_settings.zone_offsets.get(str(zone), (0, 0))
            local = [int(gx - off_x), int(gy - off_y)]

            # Update cache
            entry = {
                "zone": str(zone),
                "tile": local,
            }
            cache = self._cache if isinstance(self._cache, dict) else {}
            prev = cache.get(str(inst_id)) if cache else None
            if prev != entry:
                cache[str(inst_id)] = entry
                self._cache = cache
                self._dirty = True

        # Periodically flush
        if self._dirty and (self._frame_idx % self._write_interval_frames == 0):
            self._save_cache()
