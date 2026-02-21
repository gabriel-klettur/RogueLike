import pygame
from roguelike_editors.buildings.buildings_editor_config import SPLIT_HANDLE_SIZE, SPLIT_BAR_COLOR

class SplitToolView:
    def __init__(self, state, editor_state):
        self.state       = state
        self.editor      = editor_state
        self.handle_size = SPLIT_HANDLE_SIZE
        self.bar_color   = SPLIT_BAR_COLOR

    def render(self, screen, building, camera):
        
        bx, by       = camera.apply((building.x, building.y))
        w_scaled, h_scaled = camera.scale(building.image.get_size())
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
        handle_left = bx + offset_x
        handle_top  = y_split - self.handle_size // 2
        screen.blit(handle, (handle_left, handle_top))
        # Devolver el rect del handle en coordenadas de pantalla para overlays externos (tutorial)
        return {"handle_rect": pygame.Rect(handle_left, handle_top, self.handle_size, self.handle_size)}