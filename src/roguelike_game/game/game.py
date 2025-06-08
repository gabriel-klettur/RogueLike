# Path: src/roguelike_game/game/game.py

import pygame
import time
import os
from pathlib import Path
from datetime import datetime
import logging
from typing import Callable
from functools import partial
import cProfile
import pstats

#!---------------------- Paquetes locales: configuración --------------------------------
import roguelike_engine.config.config as config
from roguelike_engine.config.config_tiles import TILE_SIZE
from roguelike_game.ecs.factories.player.config import RENDERED_SPRITE_SIZE
from roguelike_engine.config.map_config import global_map_settings

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
from roguelike_game.ecs.systems.rendering.render_system import RenderSystem


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
        """
        Constructor de Game. Solo se encarga de recibir los parámetros
        e invocar al método privado _initialize.
        """
        # Guarda etapas personalizadas para carga estándar y de sistemas
        self.extra_stages = extra_stages or []
        self.extra_systems_stages = extra_systems_stages or []
        # Inicializa loader tempranamente para evitar AttributeError
        self.loader = LoadingScreen(screen, loading_bg)
        # Configura logs de tiempos de inicialización
        logs_dir = Path('logs')
        logs_dir.mkdir(exist_ok=True)
        # Timestamp formateado con guiones para fecha y hora
        timestamp = datetime.now().strftime("%Y-%m-%d_%H-%M-%S")
        self.stage_log_path = logs_dir / f'stage_times_{timestamp}.log'
        with open(self.stage_log_path, 'w', encoding='utf-8') as f:
            f.write(f"[{datetime.now().isoformat()}] Inicio de inicialización\n")
        # Configurar logging para capturar sub-stages en el mismo archivo de etapas
        logging.basicConfig(
            filename=str(self.stage_log_path),
            filemode='a',
            format='%(asctime)s %(message)s',
            datefmt='[%Y-%m-%dT%H:%M:%S]',
            level=logging.INFO
        )
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
        # Registro de inicio de stages
        with open(self.stage_log_path, 'a', encoding='utf-8') as log_file:
            log_file.write(f"[{datetime.now().isoformat()}] Pipeline de etapas:\n")
        # Construye pipeline dinámico de stages
        stages: list[tuple[str, Callable]] = []
        # Sistemas iniciales
        stages.append(("Pantalla, reloj y fuente", partial(self._setup_display, screen, perf_log)))
        stages.append(("Mundo (sin estado)", partial(self._setup_world)))
        # Carga de estado mundial
        stages.append(("Cargando estado de mundo", partial(self._load_world_state)))
        # Resto de sistemas
        stages.append(("Creando loader", partial(self._create_loader, loading_bg)))
        for msg, func in self.extra_systems_stages:
            stages.append((msg, func))
        # Etapas por defecto
        default_stages = [
            ("Inicializando estado Principal", partial(self._init_state)),
            ("Cargando mapa", partial(self._init_map, map_name)),
            ("Cargando edificios", partial(self._init_buildings)),
            ("Cargando Z-layer", partial(self._init_z_layer)),
            ("Cargando editor de edificios", partial(self._init_buildings_editor)),
            ("Cargando editor de tiles", partial(self._init_tile_editor)),
            ("Cargando editor de mapa", partial(self._init_map_editor)),
            ("Cargando minimapa", partial(self._init_minimap)),
            ("Inicializando ECS", partial(self._init_ecs, screen, perf_log)),
            ("Inicializando renderizador", partial(self._init_renderer)),
            ("Inicializando menú", partial(self._init_menu)),
            ("Inicializando efectos", partial(self._init_effects, perf_log)),
        ]
        for msg, func in default_stages:
            stages.append((msg, func))
        # Etapas extras definidas por usuario
        for msg, func in self.extra_stages:
            stages.append((msg, func))
        # Ejecución y registro de tiempos
        total = len(stages)
        with open(self.stage_log_path, 'a', encoding='utf-8') as log_file:
            for i, (msg, func) in enumerate(stages):
                start_t = time.time()
                func()
                elapsed = time.time() - start_t
                fraction = (i + 1) / total
                self.loader.draw(fraction, msg)
                # Removed plain log to avoid duplication; using detailed logging instead
                # Log sub-stage with function for class introspection
                func_base = getattr(func, 'func', func)
                func_name = getattr(func_base, '__qualname__', getattr(func_base, '__name__', str(func_base)))
                logging.info(f"[StageDetail] {msg}: {elapsed:.4f}s [Clases: {func_name}]")
                # Deserializar niveles diferidos justo tras cargar el estado mundial
                if msg == "Cargando estado de mundo":
                    for lvl in list(getattr(self.world, '_pending_levels', [])):
                        # Extraer estado serializado sin cambiar current_level
                        state = self.world._pending_levels.pop(lvl)
                        # 1) Construir mapa
                        t_build = time.time()
                        mgr = MapManager(lvl)
                        build_elapsed = time.time() - t_build
                        self.loader.draw(fraction, f"Construyendo nivel {lvl}")
                        logging.info(f"[StageDetail] Construyendo nivel {lvl}: {build_elapsed:.4f}s [Clases: MapManager]")
                        # 2) Aplicar estado guardado
                        t_state = time.time()
                        mgr.deserialize_state(state)
                        state_elapsed = time.time() - t_state
                        self.loader.draw(fraction, f"Aplicando estado nivel {lvl}")
                        logging.info(f"[StageDetail] Aplicando estado nivel {lvl}: {state_elapsed:.4f}s [Clases: MapManager.deserialize_state]")
                        # Registrar nivel en WorldManager en memoria
                        self.world.maps[lvl] = mgr

    # -----------------------------------------------------------------------------------
    # Métodos _init_*: cada uno se encarga de inicializar una parte del Game
    # -----------------------------------------------------------------------------------

    def _setup_display(self, screen, perf_log):
        self.screen = screen
        self.clock = pygame.time.Clock()
        self.font = pygame.font.SysFont(config.FONT_NAME, config.FONT_SIZE)
        self.camera = Camera(config.SCREEN_WIDTH, config.SCREEN_HEIGHT)
        self.z_state = ZState()
        self.perf_log = perf_log

    def _setup_world(self):
        # Inicializa WorldManager sin cargar estado para acelerar init
        self.world = WorldManager(WORLD_CONFIG, load_state_on_init=False)
        self._last_autosave_time = time.time()

    def _load_world_state(self):
        """
        Carga el estado mundial guardado desde disco de forma separada
        """
        try:
            self.world.load_world()
        except FileNotFoundError as e:
            print(f"[Game._load_world_state] No se encontró archivo de estado mundial: {e}, iniciando nuevo mundo sin cargar")

    def _create_loader(self, loading_bg):
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

    def _init_z_layer(self):
        """
        Inicializa el gestor de capas Z y asigna las capas a las entidades.
        """
        self.zlayer = ZLayerManager(self.z_state)
        # Usar self.buildings, ya inicializado en _init_buildings
        self.zlayer.initialize(self.state, self.buildings)

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
        # Profile ECSManager initialization
        profile = cProfile.Profile()
        profile.enable()
        t0 = time.perf_counter()
        self.ecs = ECSManager(screen, self.map, self.buildings, perf_log)
        elapsed = time.perf_counter() - t0
        profile.disable()
        # Dump profiling stats for ECS init
        logs_dir = Path('logs'); logs_dir.mkdir(exist_ok=True)
        profile_log = logs_dir / f'ecs_init_profile_{datetime.now().strftime("%Y%m%d_%H%M%S")}.log'
        with open(profile_log, 'w') as pf:
            stats = pstats.Stats(profile, stream=pf)
            stats.sort_stats('tottime').print_stats(30)
        logging.info(f"[Profiling] _init_ecs: {elapsed:.4f}s stats -> {profile_log}")

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
        # Si estamos en un editor activo, delegamos solo a su handler y bloqueamos ataques/spells
        if self.tiles_editor.editor_state.active:
            self.tiles_editor.handle(self.camera, self.map)
            return
        if self.buildings_editor.editor_state.active:
            self.buildings_editor.handle(self.camera, self.buildings)
            return
        if self.map_editor.editor_state.active:
            self.map_editor.handle(self.camera, self.map)
            return
        # Modo normal: procesar eventos de juego (ataques, spells, dash, etc.)
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

            # 4) Renderizar ECS (solo si no estamos en ningún editor)
            if not (
                self.tiles_editor.editor_state.active or
                self.buildings_editor.editor_state.active or
                self.map_editor.editor_state.active
            ):
                self.run_ecs()

            # 5) Actualizar pantalla
            # Aplicar escala de grises completa si hubo muerte
            if self.ecs.ecs_world.components.get('GrayscaleComponent'):
                RenderSystem(self.screen).apply_grayscale(self.screen)
                # Dibujar overlay de resurrección en lobby
                map_mgr = self.ecs.ecs_world.map_manager
                lob_x, lob_y = map_mgr.lobby_offset
                cw = global_map_settings.zone_width
                ch = global_map_settings.zone_height
                center_tx = lob_x + cw // 2
                center_ty = lob_y + ch // 2
                # Calcular coordenadas de pantalla del overlay
                world_x = (center_tx - 1) * TILE_SIZE
                world_y = (center_ty - 1) * TILE_SIZE
                x0, y0 = self.camera.apply((world_x, world_y))
                w = TILE_SIZE * 3
                h = TILE_SIZE * 3
                overlay = pygame.Surface((w, h), pygame.SRCALPHA)
                overlay.fill((255, 255, 0, 80))
                self.screen.blit(overlay, (x0, y0))
                pygame.draw.rect(self.screen, (255, 255, 0), pygame.Rect(x0, y0, w, h), 3)
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
