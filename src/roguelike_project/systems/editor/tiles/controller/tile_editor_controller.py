# src.roguelike_project/systems/editor/tiles/tile_editor.py

import pygame
from pathlib import Path
from src.roguelike_project.config import TILE_SIZE
from src.roguelike_project.config_tiles import OVERLAY_CODE_MAP, INVERSE_OVERLAY_MAP, DEFAULT_TILE_MAP

from src.roguelike_project.systems.editor.tiles.controller.tools.tile_picker import TilePicker
from src.roguelike_project.systems.editor.tiles.controller.tools.tile_toolbar import TileToolbar

from src.roguelike_project.engine.game.systems.map.overlay_manager import save_overlay
from src.roguelike_project.utils.loader import load_image



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

    def select_tile_at(self, mouse_pos):
        tile = self._tile_under_mouse(mouse_pos)
        if tile:
            self.editor.selected_tile = tile
            self.editor.picker_open   = True
            self.editor.scroll_offset = 0

    def apply_brush(self, mouse_pos):
        tile = self._tile_under_mouse(mouse_pos)
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

        if self.state.overlay_map is None:
            h = len(self.state.tile_map)
            w = len(self.state.tile_map[0]) if h else 0
            self.state.overlay_map = [["" for _ in range(w)] for _ in range(h)]

        self.state.overlay_map[row][col] = code
        save_overlay(self.state.map_name, self.state.overlay_map)

    def apply_eyedropper(self, mouse_pos):
        tile = self._tile_under_mouse(mouse_pos)
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


    def _tile_under_mouse(self, mouse_pos):
        mx, my = mouse_pos
        world_x = mx / self.state.camera.zoom + self.state.camera.offset_x
        world_y = my / self.state.camera.zoom + self.state.camera.offset_y
        col = int(world_x // TILE_SIZE)
        row = int(world_y // TILE_SIZE)
        if 0 <= row < len(self.state.tile_map) and 0 <= col < len(self.state.tile_map[0]):
            return self.state.tile_map[row][col]
        return None