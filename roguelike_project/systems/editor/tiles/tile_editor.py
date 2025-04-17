import pygame
from roguelike_project.config import TILE_SIZE
from .tile_picker import TilePicker

OUTLINE_SEL   = (0, 255, 0)     # seleccionado (verde)
OUTLINE_HOVER = (0, 220, 255)   # hover (cian)

class TileEditor:
    """
    • Contorno verde  → tile seleccionado
    • Contorno cian   → tile bajo el cursor
    Se muestran **solo** cuando editor_state.active es True.
    """
    def __init__(self, state, editor_state):
        self.state  = state
        self.editor = editor_state     # instancia de TileEditorState
        self.picker = TilePicker(state, editor_state)

    # -------------------- API pública ------------------- #
    def select_tile_at(self, mouse_pos):
        tile = self._tile_under_mouse(mouse_pos)
        if tile:
            self.editor.selected_tile = tile
            self.editor.picker_open   = True
            self.editor.scroll_offset = 0

    # -------------------- RENDER ------------------------ #
    def render_selection_outline(self, screen):
        # 🔒  Dibujar solo si el modo está activo
        if not self.editor.active:
            return

        cam = self.state.camera

        # ----------- HOVER (cian) -----------
        hover = self._tile_under_mouse(pygame.mouse.get_pos())
        if hover:
            rect = pygame.Rect(
                cam.apply((hover.x, hover.y)),
                cam.scale((TILE_SIZE, TILE_SIZE))
            )
            pygame.draw.rect(screen, OUTLINE_HOVER, rect, 3)

        # -------- Selección (verde) --------
        sel = self.editor.selected_tile
        if sel:
            rect = pygame.Rect(
                cam.apply((sel.x, sel.y)),
                cam.scale((TILE_SIZE, TILE_SIZE))
            )
            pygame.draw.rect(screen, OUTLINE_SEL, rect, 3)

    def render_picker(self, screen):
        self.picker.render(screen)

    # ------------------ helpers ------------------------- #
    def _tile_under_mouse(self, mouse_pos):
        mx, my  = mouse_pos
        world_x = mx / self.state.camera.zoom + self.state.camera.offset_x
        world_y = my / self.state.camera.zoom + self.state.camera.offset_y

        col = int(world_x // TILE_SIZE)
        row = int(world_y // TILE_SIZE)

        if 0 <= row < len(self.state.tile_map) and 0 <= col < len(self.state.tile_map[0]):
            return self.state.tile_map[row][col]
        return None
