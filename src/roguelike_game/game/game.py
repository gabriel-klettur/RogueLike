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
from roguelike_game.game.buildings_manager import BuildingsManager
from roguelike_game.game.z_layer_manager import ZLayerManager

#!----------------------- Paquetes locales: sistemas ------------------------------
from roguelike_game.systems.effects_manager import EffectsManager

#!-------------------------- Paquetes locales: menús e interfaz -------------------------------
from roguelike_game.game.menu_manager import MenuManager

#! --------------------- Paquetes locales: editores (tile) -------------------------------------
from roguelike_game.game.buildings_editor_manager import BuildingEditorManager
from roguelike_game.game.tiles_editor_manager import TilesEditorManager
from roguelike_game.game.map_editor_manager import MapEditorManager
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
        """
        Constructor de Game. Solo se encarga de recibir los parámetros
        e invocar al método privado _initialize.
        """
        self._initialize(screen, perf_log, map_name, loading_bg)

    def _initialize(
        self,
        screen,
        perf_log: any = None,
        map_name: str | None = None,
        loading_bg: str | None = None
    ):
        """
        Orquesta la inicialización por etapas:
          1) Define una lista de tuplas (mensaje, función_init)
          2) Recorre cada etapa, la ejecuta y luego dibuja la barra de carga.
        """
        # Arreglo de etapas: (mensaje a mostrar, método que realiza la inicialización)
        stages = [
            ("Inicializando estados de sistemas",
             lambda: self._init_systems_states(screen, perf_log, loading_bg)),
            ("Inicializando estado Principal",
             lambda: self._init_state()),
            ("Cargando mapa",
             lambda: self._init_map(map_name)),
            ("Cargando edificios",
             lambda: self._init_buildings()),
            ("Cargando Z-layer",
             lambda: self._init_z_layer(self.buildings)),
            ("Cargando editor de edificios",
             lambda: self._init_buildings_editor()),
            ("Cargando editor de tiles",
             lambda: self._init_tile_editor()),
            ("Cargando editor de mapa",
             lambda: self._init_map_editor()),
            ("Cargando minimapa",
             lambda: self._init_minimap()),
            ("Inicializando ECS",
             lambda: self._init_ecs(screen, perf_log)),
            ("Inicializando renderizador",
             lambda: self._init_renderer()),
            ("Inicializando menú",
             lambda: self._init_menu()),
            ("Inicializando efectos",
             lambda: self._init_effects(perf_log)),
        ]

        total = len(stages)
        for i, (msg, func) in enumerate(stages):
            func()
            # Después de cada paso, dibujo la barra de carga
            # (en la primera etapa ya debe existir self.loader)
            self.loader.draw((i + 1) / total, msg)

    # -----------------------------------------------------------------------------------
    # Métodos _init_*: cada uno se encarga de inicializar una parte del Game
    # -----------------------------------------------------------------------------------

    def _init_systems_states(self, screen, perf_log, loading_bg):
        """
        Inicializa el estado de los sistemas:
          - Configura pantalla, reloj, fuente y cámara
          - Inicializa ZState y WorldManager
          - Crea la instancia de LoadingScreen (para que exista antes del primer draw)
        """
        # — Sistema principal —
        self.screen = screen
        self.clock = pygame.time.Clock()
        self.font = pygame.font.SysFont(config.FONT_NAME, config.FONT_SIZE)
        self.camera = Camera(config.SCREEN_WIDTH, config.SCREEN_HEIGHT)
        self.z_state = ZState()
        self.perf_log = perf_log

        # — Mundo y persistencia global —
        self.world = WorldManager(WORLD_CONFIG)
        self._last_autosave_time = time.time()

        # *** Importante: aquí creamos el objeto loader ANTES del primer draw() ***
        self.loader = LoadingScreen(self.screen, loading_bg)

    def _init_state(self):
        """
        Inicializa el estado del juego (GameState).
        """
        self.state = GameState()

    def _init_map(self, map_name: str | None):
        """
        Construye o carga el mapa actual en el WorldManager.
        """
        if self.world.current_level:
            # Si ya existe un nivel en WorldManager, lo cargamos
            self.world.load_level(self.world.current_level)
            self.map = self.world.maps[self.world.current_level]
        else:
            # Caso base: creamos un nuevo mapa desde MapManager
            self.map = MapManager(map_name)
            self.world.maps[self.map.name] = self.map
            self.world.current_level = self.map.name

    def _init_buildings(self):
        """
        Inicializa el gestor de edificios (BuildingsManager).
        """
        self.buildings = BuildingsManager(self.z_state, self.map)

    def _init_z_layer(self, buildings):
        """
        Inicializa el gestor de capas Z y asigna las capas a las entidades.
        """
        self.zlayer = ZLayerManager(self.z_state)
        self.zlayer.initialize(self.state, buildings)

    def _init_buildings_editor(self):
        """
        Inicializa el editor de edificios (BuildingEditorManager).
        """
        self.buildings_editor = BuildingEditorManager(self)

    def _init_tile_editor(self):
        """
        Inicializa el editor de tiles (TilesEditorManager).
        """
        self.tiles_editor = TilesEditorManager(self)

    def _init_map_editor(self):
        """
        Inicializa el editor de mapa (MapEditorManager).
        """
        self.map_editor = MapEditorManager(self)

    def _init_ecs(self, screen, perf_log):
        """
        Inicializa el gestor ECS (ECSManager), que maneja entidades, componentes y sistemas.
        """
        self.ecs = ECSManager(screen, self.map, self.buildings, perf_log)

    def _init_renderer(self):
        """
        Inicializa el renderizador (RendererManager) con todas sus dependencias.
        Se asegura de que map_editor esté preparado.
        """
        if not hasattr(self, "map_editor"):
            # Por seguridad, en caso de que _init_map_editor no se haya ejecutado
            self._init_map_editor()

        self.renderer = RendererManager(
            self.screen,
            self.camera,
            self.map,
            self.buildings,
            self.buildings_editor,
            self.tiles_editor,
            self.map_editor,
            self.perf_log,
            self.minimap,
            self.ecs
        )

    def _init_menu(self):
        """
        Inicializa el menú principal del juego (MenuManager).
        """
        self.menu = MenuManager(self.state)

    def _init_effects(self, perf_log):
        """
        Inicializa los sistemas de efectos (EffectsManager: combate, explosiones, etc.).
        """
        self.effects = EffectsManager(self.state, perf_log, self.ecs.ecs_world)

    def _init_minimap(self):
        """
        Inicializa el minimapa (Minimap).
        """
        self.minimap = Minimap()

    # -----------------------------------------------------------------------------------
    # Métodos para manejo de loop: eventos, update, render, ECS, etc.
    # -----------------------------------------------------------------------------------

    @benchmark(lambda self: self.perf_log, "1.TOTAL: HANDLE EVENTS")
    def handle_events(self):
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

    @benchmark(lambda self: self.perf_log, "2.TOTAL UPDATE")
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

    @benchmark(lambda self: self.perf_log, "3.TOTAL RENDER")
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

    @benchmark(lambda self: self.perf_log, "4.TOTAL ECS")
    def run_ecs(self):
        self.update_ecs()
        self.render_ecs()

    # -----------------------------------------------------------------------------------
    # Bucle principal del juego
    # -----------------------------------------------------------------------------------

    def run(self):
        """
        Bucle principal del juego: maneja eventos, actualiza lógica y renderiza cada frame.
        """
        while self.state.running:
            # 1) Procesar entrada
            self.handle_events()

            # 2) Actualizar resto de partes del juego que no son ECS (menú, HUD, etc.)
            self.update()

            # 3) Renderizar resto de partes del juego que no son ECS (mapa, editores, buildings, minimapa)
            self.render()

            # 4) Renderizar ECS (solo si no estamos en Map Editor)
            if not self.map_editor.editor_state.active:
                self.run_ecs()

            # 5) Actualizar pantalla
            pygame.display.flip()

            # 6) Actualizar título con FPS actuales
            fps = self.clock.get_fps()
            pygame.display.set_caption(f"Roguelike - FPS: {fps:0.1f}")

            # 7) Autosave periódico según configuración
            if (
                self.world.config.autosave_enabled
                and time.time() - self._last_autosave_time
                >= self.world.config.autosave_interval
            ):
                self.world.save_world()
                self._last_autosave_time = time.time()

            # 8) Limitar velocidad de fotogramas
            self.clock.tick(config.FPS)

    # -----------------------------------------------------------------------------------
    # Método de cierre / guardado antes de salir
    # -----------------------------------------------------------------------------------

    def shutdown(self):
        """
        Se encarga de todo lo necesario antes de cerrar el juego:
         - Guardar posición del jugador en el mapa actual.
         - Actualizar WorldManager (maps, current_level, etc.).
         - Serializar y guardar el mundo en disco.
        """
        try:
            # 1) Obtener la entidad del jugador
            eid = self.ecs.ecs_world.player_entity
            pos = self.ecs.ecs_world.components["Position"][eid]

            # 2) Calcular coordenadas de tile usando el centro del collider 'feet'
            from roguelike_game.config_player import RENDERED_SPRITE_SIZE
            from roguelike_engine.config.config_tiles import TILE_SIZE

            w, h = RENDERED_SPRITE_SIZE
            fh = h // 4
            half_fh = fh // 2

            feet_cx = pos.x + w // 2
            feet_cy = pos.y + (h - half_fh)

            tx = int(feet_cx // TILE_SIZE)
            ty = int(feet_cy // TILE_SIZE)

            # 3) Hacer spawn del jugador en el mapa (para que guarde la nueva posición)
            self.map.spawn_player((tx, ty))

            # 4) Actualizar WorldManager
            self.world.maps[self.map.name] = self.map
            self.world.current_level = self.map.name

            # 5) Salvar el mundo en disco
            self.world.save_world()

        except Exception as exc:
            # Si ocurre un error al guardar, lo imprimimos pero no interrumpimos el cierre
            print(f"[WARN] No se pudo guardar la posición al cerrar: {exc}")


# ---------------------------------------------------------------------------------------
# Punto de entrada si ejecutamos este archivo directamente
# ---------------------------------------------------------------------------------------
if __name__ == "__main__":
    pygame.init()
    screen = pygame.display.set_mode((config.SCREEN_WIDTH, config.SCREEN_HEIGHT))

    # Creamos la instancia de Game: internamente llamará a _initialize(...)
    game = Game(screen)

    # Ejecutamos el bucle principal
    game.run()

    # Al salir, cerramos pygame
    pygame.quit()
