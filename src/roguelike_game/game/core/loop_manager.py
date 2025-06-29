
# Path: src/roguelike_game/game/core/loop_manager.py
import time
import pygame

import roguelike_engine.config.config as config
from roguelike_engine.config.config_tiles import TILE_SIZE
from roguelike_engine.utils.benchmark import benchmark
from roguelike_game.ecs.systems.rendering.render_system import RenderSystem
from roguelike_engine.config.map_config import global_map_settings


class GameLoop:
    def __init__(self, game):
        self.game = game

    def run(self):
        g = self.game
        while g.state.running:
            g.handle_events()
            g.update()
            g.render()

            # ECS only si no hay ningún editor abierto
            if not (
                g.tiles_editor.editor_state.active or
                g.buildings_editor.editor_state.active or
                g.map_editor.editor_state.active
            ):
                g.update_ecs()
                g.render_ecs()

            self._post_frame()


    def _post_frame(self):
        g = self.game

        # 1) Grayscale + overlay de lobby
        if g.ecs.ecs_world.components.get('GrayscaleComponent'):
            RenderSystem(g.screen).apply_grayscale(g.screen)
            self._draw_lobby_overlay()

        # 2) Flip
        pygame.display.flip()

        # 3) FPS
        fps = g.clock.get_fps()
        pygame.display.set_caption(f"Roguelike - FPS: {fps:0.1f}")

        # 4) Autosave
        cfg = g.world.config
        if cfg.autosave_enabled and time.time() - g._last_autosave_time >= cfg.autosave_interval:
            g.world.save_world()
            g._last_autosave_time = time.time()

        # 5) Cap FPS
        g.clock.tick(config.FPS)


    def _draw_lobby_overlay(self):
        g = self.game
        lob_x, lob_y = g.ecs.ecs_world.map_manager.lobby_offset
        cw, ch = global_map_settings.zone_width, global_map_settings.zone_height
        tx, ty = lob_x + cw//2 - 1, lob_y + ch//2 - 1
        wx, wy = tx * TILE_SIZE, ty * TILE_SIZE
        x0, y0 = g.camera.apply((wx, wy))
        size = TILE_SIZE * 3
        overlay = pygame.Surface((size, size), pygame.SRCALPHA)
        overlay.fill((255, 255, 0, 80))
        g.screen.blit(overlay, (x0, y0))
        pygame.draw.rect(
            g.screen,
            (255, 255, 0),
            pygame.Rect(x0, y0, size, size),
            3
        )