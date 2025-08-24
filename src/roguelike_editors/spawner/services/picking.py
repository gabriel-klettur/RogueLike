from __future__ import annotations

from typing import Optional
from roguelike_engine.config.config_tiles import TILE_SIZE


def pick_spawner_under_cursor(world, camera, mx: int, my: int, *, tile_size: int = TILE_SIZE, hit_radius: int = 12) -> Optional[int]:
    """Return spawner entity id under the cursor or None.

    - Computes screen position of each spawner's anchor tile via camera.apply
    - Uses a screen-space circular hit test with radius=hit_radius
    """
    comps = getattr(world, 'components', {})
    if 'SpawnerConfig' not in comps:
        return None
    best_eid: Optional[int] = None
    best_d2 = 1 << 30
    for eid in world.get_entities_with('SpawnerConfig'):
        cfg = comps['SpawnerConfig'][eid]
        tx, ty = cfg.anchor_tile
        px = tx * tile_size + tile_size // 2
        py = ty * tile_size + tile_size // 2
        sx, sy = camera.apply((px, py))
        dx, dy = mx - sx, my - sy
        d2 = dx * dx + dy * dy
        if d2 <= hit_radius * hit_radius and d2 < best_d2:
            best_d2 = d2
            best_eid = eid
    return best_eid
