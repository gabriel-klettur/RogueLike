from __future__ import annotations

import pygame
import roguelike_engine.config.config as config
from roguelike_engine.config.config_tiles import TILE_SIZE


class SpawnerAnchorDebugRenderSystem:
    """Draws spawner anchors and spawn/proximity radii with hover/selection rings.
    Gated by Spawner Editor active or config.DEBUG_SPAWNER.
    """

    def __init__(self, perf_log=None):
        self.perf_log = perf_log

    def update(self, world, screen, camera):
        # Gate: render when Spawner Editor is active OR when global DEBUG_SPAWNER is on
        editor_active = False
        try:
            editor_active = bool(getattr(getattr(world, 'state', None), 'spawner_editor_active', False))
        except Exception:
            editor_active = False
        if not editor_active and not getattr(config, 'DEBUG_SPAWNER', False):
            return

        comps = world.components
        if 'SpawnerConfig' not in comps:
            return

        zoom = getattr(camera, 'zoom', 1.0) or 1.0
        try:
            hovered_eid = getattr(getattr(world, 'state', None), 'spawner_editor_hovered_eid', None)
        except Exception:
            hovered_eid = None
        try:
            selected_eid = getattr(getattr(world, 'state', None), 'spawner_selected_eid', None)
        except Exception:
            selected_eid = None
        try:
            remove_candidate = getattr(getattr(world, 'state', None), 'spawner_remove_candidate_eid', None)
        except Exception:
            remove_candidate = None

        base_cyan = (0, 200, 255)
        yellow = (255, 220, 0)

        for eid in world.get_entities_with('SpawnerConfig', 'SpawnerState'):
            cfg = comps['SpawnerConfig'][eid]
            st = comps['SpawnerState'][eid]
            tx, ty = cfg.anchor_tile
            # convert tile center to screen position
            px = tx * TILE_SIZE + TILE_SIZE // 2
            py = ty * TILE_SIZE + TILE_SIZE // 2
            sx, sy = camera.apply((px, py))

            # Draw anchor (crosshair + dot) with hover/selection rings
            cx, cy = int(sx), int(sy)
            is_hover = (eid == hovered_eid)
            is_selected = (eid == selected_eid)
            is_remove_sel = (eid == remove_candidate)
            # Priority: remove (red) > selected (yellow) > hover (cyan)
            if is_remove_sel:
                ring_col = (255, 60, 60)
                ring_r = max(int(16 * zoom), 10)
                ring_w = 3
                pygame.draw.circle(screen, ring_col, (cx, cy), ring_r, width=ring_w)
                dot_col = ring_col
                cross_col = ring_col
            elif is_selected:
                ring_col = yellow
                ring_r = max(int(16 * zoom), 10)  # slightly larger than hover
                ring_w = 4
                pygame.draw.circle(screen, ring_col, (cx, cy), ring_r, width=ring_w)
                dot_col = yellow
                cross_col = yellow
            elif is_hover:
                ring_col = base_cyan
                ring_r = max(int(12 * zoom), 8)
                ring_w = 3
                pygame.draw.circle(screen, ring_col, (cx, cy), ring_r, width=ring_w)
                dot_col = base_cyan
                cross_col = base_cyan
            else:
                dot_col = base_cyan
                cross_col = base_cyan
            pygame.draw.circle(screen, dot_col, (cx, cy), 4)
            arm = 8
            pygame.draw.line(screen, cross_col, (cx - arm, cy), (cx + arm, cy), 2)
            pygame.draw.line(screen, cross_col, (cx, cy - arm), (cx, cy + arm), 2)

            # Draw proximity radius if applicable
            if (cfg.trigger or {}).get('type') == 'proximity':
                radius_tiles = int((cfg.trigger or {}).get('radius', 5))
                r_px_world = radius_tiles * TILE_SIZE
                r_px = max(1, int(r_px_world * zoom))
                outline_w = 4
                pygame.draw.circle(screen, (0, 200, 255), (cx, cy), r_px, width=outline_w)

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
                if shape == 'square':
                    outline_w = 4
                    pygame.draw.rect(screen, (60, 220, 80), pygame.Rect(cx - r_px, cy - r_px, size, size), width=outline_w)
                else:
                    outline_w = 4
                    pygame.draw.circle(screen, (60, 220, 80), (cx, cy), r_px, width=outline_w)
