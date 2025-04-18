# roguelike_project/systems/editor/tiles/tile_editor.py

import pygame
from pathlib import Path
from roguelike_project.config import TILE_SIZE
from .tile_picker import TilePicker
from .toolbar import TileToolbar
from roguelike_project.config_tiles import OVERLAY_CODE_MAP, INVERSE_OVERLAY_MAP, DEFAULT_TILE_MAP
from roguelike_project.engine.game.systems.map.overlay_manager import save_overlay
from roguelike_project.utils.loader import load_image

OUTLINE_SEL    = (0, 255, 0)     # seleccionado (verde)
OUTLINE_HOVER  = (0, 220, 255)   # hover (cian)
OUTLINE_CHOICE = (255, 255, 0)   # elección actual (amarillo)

class TileEditor:
    """
    • Contorno verde  → tile seleccionado
    • Contorno cian   → tile bajo el cursor
    • Toolbar de herramientas
    """
    def __init__(self, state, editor_state):
        self.state   = state
        self.editor  = editor_state     # instancia de TileEditorState
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

    def render_selection_outline(self, screen):
        if not self.editor.active:
            return

        # 1) Toolbar y toggle view
        self.toolbar.render(screen)

        # 2) Panel de ViewTile si está activo
        if self.editor.view_active:
            self.render_view_panel(screen)

        # 3) Contornos en el mundo (hover y seleccion)
        cam = self.state.camera
        hover = self._tile_under_mouse(pygame.mouse.get_pos())
        if hover:
            rect = pygame.Rect(cam.apply((hover.x, hover.y)), cam.scale((TILE_SIZE, TILE_SIZE)))
            pygame.draw.rect(screen, OUTLINE_HOVER, rect, 3)
        sel = self.editor.selected_tile
        if sel:
            rect = pygame.Rect(cam.apply((sel.x, sel.y)), cam.scale((TILE_SIZE, TILE_SIZE)))
            pygame.draw.rect(screen, OUTLINE_SEL, rect, 3)

    def render_picker(self, screen):
        self.picker.render(screen)

    def render_view_panel(self, screen):
        panel_w = TILE_SIZE + 40
        panel_h = 3 * (TILE_SIZE + 30)
        x0 = self.toolbar.x + self.toolbar.size + 20
        y0 = self.toolbar.y

        panel = pygame.Surface((panel_w, panel_h), pygame.SRCALPHA)
        panel.fill((20, 20, 20, 200))
        font = pygame.font.SysFont("Arial", 14)
        mouse_pos = pygame.mouse.get_pos()

        items = [
            ("Hovered",  self._tile_under_mouse(mouse_pos), OUTLINE_HOVER),
            ("Selected", self.editor.selected_tile,           OUTLINE_SEL),
            ("Choice",   None,                                 OUTLINE_CHOICE),
        ]
        for idx, (label, tile, color) in enumerate(items):
            ty = idx * (TILE_SIZE + 30) + 10
            if label == "Choice" and self.editor.current_choice:
                sprite = load_image(self.editor.current_choice, (TILE_SIZE, TILE_SIZE))
            else:
                sprite = tile.sprite if tile else None

            if sprite:
                panel.blit(sprite, ((panel_w - TILE_SIZE)//2, ty))
            rect = pygame.Rect((panel_w - TILE_SIZE)//2, ty, TILE_SIZE, TILE_SIZE)
            pygame.draw.rect(panel, color, rect, 3)

            text = font.render(label, True, (255, 255, 255))
            panel.blit(text, (5, ty + TILE_SIZE + 2))

        screen.blit(panel, (x0, y0))

    def _tile_under_mouse(self, mouse_pos):
        mx, my = mouse_pos
        world_x = mx / self.state.camera.zoom + self.state.camera.offset_x
        world_y = my / self.state.camera.zoom + self.state.camera.offset_y
        col = int(world_x // TILE_SIZE)
        row = int(world_y // TILE_SIZE)
        if 0 <= row < len(self.state.tile_map) and 0 <= col < len(self.state.tile_map[0]):
            return self.state.tile_map[row][col]
        return None