# Path: src/roguelike_game/systems/editor/buildings/view/tools/resize_tool_view.py
import pygame

class ResizeToolView:
    def __init__(self, state, editor_state, handle_size=50):
        self.state = state
        self.editor = editor_state
        self.handle_size = handle_size

    def render_resize_handle(self, screen, building):
        x, y = self.state.camera.apply((building.x, building.y))
        w, h = self.state.camera.scale(building.image.get_size())

        handle_rect = pygame.Rect(
            x + w - self.handle_size,
            y,
            self.handle_size,
            self.handle_size
        )
        pygame.draw.rect(screen, (0, 150, 255), handle_rect)
        pygame.draw.rect(screen, (255, 255, 255), handle_rect, 2)