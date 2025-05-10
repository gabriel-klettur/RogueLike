
# Path: src/roguelike_game/systems/editor/tiles/controller/tile_editor_controller.py
from pathlib import Path
from roguelike_engine.config_tiles import TILE_SIZE
from roguelike_engine.config_tiles import OVERLAY_CODE_MAP, INVERSE_OVERLAY_MAP, DEFAULT_TILE_MAP

from roguelike_game.systems.editor.tiles.controller.tools.tile_picker_controller import TilePicker
from roguelike_game.systems.editor.tiles.controller.tools.tile_toolbar_controller import TileToolbar

from roguelike_engine.map.overlay.overlay_manager import save_overlay
from roguelike_engine.utils.loader import load_image



class TileEditorController:
    """
    • Contorno verde  → tile seleccionado
    • Contorno cian   → tile bajo el cursor
    • Toolbar de herramientas
    """
    def __init__(self, state, editor_state):
        self.state   = state
        self.editor  = editor_state     # instancia de TileEditorControllerState
        self.picker = TilePicker(state, editor_state)
        self.toolbar = TileToolbar(state, editor_state)

    def select_tile_at(self, mouse_pos, camera, map):
        tile = self._tile_under_mouse(mouse_pos, camera, map)
        if tile:
            self.editor.selected_tile = tile
            self.editor.picker_open   = True
            self.editor.scroll_offset = 0

    def apply_brush(self, mouse_pos, camera, map):
        tile = self._tile_under_mouse(mouse_pos, camera, map)
        if not tile or not self.editor.current_choice:
            return

        # cargar nuevo sprite
        sprite = load_image(self.editor.current_choice, (TILE_SIZE, TILE_SIZE))
        tile.sprite = sprite
        tile.scaled_cache.clear()

        # inferir código de overlay
        name = Path(self.editor.current_choice).stem
        codes = INVERSE_OVERLAY_MAP.get(name, [])
        code = codes[0] if codes else ""

        tile.overlay_code = code

        # actualizar overlay_map y persistir
        row = tile.y // TILE_SIZE
        col = tile.x // TILE_SIZE

        if map.overlay is None:
            h = len(map.tiles)
            w = len(map.tiles[0]) if h else 0
            self.map.overlay = [["" for _ in range(w)] for _ in range(h)]

        map.overlay[row][col] = code
        save_overlay(map.name, map.overlay)

    def apply_eyedropper(self, mouse_pos, camera, map):
        tile = self._tile_under_mouse(mouse_pos, camera, map)
        if not tile:
            return

        # obtener código o tipo base
        code = tile.overlay_code or tile.tile_type

        # mapear a nombre de archivo
        if code in OVERLAY_CODE_MAP:
            name = OVERLAY_CODE_MAP[code]
        else:
            name = DEFAULT_TILE_MAP.get(code)

        if not name:
            return

        choice = f"assets/tiles/{name}.png"
        self.editor.current_choice = choice

        # cambiar al brush para empezar a pintar
        self.editor.current_tool = "brush"


    def _tile_under_mouse(self, mouse_pos, camera, map):
        mx, my = mouse_pos
        world_x = mx / camera.zoom + camera.offset_x
        world_y = my / camera.zoom + camera.offset_y
        col = int(world_x // TILE_SIZE)
        row = int(world_y // TILE_SIZE)
        if 0 <= row < len(map.tiles) and 0 <= col < len(map.tiles[0]):
            return map.tiles[row][col]
        return None