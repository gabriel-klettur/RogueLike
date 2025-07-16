import pygame

class ItemSelectionPanelModel:
    def __init__(self, available_items: list[str] = None, visible_count: int = 10):
        self.available_items = available_items or []
        self.visible_count = visible_count
        self.scroll_offset = 0
        self.selected_item = None
        self.quantity = 1
        self.show_panel = False
        # Drag state
        self.drag_offset = pygame.Vector2(0, 0)
        self.dragging = False
        self.drag_start_pos = pygame.Vector2(0, 0)
