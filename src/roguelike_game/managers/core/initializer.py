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
from roguelike_game.config.audio_config import AudioConfig
from roguelike_game.managers.core.state import GameState
from roguelike_game.managers.core.render_manager import RendererManager
from roguelike_game.managers.map import MapManager
from roguelike_game.managers.buildings import BuildingsManager
from roguelike_game.managers.z_layer import ZLayerManager
from types import SimpleNamespace
from roguelike_engine.console.console_model import ConsoleState, CommandRegistry
from roguelike_engine.console.console_controller import ConsoleController
from roguelike_engine.console.console_events import ConsoleEvents
from roguelike_engine.console.console_view import ConsoleView
from roguelike_engine.console import register_commands

from roguelike_game.managers.menu import MenuManager
from roguelike_game.managers.player.class_selector_manager import ClassSelectorManager
from roguelike_game.managers.player.player_manager import PlayerManager
from roguelike_game.managers.editors.buildings_editor_manager import BuildingEditorManager
from roguelike_game.managers.editors.tiles_editor_manager import TilesEditorManager
from roguelike_game.managers.editors.map_editor_manager import MapEditorManager
from roguelike_game.managers.editors.entities_editor_manager import EntitiesEditorManager
from roguelike_game.managers.editors.spells_editor_manager import SpellsEditorManager
from roguelike_game.managers.editors.items_editor_manager import ItemsEditorManager
from roguelike_game.managers.editors.inventory_editor_manager import InventoryEditorManager
from roguelike_game.managers.editors.spawner_editor_manager import SpawnerEditorManager
from roguelike_game.managers.editors.particles_editor_manager import ParticlesEditorManager
        
from roguelike_engine.minimap import Minimap
from roguelike_engine.z_layer.state import ZState
from roguelike_game.managers.ecs import ECSManager
from roguelike_game.managers.items.loader import ItemsLoader
from roguelike_game.managers.core.audio_manager import AudioManager
from roguelike_engine.audio.service import AudioService, AudioBus
from roguelike_engine.audio.config import load_audio_catalog

import logging
logger = logging.getLogger(__name__)

class GameInitializer:

    @classmethod
    def create_and_initialize(
        cls,
        game,
        screen,
        perf_log=None,
        map_name: str = None,
        loading_bg: str | None = None,
        extra_stages: list[tuple] | None = None,
        extra_systems_stages: list[tuple] | None = None
    ) -> "GameInitializer":
        """
        Fábrica estática que construye y ejecuta la inicialización completa.
        """
        inst = cls(
            game=game,
            screen=screen,
            perf_log=perf_log,
            map_name=map_name,
            loading_bg=loading_bg,
            extra_stages=extra_stages,
            extra_systems_stages=extra_systems_stages
        )
        inst.initialize()
        return inst

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
        # Añadir FileHandler para log de etapas
        fh = logging.FileHandler(self.stage_log_path, mode='a', encoding='utf-8')
        fh.setLevel(logging.INFO)
        fh.setFormatter(logging.Formatter('%(asctime)s %(message)s', datefmt='[%Y-%m-%dT%H:%M:%S]'))
        logging.getLogger().addHandler(fh)

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

        # Reordenado: inicializar audio temprano para que la música suene durante la carga
        defaults = [
            ("Inicializando audio"              , partial(self._init_audio)),
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
            ("Cargando editor de partículas"    , partial(self._init_particles_editor)),
            ("Cargando editor de spawner"       , partial(self._init_spawner_editor)),
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
            logger.info(f"[{name}]: {msg}: {elapsed:.4f}s")

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
            logger.error(f"Error al cargar mundo: {e}")

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
        # Consola quake-like
        self.game.console_state = ConsoleState()
        self.game.command_registry = CommandRegistry()
        register_commands(self.game.command_registry, self.game)
        self.game.console_controller = ConsoleController(self.game.console_state, self.game.command_registry)
        self.game.console_events = ConsoleEvents(self.game.console_controller)
        screen_w, screen_h = self.game.screen.get_size()
        console_h = screen_h // 3
        console_rect = pygame.Rect(0, screen_h - console_h, screen_w, console_h)
        self.game.console_view = ConsoleView(self.game.console_state, console_rect)

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

    def _init_spawner_editor(self):
        self.game.spawner_editor = SpawnerEditorManager(self.game)

    def _init_particles_editor(self):
        self.game.particles_editor = ParticlesEditorManager(self.game)

    def _init_minimap(self):
        self.game.minimap = Minimap()

    def _init_ecs(self):
        g = self.game
        pr = cProfile.Profile()
        pr.enable()        
        g.ecs = ECSManager(self.screen, g.map, g.buildings, self.perf_log)
        g.ecs.ecs_world.state = g.state        
        pr.disable()
        logf = Path('logs') / f'ecs_init_profile_{datetime.now().strftime("%Y%m%d_%H%M%S")}.log'
        with open(logf, 'w') as pf:
            p = pstats.Stats(pr, stream=pf)
            p.sort_stats('tottime').print_stats(30)
        # Inyectar snapshot de inventarios de NPCs (si fue cargado desde el save)
        try:
            snap = getattr(g.world, 'npc_inventories', None) or {}
            if snap:
                g.ecs.ecs_world.components['NPCInventorySnapshot'] = dict(snap)
        except Exception:
            pass

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
        # Audio config + manager (reutilizar si ya fueron creados en _init_audio)
        if not hasattr(g, 'audio_config') or g.audio_config is None:
            g.audio_config = AudioConfig()
        if not hasattr(g, 'audio_manager') or g.audio_manager is None:
            g.audio_manager = AudioManager(g.audio_config)
        g.menu = MenuManager(
            g, g.state, g.screen, g.input_config,
            audio_config=g.audio_config,
            audio_manager=g.audio_manager,
            audio_bus=getattr(g, 'audio_bus', None),
            font_size=18,
        )
        # Si la música de intro ya venía sonando (arrancada temprano), avisar al menú para no reiniciarla
        try:
            if getattr(g, 'menu_music_prestarted', False):
                setattr(g.menu, '_music_already_playing_externally', True)
        except Exception:
            pass
        # Pasar parámetros de FX de startup al menú (flash y carrusel)
        try:
            fx = getattr(g, 'startup_ui_fx', {}) or {}
            g.menu._startup_flash_enabled = bool(fx.get('flash_enabled', True))
            g.menu._startup_flash_at_s = float(fx.get('flash_at_s', 6.0))
            g.menu._startup_flash_duration_s = float(fx.get('flash_duration_s', 0.25))
            g.menu._startup_flash_color_rgba = tuple(fx.get('flash_color_rgba', (255, 255, 255, 255)))
            g.menu._startup_flash_ease = str(fx.get('flash_ease', 'linear'))
            g.menu._startup_flash_trigger = str(fx.get('flash_trigger', 'time'))
            g.menu._startup_enable_cycle_after_flash = bool(fx.get('enable_carousel_after_flash', True))
            g.menu._startup_block_cycle_until_flash = bool(fx.get('block_carousel_until_flash', True))
            g.menu._startup_fade_in_ms = int(fx.get('fade_in_ms', 300))
            # Ajustar loop para el caso en que el menú deba iniciar música por sí mismo
            try:
                g.menu._music_loop = bool(fx.get('loop', True))
            except Exception:
                pass
            # Habilitar ciclo inmediato si no se bloquea hasta el destello o si el destello está deshabilitado
            if (not g.menu._startup_block_cycle_until_flash) or (not g.menu._startup_flash_enabled):
                g.menu._bg_cycle_enabled = True
        except Exception:
            pass
        # Configurar carrusel de fondos del menú principal (pantalla de inicio)
        try:
            g.menu.set_backgrounds([
                "assets/ui/intro/Intro_elven.png",                
                "assets/ui/intro/Intro_drwaft.png",                
                "assets/ui/intro/intro_mague.png",
                "assets/ui/intro/Intro_valkyrie.png",
                "assets/ui/intro/Intro_barbarian.png",
            ], interval_s=2.0, transition_s=0.6, slide_px=24)
        except Exception:
            # No bloquear si no existe alguna ruta; el menú seguirá mostrando overlay sin fondo
            pass
        # Configurar música de intro en el menú solo como metadato (no la reproduce si ya suena)
        try:
            # Volumen desde audio_config (o mantener el del bus)
            try:
                mv = float(g.audio_config.get('music'))
            except Exception:
                mv = None
            intro_path = None
            try:
                from roguelike_engine.audio.config import load_audio_catalog
                catalog = load_audio_catalog()
                defaults = catalog.get_default_music() if catalog else {}
                startup_id = (defaults or {}).get('startup_track_id')
                if startup_id:
                    intro_path = catalog.track_path(startup_id)
            except Exception:
                intro_path = None
            g.menu.set_music(intro_path or "assets/audio/music/intro_theme.mp3", loop=True, volume=(mv if mv is not None else g.menu._music_volume))
        except Exception:
            # Silencioso si falla audio o no existe el archivo
            pass
        # Configurar logo del juego (centrado sobre el panel del menú)
        try:
            # Mostrar al tamaño original (solo reducir si supera la pantalla)
            g.menu.set_logo("assets/ui/intro/game_name.png", max_width_ratio=1.0, max_height_ratio=1.0, gap_px=12)
        except Exception:
            pass
        # Activar pantalla previa: "Pulsa para comenzar"
        try:
            # Respetar completamente data/config/intro.json (texto, blink, etc.)
            g.menu.enable_press_to_start()
        except Exception:
            pass
        # Arrancar en menú principal (start)
        try:
            g.menu.set_mode("start")
            g.menu.show_menu = True
        except Exception:
            # Fallback si aún no existe API
            g.menu.mode = "start"
            g.menu.show_menu = True
        g.class_selector = ClassSelectorManager(g.state, g.input_config, g.screen)
        g.player_manager = PlayerManager(g.ecs.ecs_world)

    def _init_audio(self):
        """Inicializa el servicio de audio (hilo) y registra un AudioBus global."""
        g = self.game
        # Asegurar configuración de audio desde el inicio para respetar volúmenes guardados
        try:
            if not hasattr(g, 'audio_config') or g.audio_config is None:
                g.audio_config = AudioConfig()
            if not hasattr(g, 'audio_manager') or g.audio_manager is None:
                g.audio_manager = AudioManager(g.audio_config)
        except Exception:
            pass
        # Crear y arrancar servicio
        try:
            catalog = load_audio_catalog()
        except Exception:
            catalog = None
        svc = AudioService(catalog)
        try:
            svc.start()
        except Exception:
            # Continuar silenciosamente si mixer falla (entornos sin audio)
            pass
        bus = AudioBus(svc)
        # Registrar bus global para acceso opcional
        try:
            from roguelike_engine.audio.api import set_bus as _set_audio_bus
            _set_audio_bus(bus)
        except Exception:
            pass
        # Aplicar volúmenes iniciales (si aún no hay audio_config, usar defaults o el catálogo)
        try:
            mv = float(getattr(getattr(g, 'audio_config', None), 'get', lambda *_: 0.6)('music')) if getattr(g, 'audio_config', None) else 0.6
        except Exception:
            mv = 0.6
        try:
            sv = float(getattr(getattr(g, 'audio_config', None), 'get', lambda *_: 0.7)('sfx')) if getattr(g, 'audio_config', None) else 0.7
        except Exception:
            sv = 0.7
        try:
            av = float(getattr(getattr(g, 'audio_config', None), 'get', lambda *_: 0.6)('ambient')) if getattr(g, 'audio_config', None) else 0.6
        except Exception:
            av = 0.6
        # Si el catálogo define volúmenes por grupo, preferirlos cuando no haya audio_config
        try:
            if getattr(g, 'audio_config', None) is None and catalog is not None and getattr(catalog, 'groups', None):
                mv = float((catalog.groups.get('music') or {}).get('volume', mv))
                sv = float((catalog.groups.get('sfx') or {}).get('volume', sv))
                av = float((catalog.groups.get('ambient') or {}).get('volume', av))
        except Exception:
            pass
        try:
            bus.set_music_volume(mv)
            bus.set_sfx_volume(sv)
            bus.set_ambient_volume(av)
        except Exception:
            pass
        # Exponer en game
        g.audio_service = svc
        g.audio_bus = bus
        # Hook para "Aplicar ahora" desde el configurador de sonidos
        try:
            from roguelike_engine.audio.api import set_apply_hook as _set_apply_hook
            def _apply_now():
                try:
                    aq = g.ecs.ecs_world.components.setdefault('AudioEventQueue', [])
                    aq.append({'type': 'reload_audio_catalog'})
                except Exception:
                    pass
            _set_apply_hook(_apply_now)
        except Exception:
            pass
        # Reproducir música de inicio inmediatamente (durante la carga) usando parámetros de audio.json
        try:
            import time as _time
            # Leer configuración de startup desde el catálogo (defaults.startup)
            startup_cfg = {}
            try:
                startup_cfg = (getattr(catalog, 'defaults', {}) or {}).get('startup', {}) or {}
            except Exception:
                startup_cfg = {}
            play_on_boot = bool(startup_cfg.get('play_on_boot', True))
            fade_in_ms = int(startup_cfg.get('fade_in_ms', 300))
            startup_loop = bool(startup_cfg.get('loop', True))
            flash_enabled = bool(startup_cfg.get('flash_enabled', True))
            flash_at_s = float(startup_cfg.get('flash_at_s', 6.0))
            flash_duration_s = float(startup_cfg.get('flash_duration_s', 0.25))
            flash_color_rgba = tuple(startup_cfg.get('flash_color_rgba', [255, 255, 255, 255]))
            flash_ease = str(startup_cfg.get('flash_ease', 'linear'))
            flash_trigger = str(startup_cfg.get('flash_trigger', 'time'))  # 'time' | 'on_menu_show' | 'on_carousel_start'
            enable_cycle_after_flash = bool(startup_cfg.get('enable_carousel_after_flash', True))
            block_cycle_until_flash = bool(startup_cfg.get('block_carousel_until_flash', True))
            # Exponer para el menú/renderer
            g.startup_ui_fx = {
                'flash_at_s': flash_at_s,
                'flash_duration_s': flash_duration_s,
                'flash_enabled': flash_enabled,
                'flash_color_rgba': flash_color_rgba,
                'flash_ease': flash_ease,
                'flash_trigger': flash_trigger,
                'enable_carousel_after_flash': enable_cycle_after_flash,
                'block_carousel_until_flash': block_cycle_until_flash,
                'fade_in_ms': fade_in_ms,
                'loop': startup_loop,
            }
            if play_on_boot:
                startup_id = None
                intro_path = None
                try:
                    defaults = catalog.get_default_music() if catalog else {}
                    startup_id = (defaults or {}).get('startup_track_id')
                except Exception:
                    startup_id = None
                if startup_id:
                    bus.play_music(track_id=startup_id, loop=startup_loop, volume=mv, fade_in_ms=fade_in_ms)
                else:
                    # Fallback directo a ruta conocida
                    intro_path = "assets/audio/music/intro_theme.mp3"
                    bus.play_music(path=intro_path, loop=startup_loop, volume=mv, fade_in_ms=fade_in_ms)
                # Marcar inicio para sincronizar destello y habilitar carrusel más tarde
                g.intro_music_started_at = _time.time()
                g.menu_music_prestarted = True
        except Exception:
            pass