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
                        retreat = 6.0
                        rcx = cx - dnx * retreat
                        rcy = cy - dny * retreat
                        if not collides_at(rcx, rcy):
                            pos.x += (rcx - cx)
                            pos.y += (rcy - cy)
                            cx, cy = rcx, rcy
                        else:
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