"""
SpawnerDebugRenderSystem: draws spawner anchors and proximity radii for debugging.
"""
from __future__ import annotations

import pygame
import roguelike_engine.config.config as config
from roguelike_engine.utils.benchmark import benchmark
from roguelike_engine.config.config_tiles import TILE_SIZE


class SpawnerDebugRenderSystem:
    def __init__(self, perf_log=None):
        self.perf_log = perf_log
        self.font = None

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

            # Optional label
            if self.font:
                label = f"{cfg.template_id}:{'ON' if st.started else 'OFF'}"
                surf = self.font.render(label, True, (0, 200, 255))
                screen.blit(surf, (sx + 6, sy - 6))
