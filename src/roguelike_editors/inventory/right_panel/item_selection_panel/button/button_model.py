import pygame

class ButtonModel:
    """
    Model for drag state of the selection panel.
    """
    def __init__(self):
        self.drag_offset = pygame.Vector2(0, 0)
        self.dragging = False
        self.drag_start_pos = pygame.Vector2(0, 0)
