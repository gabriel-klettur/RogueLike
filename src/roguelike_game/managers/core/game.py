
# Path: src/roguelike_game/game/core/game.py
import pygame
import roguelike_engine.config.config as config

from roguelike_engine.input.events import handle_events
from roguelike_engine.utils.benchmark import benchmark
from roguelike_game.managers.core.update_manager import update_game
from roguelike_game.managers.core.loop_manager import GameLoop
from roguelike_game.managers.core.shutdown_manager import ShutdownManager
from roguelike_game.managers.core.initializer import GameInitializer

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
        # Procesar QUIT antes que nada
        # Detectar QUIT sin consumir otros eventos
        if pygame.event.peek(pygame.QUIT):
            # eliminar eventos QUIT
            pygame.event.get(pygame.QUIT)
            self.state.running = False
            return
        # Capturar eventos y toggles de editores
        events = pygame.event.get()
        # Dispatch mouse events to DebugOverlay regardless of editor state
        for ev in events:
            if ev.type in (pygame.MOUSEWHEEL, pygame.MOUSEBUTTONDOWN):
                self.renderer.debug_overlay.handle_event(ev)
        # Revisar toggles de editores
        for event in events:
            if event.type == pygame.KEYDOWN and event.key == pygame.K_F6:
                # Alternar Inventory Editor
                new_vis = not self.inventory_editor.model.visible
                self.inventory_editor.model.visible = new_vis
                if new_vis:
                    # Inicializar lista de entidades
                    players = list(self.inventory_editor.world.components.get('PlayerTagComponent', {}).keys())
                    npcs = list(self.inventory_editor.world.components.get('NPCTagComponent', {}).keys())
                    self.inventory_editor.model.entities = players + npcs
                    self.inventory_editor.model.selected_eid = self.inventory_editor.model.entities[0] if self.inventory_editor.model.entities else None
                return
            if event.type == pygame.KEYDOWN and event.key == pygame.K_F7:
                # Alternar Item Editor
                self.state.item_editor_state.visible = not self.state.item_editor_state.visible
                return
                        # Toggle Debug Overlay (F9)
            if event.type == pygame.KEYDOWN and event.key == pygame.K_F9:
                config.DEBUG = not config.DEBUG
                print(f"🧪 DEBUG {'activado' if config.DEBUG else 'desactivado'}")
                return
            # Toggle Entities Debug (F12)
            if event.type == pygame.KEYDOWN and event.key == pygame.K_F12:
                config.DEBUG_ENTITIES = not config.DEBUG_ENTITIES
                print(f"🧪 ENTITIES DEBUG {'activado' if config.DEBUG_ENTITIES else 'desactivado'}")
                return
            if event.type == pygame.KEYDOWN and event.key == self.menu.input_config.get_key('toggle_tile_editor'):
                # Alternar Tile Editor
                self.tiles_editor.editor_state.active = not self.tiles_editor.editor_state.active
                return

            if event.type == pygame.KEYDOWN and event.key == self.menu.input_config.get_key('toggle_building_editor'):
                # Activate Building Editor (only when inactive)
                if not self.buildings_editor.editor_state.active:
                    self.buildings_editor.editor_state.active = True
                    self.buildings_editor.editor_state.picker_active = True
                    return
                # else, let BuildingEditorEventHandler.handle manage F10 cycling and exit


            if event.type == pygame.KEYDOWN and event.key == self.menu.input_config.get_key('toggle_map_editor'):
                # Alternar Map Editor
                self.map_editor.toggle()
                return
        # Si el editor de ítems está activo, capturar solo sus eventos
        if self.item_editor.model.visible:
            for event in events:
                self.item_editor.handle_event(event)
            return
        # Si el editor de inventario está activo, capturar solo sus eventos
        if hasattr(self, 'inventory_editor') and self.inventory_editor.model.visible:
            for event in events:
                self.inventory_editor.handle_event(event)
            return

        # Si un editor está activo, solo lo capturamos a él
        if self.tiles_editor.editor_state.active:
            self.tiles_editor.handle(self.camera, self.map)
            return

        if self.buildings_editor.editor_state.active:
            self.buildings_editor.handle(self.camera, self.buildings, events)
            return

        # Si no, delegamos al motor de eventos general
        handle_events(
            self.state,
            self.camera,
            self.clock,
            self.menu,
            self.map,
            self.buildings,            
            self.tiles_editor,
            self.buildings_editor,
            self.map_editor,
            self.renderer.debug_overlay
        )

    @benchmark(lambda self: self.perf_log, "2.TOTAL: UPDATE")
    def update(self):
        # Pause game update when inventory editor is open
        if self.inventory_editor.model.visible:
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
        # Overlay del Item Editor
        self.item_editor.draw(self.screen)
        self.inventory_editor.draw(self.screen)

    @benchmark(lambda self: self.perf_log, "4.2. ECS - update")
    def update_ecs(self):
        # Pause ECS update when inventory editor is open
        if self.inventory_editor.model.visible:
            return
        self.ecs.update(self.clock, self.screen, self.camera)

    @benchmark(lambda self: self.perf_log, "4.1 ECS - render")
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