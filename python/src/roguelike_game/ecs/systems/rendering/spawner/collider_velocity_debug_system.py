from __future__ import annotations

import math
import pygame
import roguelike_engine.config.config as config
from roguelike_game.ecs.utils.debug_draw import (
    PINK_OUTLINE, PINK_FILL, BLUE_DEBUG, BLUE_FAINT, RED_BLOCKED,
    auto_bottom_band_metrics,
)
from roguelike_game.ecs.utils.collider_utils import build_collider_rect, get_circle_world


class ColliderAndVelocityDebugSystem:
    """Draw NPC 'feet' colliders and velocity direction. Visualizes bottom-band centroid alignment.
    Gated by Spawner Editor active or DEBUG_SPAWNER (as per user preference).
    """

    def __init__(self, perf_log=None):
        self.perf_log = perf_log
        # Fallback per-entity caches if MovementDebug component is not present
        self._last_pos: dict[int, tuple[float, float]] = {}
        self._last_dir: dict[int, tuple[float, float]] = {}
        self._stuck_frames: dict[int, int] = {}

    def _get_debug_state(self, world, eid):
        comps = world.components
        dbg_map = comps.get('MovementDebug')
        if dbg_map is None:
            # Fallback to internal caches
            return None
        return dbg_map.get(eid)

    def _set_debug_state(self, world, eid, last_pos, last_dir, stuck_frames):
        comps = world.components
        dbg_map = comps.get('MovementDebug')
        if dbg_map is None:
            # Update fallback caches
            if last_pos is not None:
                self._last_pos[eid] = last_pos
            if last_dir is not None:
                self._last_dir[eid] = last_dir
            if stuck_frames is not None:
                self._stuck_frames[eid] = stuck_frames
            return
        # Update component map if present
        try:
            dbg = dbg_map.get(eid)
            if dbg is None:
                # Lazy create dataclass instance if ECS allows dynamic assignment
                from roguelike_game.ecs.components.debug.movement_debug import MovementDebug
                dbg = MovementDebug()
                dbg_map[eid] = dbg
            if last_pos is not None:
                dbg.last_pos = last_pos
            if last_dir is not None:
                dbg.last_dir = last_dir
            if stuck_frames is not None:
                dbg.stuck_frames = stuck_frames
        except Exception:
            # Never break debug rendering
            pass

    def _read_debug_state(self, world, eid):
        dbg = self._get_debug_state(world, eid)
        if dbg is not None:
            return dbg.last_pos, dbg.last_dir, dbg.stuck_frames
        # Fallback
        return self._last_pos.get(eid), self._last_dir.get(eid), self._stuck_frames.get(eid, 0)

    def update(self, world, screen, camera):
        # Gate
        editor_active = False
        try:
            editor_active = bool(getattr(getattr(world, 'state', None), 'spawner_editor_active', False))
        except Exception:
            editor_active = False
        if not editor_active and not getattr(config, 'DEBUG_SPAWNER', False):
            return

        comps = world.components
        pos_map = comps.get('Position', {})
        multi_map = comps.get('MultiCollider', {})
        death_map = comps.get('DeathTimer', {})
        player_map = comps.get('PlayerTagComponent', {})
        vel_map = comps.get('Velocity', {})

        for nid in world.get_entities_with('Position', 'MultiCollider'):
            if nid in death_map:
                continue
            if nid in player_map:
                continue
            multi = multi_map.get(nid)
            if not multi:
                continue
            feet = multi.colliders.get('feet')
            if not feet:
                continue
            pos = pos_map.get(nid)
            if not pos:
                continue
            zoom = getattr(camera, 'zoom', 1.0) or 1.0
            if hasattr(feet, "radius"):
                # Draw true circle overlay and outline
                cx_w, cy_w, r_w = get_circle_world(pos.x, pos.y, feet)
                cx_s, cy_s = camera.apply((cx_w, cy_w))
                sr = max(1, int(r_w * zoom))
                size = sr * 2
                overlay = pygame.Surface((size, size), pygame.SRCALPHA)
                pygame.draw.circle(overlay, PINK_FILL, (sr, sr), sr, width=0)
                pygame.draw.circle(overlay, PINK_OUTLINE, (sr, sr), sr, width=2)
                screen.blit(overlay, (int(cx_s) - sr, int(cy_s) - sr))

                # Visualize the exact alignment counterpart: bottom-band center from body mask
                body = multi.colliders.get('body')
                if body is not None and getattr(body, 'mask', None) is not None:
                    auto_cx, _ = auto_bottom_band_metrics(body.mask)
                    try:
                        bw, bh = body.mask.get_size()
                    except Exception:
                        bw, bh = 0, 0
                    # Expected anchor position (visual-only; do NOT mutate feet offsets here)
                    expected_x = getattr(body, 'offset_x', 0) + auto_cx
                    anchor_wx = pos.x + expected_x
                    anchor_wy = pos.y + getattr(body, 'offset_y', 0) + max(0, bh - 1)
                    ax, ay = camera.apply((anchor_wx, anchor_wy))
                    ax = int(ax); ay = int(ay)
                    cross = 5
                    pygame.draw.line(screen, BLUE_DEBUG, (ax - cross, ay), (ax + cross, ay), 2)
                    pygame.draw.line(screen, BLUE_DEBUG, (ax, ay - cross), (ax, ay + cross), 2)
                    pygame.draw.line(screen, BLUE_DEBUG, (ax, ay), (int(cx_s), int(cy_s)), 1)

                # Direction indicator using circle center and radius
                vel = vel_map.get(nid)
                last_pos, last_dir, stuck_frames = self._read_debug_state(world, nid)
                if vel is not None:
                    cx = int(cx_s)
                    cy = int(cy_s)
                    r = max(3, sr - 3)
                    if r > 2:
                        pygame.draw.circle(screen, BLUE_DEBUG, (cx, cy), r, width=2)
                    vx = getattr(vel, 'vx', 0.0) or 0.0
                    vy = getattr(vel, 'vy', 0.0) or 0.0
                    mag = math.hypot(vx, vy)
                    disp = 0.0
                    if last_pos is not None:
                        disp = math.hypot((pos.x - last_pos[0]), (pos.y - last_pos[1]))
                    vel_eps = 0.01
                    move_eps = 0.25
                    dir_vec = None
                    if mag > vel_eps:
                        dir_vec = (vx / mag, vy / mag)
                        last_dir = dir_vec
                        if disp <= move_eps and last_pos is not None:
                            stuck_frames = (stuck_frames or 0) + 1
                        else:
                            stuck_frames = 0
                    else:
                        dir_vec = last_dir
                        if (stuck_frames or 0) > 0:
                            stuck_frames = max(0, (stuck_frames or 0) - 1)
                    blocked = (stuck_frames or 0) >= 5
                    color = RED_BLOCKED if blocked else (BLUE_DEBUG if mag > vel_eps else BLUE_FAINT)
                    if dir_vec and r > 2:
                        dx = int(dir_vec[0] * r)
                        dy = int(dir_vec[1] * r)
                        pygame.draw.line(screen, color, (cx, cy), (cx + dx, cy + dy), 2)
                    else:
                        pygame.draw.circle(screen, color, (cx, cy), 2)
                    # Save state
                    self._set_debug_state(world, nid, (pos.x, pos.y), last_dir, stuck_frames)
            else:
                # Fallback: draw rect AABB as before
                rect_world = build_collider_rect(pos.x, pos.y, feet)
                tlx, tly = camera.apply((rect_world.x, rect_world.y))
                brx, bry = camera.apply((rect_world.x + rect_world.w, rect_world.y + rect_world.h))
                sx = int(tlx)
                sy = int(tly)
                sw = max(1, int(brx - tlx))
                sh = max(1, int(bry - tly))
                overlay = pygame.Surface((sw, sh), pygame.SRCALPHA)
                overlay.fill(PINK_FILL)
                screen.blit(overlay, (sx, sy))
                pygame.draw.rect(screen, PINK_OUTLINE, pygame.Rect(sx, sy, sw, sh), width=2)

                vel = vel_map.get(nid)
                last_pos, last_dir, stuck_frames = self._read_debug_state(world, nid)
                if vel is not None:
                    cx = sx + sw // 2
                    cy = sy + sh // 2
                    r = max(3, min(sw, sh) // 2 - 3)
                    if r > 2:
                        pygame.draw.circle(screen, BLUE_DEBUG, (cx, cy), r, width=2)
                    vx = getattr(vel, 'vx', 0.0) or 0.0
                    vy = getattr(vel, 'vy', 0.0) or 0.0
                    mag = math.hypot(vx, vy)
                    disp = 0.0
                    if last_pos is not None:
                        disp = math.hypot((pos.x - last_pos[0]), (pos.y - last_pos[1]))
                    vel_eps = 0.01
                    move_eps = 0.25
                    dir_vec = None
                    if mag > vel_eps:
                        dir_vec = (vx / mag, vy / mag)
                        last_dir = dir_vec
                        if disp <= move_eps and last_pos is not None:
                            stuck_frames = (stuck_frames or 0) + 1
                        else:
                            stuck_frames = 0
                    else:
                        dir_vec = last_dir
                        if (stuck_frames or 0) > 0:
                            stuck_frames = max(0, (stuck_frames or 0) - 1)
                    blocked = (stuck_frames or 0) >= 5
                    color = RED_BLOCKED if blocked else (BLUE_DEBUG if mag > vel_eps else BLUE_FAINT)
                    if dir_vec and r > 2:
                        dx = int(dir_vec[0] * r)
                        dy = int(dir_vec[1] * r)
                        pygame.draw.line(screen, color, (cx, cy), (cx + dx, cy + dy), 2)
                    else:
                        pygame.draw.circle(screen, color, (cx, cy), 2)
                    # Save state
                    self._set_debug_state(world, nid, (pos.x, pos.y), last_dir, stuck_frames)
