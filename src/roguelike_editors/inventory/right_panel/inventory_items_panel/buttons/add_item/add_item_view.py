import pygame

class AddItemView:
    """
    Vista para renderizar el botón Add Item.
    """
    def __init__(self, font, button_size, margin):
        self.font = font
        self.button_size = button_size
        self.margin = margin

    def draw(self, overlay, grid_origin_x, grid_origin_y, mx, my, slots_count):
        cols = 5
        rows = (slots_count + cols - 1) // cols
        manage_y = grid_origin_y + rows * (50 + self.margin) + self.margin
        add_item_rect = pygame.Rect(grid_origin_x, manage_y, *self.button_size)
        pygame.draw.rect(overlay, (100, 100, 100), add_item_rect)
        border_color = (255, 255, 0) if add_item_rect.collidepoint(mx, my) else (255, 255, 255)
        pygame.draw.rect(overlay, border_color, add_item_rect, 2)
        txt_add = self.font.render("Add Item", True, (255, 255, 255))
        overlay.blit(txt_add, (grid_origin_x + 10, manage_y + 5))
        return {'add_item': add_item_rect}
