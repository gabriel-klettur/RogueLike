
# Path: src/roguelike_game/game/core/loop_manager.py
import time
import pygame

import roguelike_engine.config.config as config
from roguelike_engine.utils.benchmark import benchmark

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
                self.run_ecs_phase(g)

            self._post_frame()


    @benchmark(lambda self: self.game.perf_log, "4.TOTAL: ECS")
    def run_ecs_phase(self, g):        
        g.update_ecs()
        g.render_ecs()

    def _post_frame(self):

        # 1) Actualizar ECS
        g = self.game

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