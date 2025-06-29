
# Path: src/roguelike_game/game/core/game.py
import pygame

from roguelike_engine.input.events import handle_events
from roguelike_engine.utils.benchmark import benchmark
from roguelike_game.game.update_manager import update_game
from roguelike_game.game.core.loop_manager import GameLoop
from roguelike_game.game.core.shutdown_manager import ShutdownManager
from roguelike_game.game.core.initializer import GameInitializer

class Game:
    def __init__(
        self,
        screen,
        perf_log=None,
        map_name: str = None,
        loading_bg: str | None = None,
        extra_stages: list[tuple] | None = None,
        extra_systems_stages: list[tuple] | None = None
    ):
        # 1) Parámetros básicos
        self.screen              = screen
        self.perf_log            = perf_log
        self.map_name            = map_name
        self.loading_bg          = loading_bg
        self.extra_stages        = extra_stages or []
        self.extra_systems_stages= extra_systems_stages or []

        # 2) Inicialización completa        
        self.initializer = GameInitializer(
            game=self,
            screen=screen,
            perf_log=perf_log,
            map_name=map_name,
            loading_bg=loading_bg,
            extra_stages=self.extra_stages,
            extra_systems_stages=self.extra_systems_stages
        )
        self.initializer.initialize()

        # 3) Bucle principal y gestor de cierre        
        self.loop             = GameLoop(self)
        self.shutdown_manager = ShutdownManager(self)

    @benchmark(lambda self: self.perf_log, "1.TOTAL: HANDLE EVENTS")
    def handle_events(self):
        # Si un editor está activo, solo lo capturamos a él
        if self.tiles_editor.editor_state.active:
            self.tiles_editor.handle(self.camera, self.map)
            return

        # Si no, delegamos al motor de eventos general
        handle_events(
            self.state,
            self.camera,
            self.clock,
            self.menu,
            self.map,
            self.buildings,
            self.effects,
            self.effects.explosions,
            self.tiles_editor,
            self.buildings_editor,
            self.map_editor,
            self.renderer.debug_overlay
        )

    @benchmark(lambda self: self.perf_log, "2.TOTAL: UPDATE")
    def update(self):
        update_game(
            self.state,
            self.effects,
            self.camera,
            self.clock,
            self.screen,
            self.map,
            self.buildings,
            self.tiles_editor,
            self.buildings_editor,
            self.map_editor,
            self.minimap,
            self.ecs,
            self.perf_log
        )

    @benchmark(lambda self: self.perf_log, "3.TOTAL: RENDER")
    def render(self):
        self.renderer.render_game(
            self.state,
            self.screen,
            self.camera,
            self.perf_log,
            self.menu,
            self.map,
            self.buildings,
            self.effects,
        )

    @benchmark(lambda self: self.perf_log, "4.2.ecs - update")
    def update_ecs(self):
        self.ecs.update(self.clock, self.screen, self.camera)

    @benchmark(lambda self: self.perf_log, "4.1 ecs - render")
    def render_ecs(self):
        self.ecs.render(self.screen, self.camera)

    def run(self):
        """Arranca el bucle principal."""
        self.loop.run()

    def shutdown(self):
        """Guarda todo y cierra."""
        self.shutdown_manager.shutdown()


# Si ejecutas este módulo directamente:
if __name__ == "__main__":
    import roguelike_engine.config.config as config

    pygame.init()
    screen = pygame.display.set_mode(
        (config.SCREEN_WIDTH, config.SCREEN_HEIGHT)
    )
    game = Game(screen)
    game.run()
    pygame.quit()