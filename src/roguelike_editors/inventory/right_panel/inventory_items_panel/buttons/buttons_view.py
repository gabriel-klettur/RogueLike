import pygame
from roguelike_ui.widgets.text_input import TextInput

class ButtonsView:
    """
    Vista para renderizar todos los botones del panel de inventario:
    Show Default/Active, Add Item, Delete Item, Save
    """
    def __init__(self, font, button_size, margin):
        self.font = font
        self.button_size = button_size
        self.margin = margin
        # TextInput para cantidad de eliminación
        self.delete_qty_input = TextInput(self.font)
        self.delete_qty_input_rect = None

    def draw_show_buttons(self, overlay, grid_origin_x, grid_origin_y, mx, my, current_editing_side, slots_count):
        """Dibuja botones Show Default/Active"""
        cols = 5
        rows = (slots_count + cols - 1) // cols
        show_y = grid_origin_y - self.button_size[1] - self.margin
        rects = {}
        
        # Show Default
        show_default_rect = pygame.Rect(grid_origin_x, show_y, *self.button_size)
        pygame.draw.rect(overlay, (100, 100, 100), show_default_rect)
        border_color = (255, 255, 0) if (current_editing_side == 'default' or show_default_rect.collidepoint(mx, my)) else (255, 255, 255)
        pygame.draw.rect(overlay, border_color, show_default_rect, 2)
        txt_def = self.font.render("Show Default", True, (255, 255, 255))
        overlay.blit(txt_def, (grid_origin_x + 10, show_y + 5))
        rects['show_default'] = show_default_rect
        
        # Show Active
        act_x = grid_origin_x + self.button_size[0] + 10
        show_active_rect = pygame.Rect(act_x, show_y, *self.button_size)
        pygame.draw.rect(overlay, (100, 100, 100), show_active_rect)
        border_color = (255, 255, 0) if (current_editing_side == 'active' or show_active_rect.collidepoint(mx, my)) else (255, 255, 255)
        pygame.draw.rect(overlay, border_color, show_active_rect, 2)
        txt_act = self.font.render("Show Active", True, (255, 255, 255))
        overlay.blit(txt_act, (act_x + 10, show_y + 5))
        rects['show_active'] = show_active_rect
        
        return rects

    def draw_manage_buttons(self, overlay, grid_origin_x, grid_origin_y, mx, my, delete_mode_active, slots_count):
        """Dibuja botones Add Item y Delete Item"""
        cols = 5
        rows = (slots_count + cols - 1) // cols
        manage_y = grid_origin_y + rows * (50 + self.margin) + self.margin  # 50 = slot_size
        rects = {}

        # Add Item
        add_item_rect = pygame.Rect(grid_origin_x, manage_y, *self.button_size)
        pygame.draw.rect(overlay, (100, 100, 100), add_item_rect)
        border_color = (255, 255, 0) if add_item_rect.collidepoint(mx, my) else (255, 255, 255)
        pygame.draw.rect(overlay, border_color, add_item_rect, 2)
        txt_add = self.font.render("Add Item", True, (255, 255, 255))
        overlay.blit(txt_add, (grid_origin_x + 10, manage_y + 5))
        rects['add_item'] = add_item_rect

        # Delete Item
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
        rects['delete_item'] = delete_item_rect

        return rects

    def draw_delete_quantity_input(self, overlay, grid_origin_x, grid_origin_y, mx, my, slots_count, add_item_rect, delete_item_rect):
        """Dibuja el input de cantidad para eliminación"""
        cols = 5
        rows = (slots_count + cols - 1) // cols
        manage_y = grid_origin_y + rows * (50 + self.margin) + self.margin
        qty_y = manage_y + self.button_size[1] + self.margin
        
        # Center Quantity label & input under manage buttons
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

    def draw_save_button(self, overlay, grid_origin_x, grid_origin_y, mx, my, slots_count, delete_mode_active):
        """Dibuja el botón Save unificado"""
        cols = 5
        rows = (slots_count + cols - 1) // cols
        base_y = grid_origin_y + rows * (50 + self.margin) + self.margin
        save_y = base_y + self.button_size[1] + self.margin
        
        if delete_mode_active:
            save_y += self.button_size[1] + self.margin
            
        total_width = self.button_size[0] * 2 + self.margin
        btn_x = grid_origin_x
        
        save_rect = pygame.Rect(btn_x, save_y, total_width, self.button_size[1])
        pygame.draw.rect(overlay, (100, 100, 100), save_rect)
        border_color = (255, 255, 0) if save_rect.collidepoint(mx, my) else (255, 255, 255)
        pygame.draw.rect(overlay, border_color, save_rect, 2)
        
        txt = self.font.render("Save", True, (255, 255, 255))
        overlay.blit(txt, (btn_x + (total_width - txt.get_width()) // 2, save_y + (self.button_size[1] - txt.get_height()) // 2))
        
        return {'save': save_rect}
