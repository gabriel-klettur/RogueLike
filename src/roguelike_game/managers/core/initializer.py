import time
import logging
from pathlib import Path
from functools import partial
from datetime import datetime
import cProfile, pstats

import pygame
import roguelike_engine.config.config as config
from roguelike_engine.camera.camera import Camera
from roguelike_engine.utils.loading_screen import LoadingScreen
from roguelike_engine.world.world import WorldManager
from roguelike_engine.world.world_config import WORLD_CONFIG

from roguelike_game.config.input_config import InputConfig
from roguelike_game.managers.core.state import GameState
from roguelike_game.managers.core.render_manager import RendererManager
from roguelike_game.managers.map import MapManager
from roguelike_game.managers.buildings import BuildingsManager
from roguelike_game.managers.z_layer import ZLayerManager
from types import SimpleNamespace

from roguelike_game.managers.menu import MenuManager
from roguelike_game.managers.class_selector_manager import ClassSelectorManager
from roguelike_game.managers.editors.buildings_editor_manager import BuildingEditorManager
from roguelike_game.managers.editors.tiles_editor_manager import TilesEditorManager
from roguelike_game.managers.editors.map_editor_manager import MapEditorManager
from roguelike_game.managers.editors.entities_editor_manager import EntitiesEditorManager
from roguelike_game.managers.editors.spells_editor_manager import SpellsEditorManager
from roguelike_game.managers.editors.items_editor_manager import ItemsEditorManager
from roguelike_game.managers.editors.inventory_editor_manager import InventoryEditorManager
from roguelike_game.managers.editors.inventory_editor_manager import InventoryEditorManager
        
from roguelike_engine.minimap.minimap import Minimap
from roguelike_engine.z_layer.state import ZState
from roguelike_game.managers.ecs import ECSManager
from roguelike_game.managers.items.loader import ItemsLoader


class GameInitializer:
    def __init__(self, game, screen, perf_log, map_name, loading_bg,
                 extra_stages, extra_systems_stages):
        self.game                   = game
        self.screen                 = screen
        self.perf_log               = perf_log
        self.map_name               = map_name
        self.loading_bg             = loading_bg
        self.extra_stages           = extra_stages
        self.extra_systems_stages   = extra_systems_stages

        # Preparar archivo de log de etapas
        logs_dir = Path('logs')
        logs_dir.mkdir(exist_ok=True)
        ts = datetime.now().strftime("%Y-%m-%d_%H-%M-%S")
        self.stage_log_path = logs_dir / f'stage_times_{ts}.log'
        with open(self.stage_log_path, 'w', encoding='utf-8') as f:
            f.write(f"[{datetime.now().isoformat()}] Inicio de inicialización\n")
        logging.basicConfig(
            filename=str(self.stage_log_path),
            filemode='a',
            format='%(asctime)s %(message)s',
            datefmt='[%Y-%m-%dT%H:%M:%S]',
            level=logging.INFO
        )

    def initialize(self):
        self.game.loader = LoadingScreen(self.screen, self.loading_bg)

        # Armar pipeline
        stages = []
        stages.append(("Pantalla, reloj y fuente", partial(self._setup_display)))
        stages.append(("Mundo (sin estado)"       , partial(self._setup_world)))
        stages.append(("Cargando estado de mundo" , partial(self._load_world_state)))
        stages.append(("Creando loader"           , partial(self._create_loader)))

        for msg, fn in self.extra_systems_stages:
            stages.append((msg, fn))

        defaults = [
            ("Inicializando estado Principal"   , partial(self._init_state)),
            ("Cargando mapa"                    , partial(self._init_map)),
            ("Cargando edificios"               , partial(self._init_buildings)),
            ("Inicializando ECS"                , partial(self._init_ecs)),
            ("Cargando catálogo de ítems"       , partial(self._init_items)),
            ("Cargando editor de ítems"         , partial(self._init_item_editor)),
            ("Cargando Z-layer"                 , partial(self._init_z_layer)),
            ("Cargando editor de edificios"     , partial(self._init_buildings_editor)),
            ("Cargando editor de tiles"         , partial(self._init_tile_editor)),
            ("Cargando editor de mapa"          , partial(self._init_map_editor)),
            ("Cargando editor de inventario"    , partial(self._init_inventory_editor)),
            ("Cargando editor de entidades"     , partial(self._init_entities_editor)),
                ("Cargando editor de hechizos"      , partial(self._init_spells_editor)),
            ("Cargando minimapa"                , partial(self._init_minimap)),

            ("Inicializando renderizador"       , partial(self._init_renderer)),
            ("Inicializando menú"               , partial(self._init_menu))            
        ]
        stages.extend(defaults)

        for msg, fn in self.extra_stages:
            stages.append((msg, fn))

        total = len(stages)
        for i, (msg, fn) in enumerate(stages):
            t0 = time.time()
            fn()
            elapsed = time.time() - t0
            frac = (i + 1) / total
            self.game.loader.draw(frac, msg)

            base = getattr(fn, 'func', fn)
            name = getattr(base, '__qualname__',
                           getattr(base, '__name__', str(base)))
            logging.info(f"[StageDetail] {msg}: {elapsed:.4f}s [{name}]")

            if msg == "Cargando estado de mundo":
                self._handle_deferred_levels()


    # ——— Stages ——————————————————————————————————————————————————————————————

    def _setup_display(self):
        g = self.game
        g.clock    = pygame.time.Clock()
        g.font     = pygame.font.SysFont(config.FONT_NAME, config.FONT_SIZE)
        g.camera   = Camera(config.SCREEN_WIDTH, config.SCREEN_HEIGHT)
        g.z_state  = ZState()
        g.perf_log = self.perf_log

    def _setup_world(self):
        g = self.game
        g.world             = WorldManager(WORLD_CONFIG, load_state_on_init=False)
        g._last_autosave_time = time.time()

    def _load_world_state(self):
        try:
            self.game.world.load_world()
        except Exception as e:
            print(f"[GameInitializer] Error al cargar mundo: {e}")

    def _handle_deferred_levels(self):
        g = self.game
        for lvl in list(getattr(g.world, '_pending_levels', [])):
            state = g.world._pending_levels.pop(lvl)
            mgr   = MapManager(lvl)
            mgr.deserialize_state(state)
            g.world.maps[lvl] = mgr

    def _create_loader(self):
        self.game.loader = LoadingScreen(self.screen, self.loading_bg)

    def _init_state(self):
        self.game.state = GameState()

    def _init_map(self):
        g = self.game
        if g.world.current_level:
            g.world.load_level(g.world.current_level)
            g.map = g.world.maps[g.world.current_level]
        else:
            g.map = MapManager(self.map_name)
            g.world.maps[g.map.name] = g.map
            g.world.current_level = g.map.name

    def _init_buildings(self):
        self.game.buildings = BuildingsManager(self.game.z_state, self.game.map)

    def _init_z_layer(self):
        z = ZLayerManager(self.game.z_state)
        entities = SimpleNamespace(
            player=self.game.ecs.ecs_world.player_position,
            buildings=self.game.buildings.buildings
        )
        z.initialize(self.game.state, entities)
        self.game.zlayer = z
        self.game.entities = entities

    def _init_buildings_editor(self):
        self.game.buildings_editor = BuildingEditorManager(self.game)

    def _init_tile_editor(self):
        self.game.tiles_editor = TilesEditorManager(self.game)

    def _init_map_editor(self):
        self.game.map_editor = MapEditorManager(self.game)

    def _init_inventory_editor(self):        
        self.game.inventory_editor = InventoryEditorManager(self.game)

    def _init_entities_editor(self):                
        self.game.entities_editor = EntitiesEditorManager(self.game)

    def _init_spells_editor(self):
        self.game.spells_editor = SpellsEditorManager(self.game)

    def _init_minimap(self):
        self.game.minimap = Minimap()

    def _init_ecs(self):
        g = self.game
        pr = cProfile.Profile()
        pr.enable()
        t0 = time.perf_counter()
        g.ecs = ECSManager(self.screen, g.map, g.buildings, self.perf_log)
        g.ecs.ecs_world.state = g.state
        elapsed = time.perf_counter() - t0
        pr.disable()

        logf = Path('logs') / f'ecs_init_profile_{datetime.now().strftime("%Y%m%d_%H%M%S")}.log'
        with open(logf, 'w') as pf:
            p = pstats.Stats(pr, stream=pf)
            p.sort_stats('tottime').print_stats(30)
        logging.info(f"[Profiling] ECS init: {elapsed:.4f}s -> {logf}")

    def _init_items(self):
        """Carga catálogo de ítems y assets de ítems para todo el juego"""
        # Delegar carga de datos y assets a ItemsLoader
        loader = ItemsLoader()
        items, assets = loader.load()
        self.game.items = items
        self.game.item_assets = assets


    def _init_item_editor(self):        
        self.game.item_editor = ItemsEditorManager(self.game)

    def _init_renderer(self):
        g = self.game
        if not hasattr(g, 'map_editor'):
            self._init_map_editor()
        g.renderer = RendererManager(
            g.screen,
            g.camera,
            g.map,
            g.entities,
            g.buildings_editor,
            g.tiles_editor,
            g.map_editor,
            g.perf_log,
            g.minimap,
            g.ecs
        )

    def _init_menu(self):
        g = self.game
        g.input_config = InputConfig()
        g.menu = MenuManager(g.state, g.screen, g.input_config)
        g.class_selector = ClassSelectorManager(g.state, g.input_config, g.screen)