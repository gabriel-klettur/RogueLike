import pygame

class SaveView:
    """
    Vista para renderizar el botón Save.
    """
    def __init__(self, font, button_size, margin):
        self.font = font
        self.button_size = button_size
        self.margin = margin

    def draw(self, overlay, grid_origin_x, grid_origin_y, mx, my, slots_count, delete_mode_active):
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
