import pygame
from roguelike_project.systems.editor.buildings.config import SPLIT_HANDLE_SIZE, SPLIT_BAR_COLOR

class SplitToolView:
    def __init__(self, state, editor_state):
        self.state       = state
        self.editor      = editor_state
        self.handle_size = SPLIT_HANDLE_SIZE
        self.bar_color   = SPLIT_BAR_COLOR

    def render(self, screen, building):
        cam = self.state.camera
        bx, by       = cam.apply((building.x, building.y))
        w_scaled, h_scaled = cam.scale(building.image.get_size())
        y_split      = by + int(h_scaled * building.split_ratio)

        # barra
        bar = pygame.Surface((w_scaled, 3), pygame.SRCALPHA)
        bar.fill(self.bar_color)
        screen.blit(bar, (bx, y_split - 1))

        # handle
        handle = pygame.Surface((self.handle_size, self.handle_size), pygame.SRCALPHA)
        handle.fill(self.bar_color)
        pygame.draw.rect(handle, (255, 255, 255), handle.get_rect(), 1)
        offset_x = (w_scaled - self.handle_size) // 2
        screen.blit(handle, (bx + offset_x, y_split - self.handle_size // 2))
