# Path: src/roguelike_game/game/game.py

import sys
import os
import pygame

# Agregar el path del proyecto
sys.path.append(os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..", "..", "..")))

from src.roguelike_engine.config import (
    SCREEN_WIDTH, SCREEN_HEIGHT, FONT_NAME, FONT_SIZE, FPS
)
from src.roguelike_engine.input.events import handle_events

from roguelike_game.game.state import GameState
from roguelike_engine.map.core.manager import build_map
from roguelike_game.entities.load_entities import load_entities
from roguelike_game.network.multiplayer_manager import NetworkManager
from src.roguelike_game.ui.menus.menu import Menu
from roguelike_engine.camera.camera import Camera
from roguelike_game.game.render_manager import Renderer
from src.roguelike_game.game.update_manager import update_game
from src.roguelike_game.systems.systems_manager import SystemsManager

# Building-editor: controlador, vista y handler de eventos
from src.roguelike_game.systems.editor.buildings.model.building_editor_state import BuildingsEditorState
from src.roguelike_game.systems.editor.buildings.controller.building_editor_controller import BuildingEditorController
from src.roguelike_game.systems.editor.buildings.view.building_editor_view import BuildingEditorView
from src.roguelike_game.systems.editor.buildings.controller.building_editor_events import BuildingEditorEventHandler

# Tile-editor: controlador, vista y handler de eventos
from src.roguelike_game.systems.editor.tiles.model.tile_editor_state import TileEditorControllerState
from src.roguelike_game.systems.editor.tiles.controller.tile_editor_controller import TileEditorController
from src.roguelike_game.systems.editor.tiles.view.tile_editor_view import TileEditorControllerView
from src.roguelike_game.systems.editor.tiles.controller.tile_editor_events import TileEditorEventHandler

# Z-Layer
from src.roguelike_game.systems.z_layer.state import ZState
from roguelike_game.systems.config_z_layer import Z_LAYERS

class Game:
    def __init__(self, screen, perf_log=None, map_name: str = None):
        # Permitimos pasar un map_name explícito
        self.map_name = map_name

        # ------------- infraestructura -----------------
        self.screen = screen
        self.clock = pygame.time.Clock()
        self.font = pygame.font.SysFont(FONT_NAME, FONT_SIZE)
        self.camera = Camera(SCREEN_WIDTH, SCREEN_HEIGHT)
        self.z_state = ZState()
        self.perf_log = perf_log

        # ------------- core state ----------------------
        self._init_state()
        self._init_map()
        self._init_entities()
        self._init_z_layer()
        self._init_systems()

        # ------------- editores ------------------------
        self._init_building_editor()
        self._init_tile_editor()

    def _init_state(self):
        self.state = GameState(
            screen=self.screen,
            background=None,
            player=None,
            obstacles=None,
            buildings=None,
            camera=self.camera,
            clock=self.clock,
            font=self.font,
            menu=None,
            tiles=None,
            enemies=None
        )
        self.state.running = True
        self.state.perf_log = self.perf_log

    def _init_map(self):
        """
        Construye el mapa global y carga el overlay.
        Ahora usa map_mode="global" para crear un lienzo mayor
        que contiene tanto el lobby como la dungeon.
        """
        # Generar mapa global en lugar del combinado por defecto
        result = build_map(
            map_mode="global",
            map_name=self.map_name
        )

        # Asignar a state
        self.map_data          = result.matrix        # lista de strings
        self.state.tile_map    = result.tiles         # lista de listas de Tile
        self.state.overlay_map = result.overlay       # capa overlay (o None)
        self.state.map_name    = result.name          # clave para persistir overlay

        # Aplanar la lista de tiles
        self.state.tiles = [t for row in self.state.tile_map for t in row]

    def _init_entities(self):
        player, obstacles, buildings, enemies = load_entities(self.z_state)
        self.state.player    = player
        self.state.obstacles = obstacles
        self.state.buildings = buildings
        print("🛠️ Buildings cargados:")
        for i, b in enumerate(self.state.buildings, 1):
            print(f"{i:02d} | ({b.x:.0f},{b.y:.0f}) | Z=({b.z_bottom},{b.z_top}) | img={b.image_path}")
        self.state.enemies = enemies

    def _init_z_layer(self):
        zs = self.z_state
        self.state.z_state = zs
        zs.set(self.state.player, Z_LAYERS["player"])
        for e in self.state.enemies:
            zs.set(e, Z_LAYERS["player"])
        for o in self.state.obstacles:
            zs.set(o, Z_LAYERS["low_object"])
        for b in self.state.buildings:
            zs.set(b, b.z_bottom)

    def _init_systems(self):
        self.renderer = Renderer()
        self.state.player.renderer.state = self.state
        self.state.player.state = self.state
        self.state.menu = Menu(self.state)
        self.state.show_menu = False
        self.state.mode = "local"
        self.systems = SystemsManager(self.state)
        self.state.systems = self.systems        
        self.state.effects = self.systems.effects
        self.network = NetworkManager(self.state)
        if self.state.mode == "online":
            self.network.connect()

    def _init_building_editor(self):
        # Estado, controlador y vista
        self.editor_state = BuildingsEditorState()
        self.building_editor = BuildingEditorController(self.state, self.editor_state)
        self.building_editor_view = BuildingEditorView(self.state, self.editor_state)
        self.state.editor = self.editor_state

        # Handler de eventos
        self.building_event_handler = BuildingEditorEventHandler(
            self.state,
            self.editor_state,
            self.building_editor
        )

    def _init_tile_editor(self):
        # Estado, controlador y vista
        self.tile_editor_state = TileEditorControllerState()
        self.tile_editor = TileEditorController(self.state, self.tile_editor_state)
        self.state.tile_editor = self.tile_editor
        self.state.tile_editor_state = self.tile_editor_state
        self.tile_editor_view = TileEditorControllerView(
            self.tile_editor,
            self.state,
            self.tile_editor_state
        )
        self.state.tile_editor_view = self.tile_editor_view
        self.state.tile_editor_active = False

        # Handler de eventos
        self.tile_event_handler = TileEditorEventHandler(
            self.state,
            self.tile_editor_state,
            self.tile_editor
        )

    def handle_events(self):
        # Prioriza el Tile Editor
        if self.tile_editor_state.active:
            self.tile_event_handler.handle()
            return

        # Luego el Building Editor
        if self.state.editor.active:
            self.building_event_handler.handle()
            return

        # Finalmente, el loop de eventos normal
        handle_events(self.state)

    def update(self):
        if self.tile_editor_state.active:
            return
        if self.state.editor.active:
            self.building_editor.update()
        else:
            update_game(self.state)

    def render(self, perf_log=None):
        self.renderer.render_game(self.state, perf_log)
        # Building-editor
        if self.state.editor.active:
            self.building_editor_view.render(self.screen)
        # Tile-editor
        if self.tile_editor_state.active:
            self.tile_editor_view.render(self.screen)

    def run(self):
        while self.state.running:
            self.handle_events()
            self.update()
            self.render(self.state.perf_log)
            self.state.clock.tick(FPS)

    def quit(self):
        if hasattr(self, 'network'):
            self.network.disconnect()
        pygame.quit()
