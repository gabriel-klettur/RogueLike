from __future__ import annotations

import cProfile
import logging
import pstats
import time
from datetime import datetime
from pathlib import Path
from types import SimpleNamespace
from typing import Any

import pygame
import roguelike_engine.config.config as config
from roguelike_engine.audio.config import load_audio_catalog
from roguelike_engine.audio.service import AudioBus, AudioService
from roguelike_engine.buildings import auto_importer as _auto_importer
from roguelike_engine.camera.camera import Camera
from roguelike_engine.console import register_commands
from roguelike_engine.console.console_controller import ConsoleController
from roguelike_engine.console.console_events import ConsoleEvents
from roguelike_engine.console.console_model import CommandRegistry, ConsoleState
from roguelike_engine.console.console_view import ConsoleView
from roguelike_engine.log_config import build_log_filepath
from roguelike_engine.minimap import Minimap
from roguelike_engine.world.world import WorldManager
from roguelike_engine.world.world_config import WORLD_CONFIG
from roguelike_engine.z_layer.state import ZState

from roguelike_game.config.audio_config import AudioConfig
from roguelike_game.config.input_config import InputConfig
from roguelike_game.managers.buildings import BuildingsManager
from roguelike_game.managers.core.audio_manager import AudioManager
from roguelike_game.managers.core.render.render_manager import RendererManager
from roguelike_game.managers.ecs import ECSManager
from roguelike_game.managers.editors.buildings_editor_manager import (
    BuildingEditorManager,
)
from roguelike_game.managers.editors.entities_editor_manager import (
    EntitiesEditorManager,
)
from roguelike_game.managers.editors.inventory_editor_manager import (
    InventoryEditorManager,
)
from roguelike_game.managers.editors.items_editor_manager import ItemsEditorManager
from roguelike_game.managers.editors.map_editor_manager import MapEditorManager
from roguelike_game.managers.editors.particles_editor_manager import (
    ParticlesEditorManager,
)
from roguelike_game.managers.editors.spawner_editor_manager import (
    SpawnerEditorManager,
)
from roguelike_game.managers.editors.spells_editor_manager import (
    SpellsEditorManager,
)
from roguelike_game.managers.editors.tiles_editor_manager import TilesEditorManager
from roguelike_game.managers.items.loader import ItemsLoader
from roguelike_game.managers.map import MapManager
from roguelike_game.managers.menu import MenuManager
from roguelike_game.managers.player.class_selector_manager import (
    ClassSelectorManager,
)
from roguelike_game.managers.player.player_manager import PlayerManager
from roguelike_game.managers.z_layer import ZLayerManager
from roguelike_game.managers.core.state import GameState
from roguelike_engine.utils.loading_screen import LoadingScreen

from .types import InitContext

logger = logging.getLogger(__name__)


# ——— Stages ——————————————————————————————————————————————————————————————

def setup_display(ctx: InitContext) -> None:
    g = ctx.game
    g.clock = pygame.time.Clock()
    g.font = pygame.font.SysFont(config.FONT_NAME, config.FONT_SIZE)
    g.camera = Camera(config.SCREEN_WIDTH, config.SCREEN_HEIGHT)
    g.z_state = ZState()
    g.perf_log = ctx.perf_log


def setup_world(ctx: InitContext) -> None:
    g = ctx.game
    g.world = WorldManager(WORLD_CONFIG, load_state_on_init=False)
    g._last_autosave_time = time.time()


def load_world_state(ctx: InitContext) -> None:
    try:
        ctx.game.world.load_world()
    except Exception as e:
        logger.error(f"Error al cargar mundo: {e}")


def handle_deferred_levels(ctx: InitContext) -> None:
    g = ctx.game
    for lvl in list(getattr(g.world, "_pending_levels", [])):
        state = g.world._pending_levels.pop(lvl)
        mgr = MapManager(lvl)
        mgr.deserialize_state(state)
        g.world.maps[lvl] = mgr


def create_loader(ctx: InitContext) -> None:
    ctx.game.loader = LoadingScreen(ctx.screen, ctx.loading_bg)


def init_state(ctx: InitContext) -> None:
    g = ctx.game
    g.state = GameState()
    # Consola quake-like
    g.console_state = ConsoleState()
    g.command_registry = CommandRegistry()
    register_commands(g.command_registry, g)
    g.console_controller = ConsoleController(g.console_state, g.command_registry)
    g.console_events = ConsoleEvents(g.console_controller)
    screen_w, screen_h = g.screen.get_size()
    console_h = screen_h // 3
    console_rect = pygame.Rect(0, screen_h - console_h, screen_w, console_h)
    g.console_view = ConsoleView(g.console_state, console_rect)


def init_map(ctx: InitContext) -> None:
    g = ctx.game
    if g.world.current_level:
        g.world.load_level(g.world.current_level)
        g.map = g.world.maps[g.world.current_level]
    else:
        g.map = MapManager(ctx.map_name)
        g.world.maps[g.map.name] = g.map
        g.world.current_level = g.map.name


def dev_auto_import_buildings(ctx: InitContext) -> None:
    """Escanea assets/buildings y crea nuevas plantillas/instancias si la flag DEV está activa."""
    try:
        if bool(getattr(config, "DEV_AUTO_IMPORT_BUILDINGS", False)):
            try:
                _auto_importer.run(verbose=True)
            except Exception as e:
                logger.warning(f"[AutoImporter] Error al auto-importar: {e}")
    except Exception as e:
        logger.debug(f"[Initializer] Config no disponible para auto-import: {e}")


def init_buildings(ctx: InitContext) -> None:
    ctx.game.buildings = BuildingsManager(ctx.game.z_state, ctx.game.map)


def init_z_layer(ctx: InitContext) -> None:
    g = ctx.game
    z = ZLayerManager(g.z_state)
    entities = SimpleNamespace(
        player=g.ecs.ecs_world.player_position,
        buildings=g.buildings.buildings,
    )
    z.initialize(g.state, entities)
    g.zlayer = z
    g.entities = entities


def init_buildings_editor(ctx: InitContext) -> None:
    ctx.game.buildings_editor = BuildingEditorManager(ctx.game)


def init_tile_editor(ctx: InitContext) -> None:
    ctx.game.tiles_editor = TilesEditorManager(ctx.game)


def init_map_editor(ctx: InitContext) -> None:
    ctx.game.map_editor = MapEditorManager(ctx.game)


def init_inventory_editor(ctx: InitContext) -> None:
    ctx.game.inventory_editor = InventoryEditorManager(ctx.game)


def init_entities_editor(ctx: InitContext) -> None:
    ctx.game.entities_editor = EntitiesEditorManager(ctx.game)


def init_spells_editor(ctx: InitContext) -> None:
    ctx.game.spells_editor = SpellsEditorManager(ctx.game)


def init_spawner_editor(ctx: InitContext) -> None:
    ctx.game.spawner_editor = SpawnerEditorManager(ctx.game)


def init_particles_editor(ctx: InitContext) -> None:
    ctx.game.particles_editor = ParticlesEditorManager(ctx.game)


def init_minimap(ctx: InitContext) -> None:
    ctx.game.minimap = Minimap()


def init_ecs(ctx: InitContext) -> None:
    g = ctx.game
    pr = cProfile.Profile()
    pr.enable()
    g.ecs = ECSManager(ctx.screen, g.map, g.buildings, ctx.perf_log)
    g.ecs.ecs_world.state = g.state
    pr.disable()
    logf = build_log_filepath(
        "ecs_init_profile", directory="logs/profile", extension="log", now_dt=ctx.ts_dt
    )
    with open(logf, "w") as pf:
        p = pstats.Stats(pr, stream=pf)
        p.sort_stats("tottime").print_stats(30)
    # Inyectar snapshot de inventarios de NPCs (si fue cargado desde el save)
    try:
        snap = getattr(g.world, "npc_inventories", None) or {}
        if snap:
            g.ecs.ecs_world.components["NPCInventorySnapshot"] = dict(snap)
    except Exception:
        pass


def init_items(ctx: InitContext) -> None:
    """Carga catálogo de ítems y assets de ítems para todo el juego"""
    loader = ItemsLoader()
    items, assets = loader.load()
    ctx.game.items = items
    ctx.game.item_assets = assets


def init_item_editor(ctx: InitContext) -> None:
    ctx.game.item_editor = ItemsEditorManager(ctx.game)


def init_renderer(ctx: InitContext) -> None:
    g = ctx.game
    if not hasattr(g, "map_editor"):
        init_map_editor(ctx)
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
        g.ecs,
    )


def init_menu(ctx: InitContext) -> None:
    g = ctx.game
    g.input_config = InputConfig()
    # Audio config + manager (reutilizar si ya fueron creados en init_audio)
    if not hasattr(g, "audio_config") or g.audio_config is None:
        g.audio_config = AudioConfig()
    if not hasattr(g, "audio_manager") or g.audio_manager is None:
        g.audio_manager = AudioManager(g.audio_config)
    g.menu = MenuManager(
        g,
        g.state,
        g.screen,
        g.input_config,
        audio_config=g.audio_config,
        audio_manager=g.audio_manager,
        audio_bus=getattr(g, "audio_bus", None),
        font_size=18,
    )
    # Si la música de intro ya venía sonando (arrancada temprano), avisar al menú para no reiniciarla
    try:
        if getattr(g, "menu_music_prestarted", False):
            setattr(g.menu, "_music_already_playing_externally", True)
    except Exception:
        pass
    # Pasar parámetros de FX de startup al menú (flash y carrusel)
    try:
        fx = getattr(g, "startup_ui_fx", {}) or {}
        g.menu._startup_flash_enabled = bool(fx.get("flash_enabled", True))
        g.menu._startup_flash_at_s = float(fx.get("flash_at_s", 6.0))
        g.menu._startup_flash_duration_s = float(fx.get("flash_duration_s", 0.25))
        g.menu._startup_flash_color_rgba = tuple(
            fx.get("flash_color_rgba", (255, 255, 255, 255))
        )
        g.menu._startup_flash_ease = str(fx.get("flash_ease", "linear"))
        g.menu._startup_flash_trigger = str(
            fx.get("flash_trigger", "time")
        )  # 'time' | 'on_menu_show' | 'on_carousel_start'
        g.menu._startup_enable_cycle_after_flash = bool(
            fx.get("enable_carousel_after_flash", True)
        )
        g.menu._startup_block_cycle_until_flash = bool(
            fx.get("block_carousel_until_flash", True)
        )
        g.menu._startup_fade_in_ms = int(fx.get("fade_in_ms", 300))
        try:
            g.menu._music_loop = bool(fx.get("loop", True))
        except Exception:
            pass
        if (not g.menu._startup_block_cycle_until_flash) or (
            not g.menu._startup_flash_enabled
        ):
            g.menu._bg_cycle_enabled = True
    except Exception:
        pass
    # Configurar carrusel de fondos del menú principal (pantalla de inicio)
    try:
        g.menu.set_backgrounds(
            [
                "assets/ui/intro/Intro_elven.png",
                "assets/ui/intro/Intro_drwaft.png",
                "assets/ui/intro/intro_mague.png",
                "assets/ui/intro/Intro_valkyrie.png",
                "assets/ui/intro/Intro_barbarian.png",
            ],
            interval_s=2.0,
            transition_s=0.6,
            slide_px=24,
        )
    except Exception:
        pass
    # Configurar música de intro en el menú solo como metadato (no la reproduce si ya suena)
    try:
        try:
            mv = float(g.audio_config.get("music")) if g.audio_config else 0.6
        except Exception:
            mv = 0.6
        intro_path = None
        try:
            catalog = load_audio_catalog()
            defaults = catalog.get_default_music() if catalog else {}
            startup_id = (defaults or {}).get("startup_track_id")
            if startup_id:
                intro_path = catalog.track_path(startup_id)
        except Exception:
            intro_path = None
        g.menu.set_music(
            intro_path or "assets/audio/music/intro_theme.mp3",
            loop=True,
            volume=(mv if mv is not None else g.menu._music_volume),
        )
    except Exception:
        pass
    # Configurar logo del juego (centrado sobre el panel del menú)
    try:
        g.menu.set_logo(
            "assets/ui/intro/game_name.png",
            max_width_ratio=1.0,
            max_height_ratio=1.0,
            gap_px=12,
        )
    except Exception:
        pass
    # Activar pantalla previa: "Pulsa para comenzar"
    try:
        g.menu.enable_press_to_start()
    except Exception:
        pass
    # Arrancar en menú principal (start)
    try:
        g.menu.set_mode("start")
        g.menu.show_menu = True
    except Exception:
        g.menu.mode = "start"
        g.menu.show_menu = True
    g.class_selector = ClassSelectorManager(g.state, g.input_config, g.screen)
    g.player_manager = PlayerManager(g.ecs.ecs_world)


def init_audio(ctx: InitContext) -> None:
    """Inicializa el servicio de audio (hilo) y registra un AudioBus global."""
    g = ctx.game
    # Asegurar configuración de audio desde el inicio para respetar volúmenes guardados
    try:
        if not hasattr(g, "audio_config") or g.audio_config is None:
            g.audio_config = AudioConfig()
        if not hasattr(g, "audio_manager") or g.audio_manager is None:
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
        mv = (
            float(getattr(getattr(g, "audio_config", None), "get", lambda *_: 0.6)("music"))
            if getattr(g, "audio_config", None)
            else 0.6
        )
    except Exception:
        mv = 0.6
    try:
        sv = (
            float(getattr(getattr(g, "audio_config", None), "get", lambda *_: 0.7)("sfx"))
            if getattr(g, "audio_config", None)
            else 0.7
        )
    except Exception:
        sv = 0.7
    try:
        av = (
            float(
                getattr(getattr(g, "audio_config", None), "get", lambda *_: 0.6)(
                    "ambient"
                )
            )
            if getattr(g, "audio_config", None)
            else 0.6
        )
    except Exception:
        av = 0.6
    try:
        if getattr(g, "audio_config", None) is None and catalog is not None and getattr(
            catalog, "groups", None
        ):
            mv = float((catalog.groups.get("music") or {}).get("volume", mv))
            sv = float((catalog.groups.get("sfx") or {}).get("volume", sv))
            av = float((catalog.groups.get("ambient") or {}).get("volume", av))
    except Exception:
        pass
    try:
        bus.set_music_volume(mv)
        bus.set_sfx_volume(sv)
        bus.set_ambient_volume(av)
    except Exception:
        pass
    g.audio_service = svc
    g.audio_bus = bus
    # Hook para "Aplicar ahora" desde el configurador de sonidos
    try:
        from roguelike_engine.audio.api import set_apply_hook as _set_apply_hook

        def _apply_now():
            try:
                aq = g.ecs.ecs_world.components.setdefault("AudioEventQueue", [])
                aq.append({"type": "reload_audio_catalog"})
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
            startup_cfg = (getattr(catalog, "defaults", {}) or {}).get("startup", {}) or {}
        except Exception:
            startup_cfg = {}
        play_on_boot = bool(startup_cfg.get("play_on_boot", True))
        fade_in_ms = int(startup_cfg.get("fade_in_ms", 300))
        startup_loop = bool(startup_cfg.get("loop", True))
        flash_enabled = bool(startup_cfg.get("flash_enabled", True))
        flash_at_s = float(startup_cfg.get("flash_at_s", 6.0))
        flash_duration_s = float(startup_cfg.get("flash_duration_s", 0.25))
        flash_color_rgba = tuple(startup_cfg.get("flash_color_rgba", [255, 255, 255, 255]))
        flash_ease = str(startup_cfg.get("flash_ease", "linear"))
        flash_trigger = str(startup_cfg.get("flash_trigger", "time"))
        enable_cycle_after_flash = bool(startup_cfg.get("enable_carousel_after_flash", True))
        block_cycle_until_flash = bool(startup_cfg.get("block_carousel_until_flash", True))
        # Exponer para el menú/renderer
        g.startup_ui_fx = {
            "flash_at_s": flash_at_s,
            "flash_duration_s": flash_duration_s,
            "flash_enabled": flash_enabled,
            "flash_color_rgba": flash_color_rgba,
            "flash_ease": flash_ease,
            "flash_trigger": flash_trigger,
            "enable_carousel_after_flash": enable_cycle_after_flash,
            "block_carousel_until_flash": block_cycle_until_flash,
            "fade_in_ms": fade_in_ms,
            "loop": startup_loop,
        }
        if play_on_boot:
            startup_id = None
            intro_path = None
            try:
                defaults = catalog.get_default_music() if catalog else {}
                startup_id = (defaults or {}).get("startup_track_id")
            except Exception:
                startup_id = None
            if startup_id:
                bus.play_music(
                    track_id=startup_id, loop=startup_loop, volume=mv, fade_in_ms=fade_in_ms
                )
            else:
                intro_path = "assets/audio/music/intro_theme.mp3"
                bus.play_music(
                    path=intro_path, loop=startup_loop, volume=mv, fade_in_ms=fade_in_ms
                )
            g.intro_music_started_at = _time.time()
            g.menu_music_prestarted = True
    except Exception:
        pass
