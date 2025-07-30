import time
import pygame

import roguelike_engine.config.config as config
from roguelike_engine.utils.benchmark import benchmark


class GameLoop:
    def __init__(self, game: 'Game') -> None:
        self.game = game

    def run(self) -> None:
        """Bucle principal del juego."""
        while self.game.state.running:
            self._process_frame()

    def _process_frame(self) -> None:
        self.game.handle_events()
        self.game.update()
        self.game.render()

        if not self._is_editor_active():
            self.run_ecs_phase()

        self._post_frame()

    def _is_editor_active(self) -> bool:
        """Devuelve True si algún editor o menú está activo."""
        g = self.game
        return (
            g.menu.show_menu
            or g.tiles_editor.editor_state.active
            or g.buildings_editor.editor_state.active
            or g.map_editor.editor_state.active
            or g.item_editor.model.visible
            or g.inventory_editor.model.visible
        )

    @benchmark(lambda self: self.game.perf_log, "4.TOTAL: ECS")
    def run_ecs_phase(self) -> None:
        """Actualiza y renderiza el sistema ECS."""
        self.game.update_ecs()
        self.game.render_ecs()

    def _post_frame(self) -> None:
        """Flip de pantalla, actualización de FPS, autosave y cap de frames."""
        self._flip_display()
        self._update_caption()
        self._autosave_if_needed()
        self._cap_fps()

    def _flip_display(self) -> None:
        pygame.display.flip()

    def _update_caption(self) -> None:
        fps = self.game.clock.get_fps()
        pygame.display.set_caption(f"Roguelike - FPS: {fps:0.1f}")

    def _autosave_if_needed(self) -> None:
        cfg = self.game.world.config
        now = time.time()
        if cfg.autosave_enabled and (now - self.game._last_autosave_time >= cfg.autosave_interval):
            self.game.world.save_world()
            self.game._last_autosave_time = now

    def _cap_fps(self) -> None:
        self.game.clock.tick(config.FPS)
