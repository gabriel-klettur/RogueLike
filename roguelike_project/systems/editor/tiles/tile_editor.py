import pygame
from roguelike_project.config import TILE_SIZE
from .tile_picker import TilePicker

class TileEditor:
    """
    Lógica principal: selección de tile y delegación al TilePicker.
    """
    OUTLINE_CLR = (0, 255, 0)

    def __init__(self, state, editor_state):
        self.state  = state
        self.editor = editor_state
        self.picker = TilePicker(state, editor_state)

    # ---------------------------------------------------- #
    # API pública llamada desde eventos
    def select_tile_at(self, mouse_pos):
        mx, my = mouse_pos
        world_x = mx / self.state.camera.zoom + self.state.camera.offset_x
        world_y = my / self.state.camera.zoom + self.state.camera.offset_y

        col = int(world_x // TILE_SIZE)
        row = int(world_y // TILE_SIZE)

        if 0 <= row < len(self.state.tile_map) and 0 <= col < len(self.state.tile_map[0]):
            tile = self.state.tile_map[row][col]
            self.editor.selected_tile = tile
            self.editor.picker_open   = True
            self.editor.scroll_offset = 0

    # ---------------------------------------------------- #
    # Render helpers
    def render_selection_outline(self, screen):
        if not self.editor.selected_tile:
            return
        tile = self.editor.selected_tile
        cam = self.state.camera
        rect = pygame.Rect(tile.x, tile.y, TILE_SIZE, TILE_SIZE)
        rect_screen = pygame.Rect(cam.apply(rect.topleft), cam.scale(rect.size))
        pygame.draw.rect(screen, self.OUTLINE_CLR, rect_screen, 3)

    def render_picker(self, screen):
        self.picker.render(screen)
