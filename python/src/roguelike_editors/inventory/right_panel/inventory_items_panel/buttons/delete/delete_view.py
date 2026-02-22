import pygame
from roguelike_ui.widgets.text_input.text_input import TextInput

class DeleteView:
    """
    Vista para renderizar el botón Delete Item y la entrada de cantidad.
    """
    def __init__(self, font, button_size, margin):
        self.font = font
        self.button_size = button_size
        self.margin = margin
        self.delete_qty_input = TextInput(self.font)
        self.delete_qty_input_rect = None

    def draw_button(self, overlay, grid_origin_x, grid_origin_y, mx, my, slots_count, delete_mode_active):
        cols = 5
        rows = (slots_count + cols - 1) // cols
        manage_y = grid_origin_y + rows * (50 + self.margin) + self.margin
        del_x = grid_origin_x + self.button_size[0] + self.margin
        delete_item_rect = pygame.Rect(del_x, manage_y, *self.button_size)
        if delete_mode_active:
            border_color = (255, 0, 0)
        elif delete_item_rect.collidepoint(mx, my):
            border_color = (255, 255, 0)
        else:
            border_color = (255, 255, 255)
        pygame.draw.rect(overlay, (100, 100, 100), delete_item_rect)
        pygame.draw.rect(overlay, border_color, delete_item_rect, 2)
        txt_del = self.font.render("Delete Item", True, (255, 255, 255))
        overlay.blit(txt_del, (del_x + 10, manage_y + 5))
        return {'delete_item': delete_item_rect}

    def draw_input(self, overlay, grid_origin_x, grid_origin_y, mx, my, slots_count, add_item_rect, delete_item_rect):
        cols = 5
        rows = (slots_count + cols - 1) // cols
        manage_y = grid_origin_y + rows * (50 + self.margin) + self.margin
        qty_y = manage_y + self.button_size[1] + self.margin
        
        if add_item_rect and delete_item_rect:
            manage_left = add_item_rect.x
            manage_width = delete_item_rect.right - add_item_rect.x
        else:
            manage_left = grid_origin_x
            manage_width = self.button_size[0] * 2 + self.margin
        
        # Render label and input sizes
        label_surf = self.font.render("Quantity:", True, (255, 255, 255))
        label_w = label_surf.get_width() + 8
        label_h = label_surf.get_height() + 4
        text_w = self.delete_qty_input.font.size(self.delete_qty_input.text)[0]
        input_w = max(text_w, self.button_size[0] // 4) + 8
        total_w = label_w + self.margin + input_w
        
        # Compute base X for centering
        start_x = manage_left + (manage_width - total_w) // 2
        
        # Label background & border
        label_bg_rect = pygame.Rect(start_x - 4, qty_y - 2, label_w, label_h)
        pygame.draw.rect(overlay, (100, 100, 100), label_bg_rect)
        pygame.draw.rect(overlay, (255, 255, 255), label_bg_rect, 2)
        overlay.blit(label_surf, (start_x, qty_y))
        
        # Input background & border
        input_text_x = label_bg_rect.right + self.margin
        input_bg_rect = pygame.Rect(input_text_x - 4, qty_y - 2, input_w, label_h)
        pygame.draw.rect(overlay, (100, 100, 100), input_bg_rect)
        border_color = (255, 0, 0) if input_bg_rect.collidepoint(mx, my) else (255, 255, 255)
        pygame.draw.rect(overlay, border_color, input_bg_rect, 2)
        
        # Draw the text input inside the box
        self.delete_qty_input.draw(overlay, input_text_x, qty_y)
        self.delete_qty_input_rect = input_bg_rect
