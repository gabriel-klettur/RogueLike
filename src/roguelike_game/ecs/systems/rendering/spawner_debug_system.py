"""
SpawnerDebugRenderSystem: draws spawner anchors and proximity radii for debugging.
"""
from __future__ import annotations

import pygame
import math
import roguelike_engine.config.config as config
from roguelike_engine.utils.benchmark import benchmark
from roguelike_engine.config.config_tiles import TILE_SIZE
from roguelike_game.ecs.utils.collider_utils import build_collider_rect, get_circle_world
from roguelike_game.ecs.components.physics.circle_collider import CircleCollider


class SpawnerDebugRenderSystem:
    def __init__(self, perf_log=None):
        self.perf_log = perf_log
        self.font = None
        # Track per-entity state to diagnose movement issues
        self._last_pos: dict[int, tuple[float, float]] = {}
        self._last_dir: dict[int, tuple[float, float]] = {}
        self._stuck_frames: dict[int, int] = {}

    def _auto_bottom_band_metrics(self, mask) -> tuple[int, int]:
        """Return (auto_center_x, avg_width) on the bottom band using weighted centroid of opaque pixels.
        Mirrors factory logic so cross matches feet center X.
        """
        try:
            w, h = mask.get_size()
        except Exception:
            return 0, 0
        if w <= 0 or h <= 0:
            return 0, 0
        band_h = max(6, min(max(6, h // 5), 28))
        y_start = h - band_h
        total_weight = 0.0
        sum_x = 0.0
        sum_width = 0.0
        for y in range(h - 1, y_start - 1, -1):
            weight = 1.0 + (y - y_start) * 0.3
            row_count = 0
            for x in range(w):
                if mask.get_at((x, y)):
                    sum_x += x * weight
                    row_count += 1
            if row_count > 0:
                total_weight += weight * row_count
                sum_width += (row_count * weight)
        if total_weight <= 0.0:
            return w // 2, 0
        cx = int(round(sum_x / total_weight))
        denom = max(1.0, (band_h))
        avg_width = int(round((sum_width / denom)))
        return max(0, min(w - 1, cx)), max(0, avg_width)

    def _ensure_font(self):
        if self.font is None:
            try:
                self.font = pygame.font.SysFont('Arial', 14)
            except Exception:
                self.font = None

    @benchmark(lambda self: self.perf_log, "4.2.[RENDER]SpawnerDebugRenderSystem")
    def update(self, world, screen, camera):
        # Only render when the Spawner Editor is visible
        if not getattr(config, 'DEBUG_SPAWNER', False):
            return
        comps = world.components
        if 'SpawnerConfig' not in comps:
            return
        self._ensure_font()
        zoom = getattr(camera, 'zoom', 1.0) or 1.0
        hovered_eid = None
        remove_candidate = None
        try:
            hovered_eid = getattr(getattr(world, 'state', None), 'spawner_editor_hovered_eid', None)
        except Exception:
            hovered_eid = None
        try:
            remove_candidate = getattr(getattr(world, 'state', None), 'spawner_remove_candidate_eid', None)
        except Exception:
            remove_candidate = None
        for eid in world.get_entities_with('SpawnerConfig', 'SpawnerState'):
            cfg = comps['SpawnerConfig'][eid]
            st = comps['SpawnerState'][eid]
            tx, ty = cfg.anchor_tile
            # convert tile center to screen position
            px = tx * TILE_SIZE + TILE_SIZE // 2
            py = ty * TILE_SIZE + TILE_SIZE // 2
            sx, sy = camera.apply((px, py))

            # Draw anchor (crosshair + dot)
            cx, cy = int(sx), int(sy)
            base_col = (0, 200, 255)
            is_hover = (eid == hovered_eid)
            is_remove_sel = (eid == remove_candidate)
            if is_remove_sel:
                dot_col = (255, 60, 60)
                cross_col = (255, 60, 60)
                # Red selection halo
                halo_r = int(14 * zoom)
                pygame.draw.circle(screen, (255, 60, 60), (cx, cy), max(halo_r, 8), width=3)
            else:
                dot_col = (255, 220, 0) if is_hover else base_col
                cross_col = (255, 220, 0) if is_hover else base_col
                # Yellow hover halo
                if is_hover:
                    halo_r = int(14 * zoom)
                    pygame.draw.circle(screen, (255, 220, 0), (cx, cy), max(halo_r, 8), width=3)
            pygame.draw.circle(screen, dot_col, (cx, cy), 4)
            arm = 8
            pygame.draw.line(screen, cross_col, (cx - arm, cy), (cx + arm, cy), 2)
            pygame.draw.line(screen, cross_col, (cx, cy - arm), (cx, cy + arm), 2)
            # Draw proximity radius if applicable
            if (cfg.trigger or {}).get('type') == 'proximity':
                radius_tiles = int((cfg.trigger or {}).get('radius', 5))
                r_px_world = radius_tiles * TILE_SIZE
                r_px = max(1, int(r_px_world * zoom))
                # Filled alpha overlay for high visibility
                size = r_px * 2
                overlay = pygame.Surface((size, size), pygame.SRCALPHA)
                pygame.draw.circle(overlay, (0, 200, 255, 50), (r_px, r_px), r_px, width=0)
                pygame.draw.circle(overlay, (0, 200, 255, 180), (r_px, r_px), r_px, width=2)
                screen.blit(overlay, (cx - r_px, cy - r_px))

            # Draw spawn_radius if numeric (>0) to visualize random-in-area (circle or square)
            try:
                sr = getattr(cfg, 'spawn_radius', None)
                sr_val = int(sr) if isinstance(sr, (int, float)) else 0
            except Exception:
                sr_val = 0
            if sr_val and sr_val > 0:
                r_tiles = int(sr_val)
                r_px_world = r_tiles * TILE_SIZE
                r_px = max(1, int(r_px_world * zoom))
                shape = str(getattr(cfg, 'spawner_shape', 'circle') or 'circle').lower()
                size = r_px * 2
                overlay = pygame.Surface((size, size), pygame.SRCALPHA)
                # green overlay (different from proximity cyan)
                if shape == 'square':
                    rect = pygame.Rect(0, 0, size, size)
                    overlay.fill((60, 220, 80, 40))
                    screen.blit(overlay, (cx - r_px, cy - r_px))
                    pygame.draw.rect(screen, (60, 220, 80, 180), pygame.Rect(cx - r_px, cy - r_px, size, size), width=2)
                else:
                    pygame.draw.circle(overlay, (60, 220, 80, 40), (r_px, r_px), r_px, width=0)
                    pygame.draw.circle(overlay, (60, 220, 80, 180), (r_px, r_px), r_px, width=2)
                    screen.blit(overlay, (cx - r_px, cy - r_px))

            # Optional label
            if self.font:
                # Build compact multiline info centered on the spawner anchor (inside cyan circle)
                fps = getattr(config, 'FPS', 60) or 60
                total_waves = max(1, len(getattr(cfg, 'waves', []) or []))
                wave_num = min(getattr(st, 'current_wave_idx', 0) + 1, total_waves)
                live = 0
                try:
                    live = len(getattr(st, 'current_wave_entities', set()) or [])
                except Exception:
                    live = 0
                exp = int(getattr(st, 'expected_this_wave', 0) or 0)
                cd_frames = int(getattr(st, 'cooldown_remaining', 0) or 0)
                rc_frames = int(getattr(st, 'restart_cooldown_remaining', 0) or 0)
                cd_s = cd_frames / float(fps)
                rc_s = rc_frames / float(fps)
                loop_policy = bool((getattr(cfg, 'policy', {}) or {}).get('loop') or (getattr(cfg, 'policy', {}) or {}).get('repeat') or (getattr(cfg, 'policy', {}) or {}).get('restart_on_done'))
                mode = str((getattr(cfg, 'policy', {}) or {}).get('mode', '') or '')
                status = 'ON' if getattr(st, 'started', False) else 'OFF'
                if getattr(st, 'finished', False):
                    status = 'DONE'

                # Decide which cooldown to display
                if getattr(st, 'finished', False) and rc_frames > 0:
                    cd_line = f"rc {rc_s:.2f}s"
                else:
                    cd_line = f"cd {cd_s:.2f}s"

                shape = str(getattr(cfg, 'spawner_shape', 'circle') or 'circle').lower()
                lines = [
                    f"{cfg.template_id}",
                    f"{status} | wave {wave_num}/{total_waves}",
                    f"live {live}/{exp} | {cd_line}",
                    f"{mode} | loop:{'on' if loop_policy else 'off'} | shape:{shape}",
                ]

                # Render multiline with translucent background, centered at (cx, cy)
                cyan = (0, 200, 255)
                padding = 4
                line_gap = 1
                line_surfs = [self.font.render(t, True, cyan) for t in lines]
                max_w = max((s.get_width() for s in line_surfs), default=0)
                total_h = sum((s.get_height() for s in line_surfs)) + line_gap * (len(line_surfs) - 1 if line_surfs else 0)
                box_w = max_w + padding * 2
                box_h = total_h + padding * 2
                box = pygame.Surface((box_w, box_h), pygame.SRCALPHA)
                # Background and border
                box.fill((0, 0, 0, 150))
                pygame.draw.rect(box, cyan, pygame.Rect(0, 0, box_w, box_h), width=1)
                # Blit lines
                y = padding
                for srf in line_surfs:
                    x = (box_w - srf.get_width()) // 2
                    box.blit(srf, (x, y))
                    y += srf.get_height() + line_gap
                screen.blit(box, (cx - box_w // 2, cy - box_h // 2))

        # Draw NPC 'feet' colliders in pink to visualize overlap hitboxes
        pos_map = comps.get('Position', {})
        multi_map = comps.get('MultiCollider', {})
        death_map = comps.get('DeathTimer', {})
        player_map = comps.get('PlayerTagComponent', {})
        vel_map = comps.get('Velocity', {})
        pink_outline = (255, 105, 180)
        pink_fill = (255, 105, 180, 80)
        blue_debug = (80, 160, 255)
        blue_faint = (120, 180, 255)
        red_blocked = (255, 80, 80)

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
            if isinstance(feet, CircleCollider):
                # Draw true circle overlay and outline
                cx_w, cy_w, r_w = get_circle_world(pos.x, pos.y, feet)
                cx_s, cy_s = camera.apply((cx_w, cy_w))
                sr = max(1, int(r_w * zoom))
                size = sr * 2
                overlay = pygame.Surface((size, size), pygame.SRCALPHA)
                pygame.draw.circle(overlay, pink_fill, (sr, sr), sr, width=0)
                pygame.draw.circle(overlay, pink_outline, (sr, sr), sr, width=2)
                screen.blit(overlay, (int(cx_s) - sr, int(cy_s) - sr))

                # Visualize the exact alignment counterpart: bottom-band center from body mask
                body = multi.colliders.get('body')
                if body is not None and getattr(body, 'mask', None) is not None:
                    auto_cx, _ = self._auto_bottom_band_metrics(body.mask)
                    try:
                        bw, bh = body.mask.get_size()
                    except Exception:
                        bw, bh = 0, 0
                    # DEBUG: hot-fix vertical alignment by syncing feet.offset_x to centroid
                    expected_x = getattr(body, 'offset_x', 0) + auto_cx
                    if abs(getattr(feet, 'offset_x', 0) - expected_x) >= 1:
                        feet.offset_x = expected_x
                    anchor_wx = pos.x + expected_x
                    anchor_wy = pos.y + getattr(body, 'offset_y', 0) + max(0, bh - 1)
                    ax, ay = camera.apply((anchor_wx, anchor_wy))
                    ax = int(ax); ay = int(ay)
                    cross = 5
                    pygame.draw.line(screen, blue_debug, (ax - cross, ay), (ax + cross, ay), 2)
                    pygame.draw.line(screen, blue_debug, (ax, ay - cross), (ax, ay + cross), 2)
                    pygame.draw.line(screen, blue_debug, (ax, ay), (int(cx_s), int(cy_s)), 1)

                # Direction indicator using circle center and radius
                vel = vel_map.get(nid)
                if vel is not None:
                    cx = int(cx_s)
                    cy = int(cy_s)
                    r = max(3, sr - 3)
                    if r > 2:
                        pygame.draw.circle(screen, blue_debug, (cx, cy), r, width=2)
                    vx = getattr(vel, 'vx', 0.0) or 0.0
                    vy = getattr(vel, 'vy', 0.0) or 0.0
                    mag = math.hypot(vx, vy)
                    lastp = self._last_pos.get(nid)
                    disp = 0.0
                    if lastp is not None:
                        disp = math.hypot((pos.x - lastp[0]), (pos.y - lastp[1]))
                    vel_eps = 0.01
                    move_eps = 0.25
                    dir_vec = None
                    if mag > vel_eps:
                        dir_vec = (vx / mag, vy / mag)
                        self._last_dir[nid] = dir_vec
                        if disp <= move_eps and lastp is not None:
                            self._stuck_frames[nid] = self._stuck_frames.get(nid, 0) + 1
                        else:
                            self._stuck_frames[nid] = 0
                    else:
                        dir_vec = self._last_dir.get(nid)
                        if self._stuck_frames.get(nid, 0) > 0:
                            self._stuck_frames[nid] = max(0, self._stuck_frames[nid] - 1)
                    blocked = self._stuck_frames.get(nid, 0) >= 5
                    color = red_blocked if blocked else (blue_debug if mag > vel_eps else blue_faint)
                    if dir_vec and r > 2:
                        dx = int(dir_vec[0] * r)
                        dy = int(dir_vec[1] * r)
                        pygame.draw.line(screen, color, (cx, cy), (cx + dx, cy + dy), 2)
                    else:
                        pygame.draw.circle(screen, color, (cx, cy), 2)
                    self._last_pos[nid] = (pos.x, pos.y)
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
                overlay.fill(pink_fill)
                screen.blit(overlay, (sx, sy))
                pygame.draw.rect(screen, pink_outline, pygame.Rect(sx, sy, sw, sh), width=2)

                vel = vel_map.get(nid)
                if vel is not None:
                    cx = sx + sw // 2
                    cy = sy + sh // 2
                    r = max(3, min(sw, sh) // 2 - 3)
                    if r > 2:
                        pygame.draw.circle(screen, blue_debug, (cx, cy), r, width=2)
                    vx = getattr(vel, 'vx', 0.0) or 0.0
                    vy = getattr(vel, 'vy', 0.0) or 0.0
                    mag = math.hypot(vx, vy)
                    lastp = self._last_pos.get(nid)
                    disp = 0.0
                    if lastp is not None:
                        disp = math.hypot((pos.x - lastp[0]), (pos.y - lastp[1]))
                    vel_eps = 0.01
                    move_eps = 0.25
                    dir_vec = None
                    if mag > vel_eps:
                        dir_vec = (vx / mag, vy / mag)
                        self._last_dir[nid] = dir_vec
                        if disp <= move_eps and lastp is not None:
                            self._stuck_frames[nid] = self._stuck_frames.get(nid, 0) + 1
                        else:
                            self._stuck_frames[nid] = 0
                    else:
                        dir_vec = self._last_dir.get(nid)
                        if self._stuck_frames.get(nid, 0) > 0:
                            self._stuck_frames[nid] = max(0, self._stuck_frames[nid] - 1)
                    blocked = self._stuck_frames.get(nid, 0) >= 5
                    color = red_blocked if blocked else (blue_debug if mag > vel_eps else blue_faint)
                    if dir_vec and r > 2:
                        dx = int(dir_vec[0] * r)
                        dy = int(dir_vec[1] * r)
                        pygame.draw.line(screen, color, (cx, cy), (cx + dx, cy + dy), 2)
                    else:
                        pygame.draw.circle(screen, color, (cx, cy), 2)
                    self._last_pos[nid] = (pos.x, pos.y)
