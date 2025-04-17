# roguelike_project/engine/game/game.py
import pygame

from roguelike_project.config import (
    SCREEN_WIDTH, SCREEN_HEIGHT, FONT_NAME, FONT_SIZE, BUILDINGS_DATA_PATH
)

# ---------- core input / update ----------
from roguelike_project.engine.game.input.events     import handle_events
from roguelike_project.engine.game.systems.state    import GameState
from roguelike_project.engine.game.systems.map_manager import build_map
from roguelike_project.engine.game.systems.entity   import load_entities
from roguelike_project.engine.game.systems.multiplayer_manager import NetworkManager
from roguelike_project.ui.menus.menu               import Menu
from roguelike_project.engine.camera               import Camera
from roguelike_project.engine.game.render.render   import Renderer
from roguelike_project.engine.game.update_manager  import update_game
from roguelike_project.systems.systems_manager     import SystemsManager

# ---------- Building‑editor ----------
from roguelike_project.systems.editor.editor_state                     import EditorState
from roguelike_project.systems.editor.buildings.building_editor       import BuildingEditor
from roguelike_project.systems.editor.buildings.tools.placer_tool     import PlacerTool
from roguelike_project.systems.editor.buildings.tools.delete_tool     import DeleteTool
from roguelike_project.systems.editor.buildings.editor_events         import handle_editor_events
from roguelike_project.systems.editor.json_handler                    import save_buildings_to_json

# ---------- Tile‑editor ----------
from roguelike_project.systems.editor.tiles.tile_editor_state   import TileEditorState
from roguelike_project.systems.editor.tiles.tile_editor         import TileEditor
from roguelike_project.systems.editor.tiles.tile_editor_events  import handle_tile_editor_events

# ---------- Z‑Layer ----------
from roguelike_project.systems.z_layer.state   import ZState
from roguelike_project.systems.z_layer.config  import Z_LAYERS


class Game:
    def __init__(self, screen, perf_log=None):
        # ------------- infraestructura -----------------
        self.screen = screen
        self.clock  = pygame.time.Clock()
        self.font   = pygame.font.SysFont(FONT_NAME, FONT_SIZE)
        self.camera = Camera(SCREEN_WIDTH, SCREEN_HEIGHT)
        self.z_state = ZState()

        # ------------- core state ----------------------
        self._init_state(perf_log)
        self._init_map()
        self._init_entities()
        self._init_z_layer()
        self._init_systems()

        # ------------- editores ------------------------
        self._init_building_editor()
        self._init_tile_editor()

    # ================================================= #
    # --------------------- STATE ---------------------- #
    def _init_state(self, perf_log):
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
        self.state.perf_log = perf_log

    # ================================================= #
    def _init_map(self):
        self.map_data, self.state.tile_map = build_map()
        self.state.tiles = [t for row in self.state.tile_map for t in row]

    def _init_entities(self):
        player, obstacles, buildings, enemies = load_entities(self.z_state)
        self.state.player     = player
        self.state.obstacles  = obstacles
        self.state.buildings  = buildings
        self.state.enemies    = enemies

        # listado de debug
        print("🛠️ Buildings cargados:")
        for i, b in enumerate(self.state.buildings, 1):
            print(f"{i:02d} | ({b.x:>6.0f},{b.y:>6.0f}) | Z=({b.z_bottom},{b.z_top}) | img={b.image_path}")

    # ================================================= #
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

    # ================================================= #
    def _init_systems(self):
        self.renderer = Renderer()

        self.state.player.renderer.state = self.state
        self.state.player.state          = self.state

        self.state.menu  = Menu(self.state)
        self.state.show_menu = False
        self.state.mode      = "local"

        self.systems = SystemsManager(self.state)
        self.state.systems = self.systems
        self.state.combat  = self.systems.combat
        self.state.effects = self.systems.effects

        self.network = NetworkManager(self.state)
        if self.state.mode == "online":
            self.network.connect()

    # ================================================= #
    # ---------------- Building‑Editor ---------------- #
    def _init_building_editor(self):
        self.editor_state   = EditorState()
        self.building_editor = BuildingEditor(self.state, self.editor_state)

        # herramientas
        self.placer_tool = PlacerTool(
            self.state, self.editor_state,
            building_class=type(self.state.buildings[0]),
            default_image="assets/buildings/others/portal.png",
            default_scale=(512, 824),
            default_solid=True
        )
        self.delete_tool = DeleteTool(self.state, self.editor_state)

        # acceso desde editor_events
        self.state.placer_tool = self.placer_tool
        self.state.delete_tool = self.delete_tool

        self.state.editor = self.editor_state

    # ------------------ Tile‑Editor ------------------ #
    def _init_tile_editor(self):
        self.tile_editor_state = TileEditorState()
        self.tile_editor       = TileEditor(self.state, self.tile_editor_state)

        self.state.tile_editor       = self.tile_editor
        self.state.tile_editor_state = self.tile_editor_state
        self.state.tile_editor_active = False

    # ================================================= #
    # ---------------- HANDLER DE EVENTOS -------------- #
    def handle_events(self):
        # ---- Tile‑Editor (F8) -------------------------
        if self.tile_editor_state.active:
            handle_tile_editor_events(
                self.state, self.tile_editor_state, self.tile_editor
            )
            return

        # ---- Building‑Editor (F10) --------------------
        if self.state.editor.active:
            handle_editor_events(
                self.state, self.editor_state, self.building_editor
            )
            return

        # ---- Loop de juego normal ---------------------
        handle_events(self.state)

    # ================================================= #
    def update(self):
        if self.tile_editor_state.active:
            # no lógica de juego en modo tile
            return

        if self.state.editor.active:
            self.building_editor.update()
        else:
            update_game(self.state)

    def render(self, perf_log=None):
        self.renderer.render_game(self.state, perf_log)
        if self.state.editor.active:
            self.building_editor.render_selection_outline(self.screen)

    # ================================================= #
    def run(self):
        while self.state.running:
            self.handle_events()
            self.update()
            self.render(self.state.perf_log)
            self.state.clock.tick(60)

    def quit(self):
        if hasattr(self, 'network'):
            self.network.disconnect()
        pygame.quit()
