#Path: src/roguelike_game/game/game.py

import pygame

#!---------------------- Paquetes locales: configuración --------------------------------
import roguelike_engine.config as config

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
from roguelike_game.systems.systems_manager import SystemsManager

#!-------------------------- Paquetes locales: menús e interfaz -------------------------------
from roguelike_game.game.menu_manager import MenuManager

#! --------------------- Paquetes locales: editores (tile) -------------------------------------
from roguelike_game.game.buildings_editor_manager import BuildingEditorManager
from roguelike_game.game.tiles_editor_manager import TilesEditorManager

#! -------------------------- Paquetes locales: z-layer ----------------------------------------
from roguelike_game.systems.z_layer.state import ZState

#! -------------------------- Paquetes locales: utilidades --------------------------------------
from roguelike_engine.utils.benchmark import benchmark

class Game:
    def __init__(
            self, 
            screen, 
            perf_log=None,             
            map_name: str = None
    ):        
        #! ------------- infraestructura -----------------
        self.screen = screen
        self.clock = pygame.time.Clock()
        self.font = pygame.font.SysFont(config.FONT_NAME, config.FONT_SIZE)
        self.camera = Camera(config.SCREEN_WIDTH, config.SCREEN_HEIGHT)
        self.z_state = ZState()
        self.perf_log = perf_log

        #! ---------------- core state -------------------
        self._init_state()
        
        #! ------------------ systems --------------------
        self._init_map(map_name)
        self._init_entities() 
        self._init_z_layer(self.entities)
        self._init_buildings_editor()
        self._init_tile_editor()
        self._init_renderer()
        self._init_menu()
        self._init_systems(perf_log)        

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
        Inicializa el gestor de entidades (player, enemies, obstacles y buildings) y carga los datos en el estado.
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
            self.perf_log
        )

    def _init_menu(self):
        """
        Inicializa el menú principal del juego.
        """
        self.menu = MenuManager(self.state)        

    def _init_systems(self, perf_log):
        """
        Inicializa los sistemas del juego (combat, effects, explosions, etc.).
        """
        self.systems = SystemsManager(self.state, perf_log)

    @benchmark(lambda self: self.perf_log, "1.handle_events")
    def handle_events(self):        
        handle_events(
            self.state,
            self.camera,
            self.clock,
            self.menu,
            self.map,
            self.entities,
            self.systems.effects,
            self.systems.explosions,
            self.tiles_editor,
            self.buildings_editor
        )

    @benchmark(lambda self: self.perf_log, "2.update")
    def update(self):
        update_game(
            self.state,
            self.systems,
            self.camera,
            self.clock,
            self.screen,
            self.map,
            self.entities,            
            self.tiles_editor,
            self.buildings_editor
        )

    @benchmark(lambda self: self.perf_log, "3.total_render")
    def render(self, perf_log=None):
        self.renderer.render_game(
            self.state,
            self.screen,
            self.camera,
            perf_log,
            menu=self.menu,
            map=self.map,
            entities=self.entities,
            systems=self.systems
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
            self.render(self.perf_log)

            # 4) Actualizar título con FPS actuales
            fps = self.clock.get_fps()
            pygame.display.set_caption(f"Roguelike - FPS: {fps:0.1f}")

            # 5) Limitar velocidad de fotogramas
            self.clock.tick(config.FPS)