import time
import math
import pygame
from roguelike_engine.config.config_tiles import TILE_SIZE
from roguelike_engine.utils.benchmark import benchmark
from roguelike_game.ecs.components.abilities.dash_component import DashComponent
from roguelike_game.ecs.components.transform.position import Position
from roguelike_game.ecs.components.particles.particle_component import ParticleComponent
from roguelike_game.ecs.utils.collider_utils import (
    get_circle_world,
    circle_overlaps_rect,
    circle_rect_mtv,
)
from roguelike_game.ecs.utils.health_utils import is_neutral

class DashSystem:
    """
    ECS system that moves entities with DashComponent during dash duration.
    """
    def __init__(self, perf_log):
        self.perf_log = perf_log
    
    def update(self, world, camera=None):
        now = time.time()
        to_remove = []
        for eid, dash in list(world.components.get('DashComponent', {}).items()):
            delta = now - dash.last_update
            if delta <= 0:
                continue
            pos = world.components.get('Position', {}).get(eid)
            multi = world.components.get('MultiCollider', {}).get(eid)
            move_dist = dash.speed * delta
            if pos and multi:
                feet = getattr(multi, 'colliders', {}).get('feet')
                if feet and hasattr(feet, 'radius'):
                    # Prepare continuous movement: substep along the path to prevent tunneling
                    total_vx = float(dash.dir_x * move_dist)
                    total_vy = float(dash.dir_y * move_dist)
                    cx, cy, r = get_circle_world(pos.x, pos.y, feet)
                    # Helper: collision test at world circle center
                    def collides_at(px: float, py: float) -> bool:
                        ra = pygame.Rect(math.floor(px - r - 1), math.floor(py - r - 1), math.ceil(2 * r) + 2, math.ceil(2 * r) + 2)
                        cand = world.get_solid_tiles_for_rect(ra)
                        for rr in cand:
                            if circle_overlaps_rect(px, py, r, rr):
                                return True
                        return False
                    # If starting inside a collider (edge case), try a small retreat
                    dmx = float(dash.dir_x)
                    dmy = float(dash.dir_y)
                    mag = (dmx * dmx + dmy * dmy) ** 0.5 or 1.0
                    dnx = dmx / mag
                    dny = dmy / mag
                    if collides_at(cx, cy):
                        # 1) Try a small retreat opposite to dash
                        retreat = 6.0
                        rcx = cx - dnx * retreat
                        rcy = cy - dny * retreat
                        if not collides_at(rcx, rcy):
                            pos.x += (rcx - cx)
                            pos.y += (rcy - cy)
                            cx, cy = rcx, rcy
                        # 2) If still colliding, perform MTV-based ejection
                        if collides_at(cx, cy):
                            aabb0 = pygame.Rect(math.floor(cx - r - 1), math.floor(cy - r - 1), math.ceil(2 * r) + 2, math.ceil(2 * r) + 2)
                            tiles0 = world.get_solid_tiles_for_rect(aabb0)
                            ex, ey = 0.0, 0.0
                            for tile in tiles0:
                                if circle_overlaps_rect(cx, cy, r, tile):
                                    mx, my = circle_rect_mtv(cx, cy, r, tile)
                                    ex += mx
                                    ey += my
                            # Add epsilon beyond MTV to avoid tangency registered as overlap
                            mag = (ex * ex + ey * ey) ** 0.5
                            if mag > 1e-6:
                                extra = 1.0
                                sx = ex * ((mag + extra) / mag)
                                sy = ey * ((mag + extra) / mag)
                            else:
                                sx, sy = ex, ey
                            nx = cx + sx
                            ny = cy + sy
                            if collides_at(nx, ny):
                                # Fallback: push opposite dash direction by ~1.6 radii
                                nx = cx - dnx * (r * 1.6)
                                ny = cy - dny * (r * 1.6)
                            pos.x += (nx - cx)
                            pos.y += (ny - cy)
                            cx, cy = nx, ny
                            # Emergency: if still colliding, step back opposite to dash until free
                            if collides_at(cx, cy):
                                max_steps = int(r * 3 + 4)
                                for i in range(max_steps):
                                    tx = cx - dnx * float(i + 1)
                                    ty = cy - dny * float(i + 1)
                                    if not collides_at(tx, ty):
                                        pos.x += (tx - cx)
                                        pos.y += (ty - cy)
                                        cx, cy = tx, ty
                                        break
                        # 3) Cancel dash after resolving overlap to avoid further motion this frame
                        to_remove.append(eid)
                        dash.last_update = now
                        continue
                    # Compute substeps based on tile size and radius
                    step_len = max(1.0, min(float(r) * 0.5, float(TILE_SIZE) * 0.5, 8.0))
                    dist = (total_vx * total_vx + total_vy * total_vy) ** 0.5
                    steps = max(1, int(math.ceil(dist / step_len)))
                    svx = total_vx / steps
                    svy = total_vy / steps
                    collided = False
                    for _ in range(steps):
                        nx = cx + svx
                        ny = cy + svy
                        # Check collision at this substep target
                        aabb = pygame.Rect(math.floor(nx - r - 1), math.floor(ny - r - 1), math.ceil(2 * r) + 2, math.ceil(2 * r) + 2)
                        tiles = world.get_solid_tiles_for_rect(aabb)
                        hit = False
                        for tile in tiles:
                            if circle_overlaps_rect(nx, ny, r, tile):
                                hit = True
                                break
                        if not hit:
                            # Apply substep
                            pos.x += (nx - cx)
                            pos.y += (ny - cy)
                            cx, cy = nx, ny
                            continue
                        # Collision within this substep: binary search to last non-colliding point
                        low, high = 0.0, 1.0
                        best_t = 0.0
                        for _ in range(8):
                            mid = (low + high) * 0.5
                            px = cx + svx * mid
                            py = cy + svy * mid
                            if collides_at(px, py):
                                high = mid
                            else:
                                best_t = mid
                                low = mid
                        out_x = cx + svx * best_t
                        out_y = cy + svy * best_t
                        # Epsilon + knockback backwards along dash direction
                        eps = 0.75
                        kb = float(getattr(dash, 'knockback', 4.0))
                        back = eps + max(0.0, kb)
                        cand_x = out_x - dnx * back
                        cand_y = out_y - dny * back
                        if collides_at(cand_x, cand_y):
                            cand_x = out_x - dnx * eps
                            cand_y = out_y - dny * eps
                            if collides_at(cand_x, cand_y):
                                cand_x, cand_y = out_x, out_y
                        pos.x += (cand_x - cx)
                        pos.y += (cand_y - cy)
                        # On dash impact, apply configurable collision damage to the dasher (Player or hostile)
                        try:
                            is_player = eid in world.components.get('PlayerTagComponent', {})
                            dmg = int(getattr(dash, 'collision_damage', 2.0))
                            health = world.components.get('Health', {}).get(eid)
                            if health is not None and dmg > 0:
                                # Skip collision damage to neutral entities
                                if is_neutral(world, eid):
                                    pass
                                elif is_player:
                                    godmode = bool(getattr(getattr(world, 'state', None), 'godmode', False))
                                    if not godmode:
                                        health.current_hp = max(0, int(health.current_hp) - dmg)
                                else:
                                    # Hostiles and other entities always take collision damage
                                    health.current_hp = max(0, int(health.current_hp) - dmg)
                                # Emit OnHit/OnDeath to drive animations/state (for both)
                                qmap = world.components.setdefault('FSMEventQueue', {})
                                q = qmap.setdefault(eid, [])
                                from_left = bool(dash.dir_x < 0)
                                q.append({"type": "OnHit", "from_left": from_left})
                                if health.current_hp <= 0:
                                    pass
                                # Break combo on self-damage only for player
                                if is_player:
                                    combo_q = world.components.setdefault('ComboEventQueue', [])
                                    combo_q.append({'type': 'break', 'entity': int(eid)})
                        except Exception:
                            # Never fail dash resolution due to damage/event side-effects
                            pass
                        collided = True
                        break
                    if collided:
                        to_remove.append(eid)
                    # else: finished all substeps without collision
                else:
                    # Fallback: simple translation if feet collider not found
                    pos.x += dash.dir_x * move_dist
                    pos.y += dash.dir_y * move_dist

            dash.last_update = now
            if now >= dash.start_time + dash.duration:
                to_remove.append(eid)
        for eid in to_remove:
            world.components['DashComponent'].pop(eid, None)