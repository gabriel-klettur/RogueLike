import pygame
import roguelike_engine.config.config as config

from roguelike_game.managers.core.events import handle_events as core_handle_events
from roguelike_engine.utils.benchmark import benchmark
from roguelike_game.managers.core.update_manager import update_game
from roguelike_game.managers.core.loop_manager import GameLoop
from roguelike_game.managers.core.shutdown_manager import ShutdownManager
from roguelike_game.managers.core.initializer import GameInitializer

class Game:
    
    #!---------------------------------------------------------------------------------------------------------------------
    #!-------------------------------------------------- INICIALIZACION ---------------------------------------------------
    #!---------------------------------------------------------------------------------------------------------------------
    
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
        self.initializer = GameInitializer.create_and_initialize(
            game=self,
            screen=screen,
            perf_log=perf_log,
            map_name=map_name,
            loading_bg=loading_bg,
            extra_stages=self.extra_stages,
            extra_systems_stages=self.extra_systems_stages
        )        

        # 3) Bucle principal y gestor de cierre        
        self.loop             = GameLoop(self)
        self.shutdown_manager = ShutdownManager(self)
        
    #!---------------------------------------------------------------------------------------------------------------------
    #!-------------------------------------------------- LOOP PRINCIPAL ---------------------------------------------------
    #!---------------------------------------------------------------------------------------------------------------------

    @benchmark(lambda self: self.perf_log, "1.TOTAL: HANDLE EVENTS")
    def handle_events(self):
        core_handle_events(self)
        return
 
    @benchmark(lambda self: self.perf_log, "2.TOTAL: UPDATE")
    def update(self):        
        if self.inventory_editor.model.visible:
            return
        if hasattr(self, 'entities_editor') and self.entities_editor.model.visible:
            return
        if hasattr(self, 'spells_editor') and self.spells_editor.model.visible:
            return
        update_game(
            self.state,            
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
        # Renderiza el mundo
        self.renderer.render_game(
            self.state,
            self.screen,
            self.camera,
            self.perf_log,
            self.menu,
            self.map,
            self.buildings            
        )
        if hasattr(self, 'class_selector') and self.class_selector.show:
            self.class_selector.draw()
            return
        # Overlay del Item Editor
        self.item_editor.draw(self.screen)
        self.inventory_editor.draw(self.screen)
        self.entities_editor.draw(self.screen)
        self.spells_editor.draw(self.screen)
        # Render consola
        self.console_view.render(self.screen)

    #!---------------------------------------------------------------------------------------------------------------------
    #!-------------------------------------------------- LOOP ECS ---------------------------------------------------------
    #!---------------------------------------------------------------------------------------------------------------------

    @benchmark(lambda self: self.perf_log, "4.2. ECS - update")
    def update_ecs(self):
        # Pause ECS update when inventory editor is open
        if self.inventory_editor.model.visible:
            return
        if hasattr(self, 'entities_editor') and self.entities_editor.model.visible:
            return
        if hasattr(self, 'spells_editor') and self.spells_editor.model.visible:
            return
        self.ecs.update(self.clock, self.screen, self.camera)

    @benchmark(lambda self: self.perf_log, "4.1 ECS - render")
    def render_ecs(self):
        self.ecs.render(self.screen, self.camera)
    
    #!---------------------------------------------------------------------------------------------------------------------
    #!-------------------------------------------------- BUCLE PRINCIPAL --------------------------------------------------
    #!---------------------------------------------------------------------------------------------------------------------
    
    def run(self):
        """Arranca el bucle principal."""
        self.loop.run()


    #!---------------------------------------------------------------------------------------------------------------------
    #!-------------------------------------------------- SHUTDOWN ---------------------------------------------------------
    #!---------------------------------------------------------------------------------------------------------------------

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