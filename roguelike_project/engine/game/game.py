import sys
import os
import pygame
from collections import defaultdict

# Agregar el path del proyecto
sys.path.append(os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..", "..", "..")))

from roguelike_project.config import (
    SCREEN_WIDTH, SCREEN_HEIGHT, FONT_NAME, FONT_SIZE, FPS
)
from roguelike_project.engine.game.input.events import handle_events
from roguelike_project.engine.game.systems.state import GameState
from roguelike_project.engine.game.systems.map_manager import build_map
from roguelike_project.engine.game.systems.entity import load_entities
from roguelike_project.engine.game.systems.multiplayer_manager import NetworkManager
from roguelike_project.ui.menus.menu import Menu
from roguelike_project.engine.camera import Camera
from roguelike_project.engine.game.render.render import Renderer
from roguelike_project.engine.game.update_manager import update_game
from roguelike_project.systems.systems_manager import SystemsManager

# Building‑editor: controlador y vista
from roguelike_project.systems.editor.buildings.model.building_editor_state import BuildingsEditorState
from roguelike_project.systems.editor.buildings.controller.building_editor_controller import BuildingEditorController
from roguelike_project.systems.editor.buildings.view.building_editor_view import BuildingEditorView

# Tile‑editor
from roguelike_project.systems.editor.tiles.tile_editor_state import TileEditorState
from roguelike_project.systems.editor.tiles.tile_editor import TileEditor
from roguelike_project.systems.editor.tiles.tile_editor_events import handle_tile_editor_events

# Z‑Layer
from roguelike_project.systems.z_layer.state import ZState
from roguelike_project.systems.z_layer.config import Z_LAYERS

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
        Construye el mapa y carga el overlay.
        build_map ahora devuelve también la clave estática map_name.
        """
        # 1) Generar mapa y recibir clave
        self.map_data, self.state.tile_map, self.state.overlay_map, key = build_map(
            map_name=self.map_name
        )
        # 2) Guardar esa clave en el state
        self.state.map_name = key

        # 3) Aplanar la lista de tiles
        self.state.tiles = [t for row in self.state.tile_map for t in row]

    def _init_entities(self):
        player, obstacles, buildings, enemies = load_entities(self.z_state)
        self.state.player = player
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
        self.state.combat = self.systems.combat
        self.state.effects = self.systems.effects
        self.network = NetworkManager(self.state)
        if self.state.mode == "online":
            self.network.connect()

    def _init_building_editor(self):
        self.editor_state = BuildingsEditorState()
        self.building_editor = BuildingEditorController(self.state, self.editor_state)
        # Creamos la vista del editor para el renderizado
        self.building_editor_view = BuildingEditorView(self.state, self.editor_state)
        self.state.editor = self.editor_state

    def _init_tile_editor(self):
        self.tile_editor_state = TileEditorState()
        self.tile_editor = TileEditor(self.state, self.tile_editor_state)
        self.state.tile_editor = self.tile_editor
        self.state.tile_editor_state = self.tile_editor_state
        self.state.tile_editor_active = False

    def handle_events(self):
        if self.tile_editor_state.active:
            handle_tile_editor_events(self.state, self.tile_editor_state, self.tile_editor)
            return
        if self.state.editor.active:
            from roguelike_project.systems.editor.buildings.controller.building_editor_events import handle_editor_events
            handle_editor_events(self.state, self.editor_state, self.building_editor)
            return
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
        # Usamos ahora la vista para dibujar el editor de buildings
        if self.state.editor.active:
            self.building_editor_view.render(self.screen)

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
