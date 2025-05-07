# Path: src/roguelike_game/systems/editor/buildings/controller/tools/default_tool.py
import pygame

class DefaultTool:
    def __init__(self, state, editor_state, handle_size=50):
        self.state = state
        self.editor = editor_state
        self.handle_size = handle_size

    def check_reset_handle_click(self, mx, my, building):
        """
        Verifica si el clic fue sobre el handle de reset (blanco),
        que se ubica a la izquierda del handle azul de resize.
        """
        bx, by = self.camera.apply((building.x, building.y))
        bw, bh = self.camera.scale(building.image.get_size())

        reset_rect = pygame.Rect(
            bx + bw - 2 * self.handle_size,
            by,
            self.handle_size,
            self.handle_size
        )
        return reset_rect.collidepoint(mx, my)

    def apply_reset(self, building):
        building.reset_to_original_size()