# Path: src/roguelike_game/game/game.py
import pygame
import time

#!---------------------- Paquetes locales: configuración --------------------------------
import roguelike_engine.config.config as config

#!------------------------ Paquetes locales: motor (engine) -----------------------------------
from roguelike_engine.camera.camera import Camera
from roguelike_engine.input.events import handle_events

#!-------------------- Paquetes locales: lógica de juego principal ----------------------------
from roguelike_game.game.state import GameState
from roguelike_game.game.render_manager import RendererManager
from roguelike_game.game.update_manager import update_game

#!----------------------------- Paquetes locales: managers ------------------------------------
from roguelike_game.game.map_manager import MapManager
from roguelike_game.game.entities_manager import EntitiesManager
from roguelike_game.game.z_layer_manager import ZLayerManager

#!----------------------- Paquetes locales: sistemas ------------------------------
from roguelike_game.systems.effects_manager import EffectsManager

#!-------------------------- Paquetes locales: menús e interfaz -------------------------------
from roguelike_game.game.menu_manager import MenuManager

#! --------------------- Paquetes locales: editores (tile) -------------------------------------
from roguelike_game.game.buildings_editor_manager import BuildingEditorManager
from roguelike_game.game.tiles_editor_manager import TilesEditorManager
from roguelike_engine.minimap.minimap import Minimap

#! -------------------------- Paquetes locales: z-layer ----------------------------------------
from roguelike_game.systems.z_layer.state import ZState

#! -------------------------- Paquetes locales: utilidades --------------------------------------
from roguelike_engine.utils.benchmark import benchmark

#! -------------------------- Paquetes locales: loading screen ---------------------------------
from roguelike_engine.utils.loading_screen import LoadingScreen

#! -------------------------- Paquetes locales: world ---------------------------------
from roguelike_engine.world.world import WorldManager
from roguelike_engine.world.world_config import WORLD_CONFIG
from roguelike_game.entities.player.model.player_data import PlayerData

#! -------------------------- Paquetes locales: ECS ---------------------------------
from roguelike_game.game.ecs_manager import ECSManager

class Game:
    def __init__(
            self, 
            screen, 
            perf_log=None,             
            map_name: str = None,
            loading_bg: str | None = None
    ):        
                
        # loading stages
        stages = [
            ("Inicializando estados de sistemas",  lambda: self._init_systems_states(screen, perf_log, loading_bg)),
            ("Inicializando estado Principal",     lambda: self._init_state()),
            ("Cargando mapa",                      lambda: self._init_map(map_name)),
            ("Cargando entidades",                 lambda: self._init_entities()),
            ("Cargando Z-layer",                   lambda: self._init_z_layer(self.entities)),
            ("Cargando editor de edificios",       lambda: self._init_buildings_editor()),
            ("Cargando editor de tiles",           lambda: self._init_tile_editor()),
            ("Cargando minimapa",                  lambda: self._init_minimap()),
            ("Inicializando ECS",                  lambda: self._init_ecs(screen)),
            ("Inicializando renderizador",         lambda: self._init_renderer()),
            ("Inicializando menú",                 lambda: self._init_menu()),            
            ("Inicializando efectos",              lambda: self._init_effects(perf_log)),            
        ]
        total = len(stages)
        for i, (msg, func) in enumerate(stages):
            func()
            self.loader.draw((i+1)/total, msg)

    def _init_systems_states(self, screen, perf_log, loading_bg):
        """
        Inicializa el estado de los sistemas
        """
        # — Sistema principal —
        self.screen = screen
        self.clock = pygame.time.Clock()
        self.font = pygame.font.SysFont(config.FONT_NAME, config.FONT_SIZE)
        self.camera = Camera(config.SCREEN_WIDTH, config.SCREEN_HEIGHT)
        self.z_state = ZState()
        self.perf_log = perf_log

        # — Mundo y persistencia global —
        self.world = WorldManager(PlayerData(), WORLD_CONFIG)
        self._last_autosave_time = time.time()

        # initialize loading screen
        self.loader = LoadingScreen(self.screen, loading_bg)

    def _init_state(self):
        """
        Inicializa el estado del juego
        """
        self.state = GameState()        

    def _init_map(self, map_name: str | None):
        """
        Construye el mapa global y carga todos sus datos en el estado.
        """        
        self.map = MapManager(map_name)                                        

    def _init_entities(self):
        """
        Inicializa el gestor de entidades (player, obstacles y buildings) y carga los datos en el estado.
        """
        self.entities = EntitiesManager(self.z_state, self.map)        
        

    def _init_z_layer(self, entities):
        """
        Inicializa el gestor de capas Z y asigna las capas a las entidades.
        """
        self.zlayer = ZLayerManager(self.z_state)
        self.zlayer.initialize(self.state, entities)

    def _init_buildings_editor(self):
        """
        Inicializa el editor de edificios.
        """
        self.buildings_editor = BuildingEditorManager(self)

    def _init_tile_editor(self):
        """
        Inicializa el editor de tiles.
        """
        self.tiles_editor = TilesEditorManager(self)

    def _init_ecs(self, screen):
        """
        Inicializa el gestor ECS
        """
        self.ecs = ECSManager(screen)

    def _init_renderer(self):
        """
        Inicializa el renderizador con todas sus dependencias.
        """
        self.renderer = RendererManager(
            self.screen,
            self.camera,
            self.map,
            self.entities,
            self.buildings_editor,
            self.tiles_editor,
            self.perf_log,
            self.minimap,
            self.ecs
        )

    def _init_menu(self):
        """
        Inicializa el menú principal del juego.
        """
        self.menu = MenuManager(self.state)        

    def _init_effects(self, perf_log):
        """
        Inicializa los sistemas del juego (combat, effects, explosions, etc.).
        """
        self.effects = EffectsManager(self.state, perf_log)

    def _init_minimap(self):
        """
        Inicializa el minimapa del juego.
        """
        self.minimap = Minimap()

    @benchmark(lambda self: self.perf_log, "1.TOTAL: HANDLE EVENTS")
    def handle_events(self):        
        handle_events(
            self.state,
            self.camera,
            self.clock,
            self.menu,
            self.map,
            self.entities,
            self.effects.effects,
            self.effects.explosions,
            self.tiles_editor,
            self.buildings_editor,
            self.renderer.debug_overlay
        )

    @benchmark(lambda self: self.perf_log, "2.TOTAL UPDATE")
    def update(self):
        update_game(
            self.state,
            self.effects,
            self.camera,
            self.clock,
            self.screen,
            self.map,
            self.entities,            
            self.tiles_editor,
            self.buildings_editor,
            self.minimap,
            self.perf_log
        )

    @benchmark(lambda self: self.perf_log, "3.TOTAL RENDER")
    def render(self):
        self.renderer.render_game(
            self.state,
            self.screen,
            self.camera,
            self.perf_log,
            self.menu,
            self.map,
            self.entities,
            self.effects,            
        )

    
    def run(self):
        """
        Bucle principal del juego: maneja eventos, actualiza lógica y renderiza cada frame.
        """
        while self.state.running:
            # 1) Procesar entrada
            self.handle_events()

            # 2) Actualizar juego
            self.update()

            # 3) Renderizar todo (incluye pygame.display.flip() dentro de render_game)
            self.render()

            # 4) Actualizar título con FPS actuales
            fps = self.clock.get_fps()
            pygame.display.set_caption(f"Roguelike - FPS: {fps:0.1f}")

            # 5) Autosave periódico según configuración de WorldConfig            
            if self.world.config.autosave_enabled and time.time() - self._last_autosave_time >= self.world.config.autosave_interval:
                # Guarda en el path por defecto (WORLD_CONFIG.save_path)
                self.world.save_world()
                self._last_autosave_time = time.time()

            # 6) Limitar velocidad de fotogramas
            self.clock.tick(config.FPS)

if __name__ == "__main__":
    # Inicializa pygame
    pygame.init()

    # Crea la pantalla del juego
    screen = pygame.display.set_mode((config.SCREEN_WIDTH, config.SCREEN_HEIGHT))

    # Crea una instancia del juego
    game = Game(screen)

    # Ejecuta el bucle principal del juego
    game.run()

    # Finaliza pygame
    pygame.quit()