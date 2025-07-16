import pygame

class InventoryGridView:
    """
    Clase para renderizar la cuadrícula de inventario, los botones de mostrar y guardar.
    """
    def __init__(self, font, slot_size, margin, button_size, get_item_image_func, images, logger):
        self.font = font
        self.slot_size = slot_size
        self.margin = margin
        self.button_size = button_size
        self.get_item_image = get_item_image_func
        self.images = images
        self.logger = logger
        # Rects de botones
        self.show_default_rect = None
        self.show_active_rect = None
        self.save_default_rect = None
        self.save_active_rect = None

    def draw(self, overlay, model, panel_rect):
        """
        Dibuja grid de inventario, botones de mostrar y botones de guardar.
        Devuelve un dict con los rects:
          'show_default', 'show_active', 'save_default', 'save_active'
        """
        # Obtener datos de slots y posición
        slots = self._get_slots(model)
        grid_origin_x, grid_origin_y = self._get_grid_origin(panel_rect)
        mx, my = pygame.mouse.get_pos()

        # Dibujar slots
        self._draw_slots(overlay, slots, grid_origin_x, grid_origin_y, mx, my)

        # Dibujar botones Show y Save
        rects = {}
        rects.update(self._draw_show_buttons(overlay, slots, grid_origin_x, grid_origin_y, mx, my))
        rects.update(self._draw_save_buttons(overlay, slots, grid_origin_x, grid_origin_y, mx, my))
        return rects

    def _get_slots(self, model):
        source = model.default_data if model.editing_side == 'default' else model.active_data
        data = source.get(model.current_category, {})
        entry = data.get(str(model.selected_eid), {})
        return entry.get('slots', [])

    def _get_grid_origin(self, panel_rect):
        return panel_rect.x + panel_rect.width + self.margin, panel_rect.y

    def _draw_slots(self, overlay, slots, grid_origin_x, grid_origin_y, mx, my):
        cols = 5
        for idx, slot in enumerate(slots):
            col = idx % cols
            row = idx // cols
            rx = grid_origin_x + col * (self.slot_size + self.margin)
            ry = grid_origin_y + row * (self.slot_size + self.margin)
            slot_rect = pygame.Rect(rx, ry, self.slot_size, self.slot_size)
            pygame.draw.rect(overlay, (80, 80, 80), slot_rect)
            # Resaltar hover
            if slot_rect.collidepoint(mx, my):
                pygame.draw.rect(overlay, (255, 255, 0), slot_rect, 2)
            else:
                pygame.draw.rect(overlay, (200, 200, 200), slot_rect, 1)
            # Dibujar ítem y cantidad
            if slot:
                try:
                    img = self.get_item_image(slot.get('item'))
                    if img:
                        overlay.blit(img, (rx + 5, ry + 5))
                except Exception as e:
                    self.logger.error(f"Error dibujando imagen de ítem: {e}")
                qty = slot.get('quantity', 0)
                qty_surf = self.font.render(str(qty), True, (255, 255, 255))
                overlay.blit(qty_surf, qty_surf.get_rect(
                    bottomright=(rx + self.slot_size - 5, ry + self.slot_size - 5)
                ))

    def _draw_show_buttons(self, overlay, slots, grid_origin_x, grid_origin_y, mx, my):
        cols = 5
        rows = (len(slots) + cols - 1) // cols
        show_y = grid_origin_y + rows * (self.slot_size + self.margin) + self.margin
        rects = {}
        # Show Default
        self.show_default_rect = pygame.Rect(grid_origin_x, show_y, *self.button_size)
        pygame.draw.rect(overlay, (100, 100, 100), self.show_default_rect)
        border_color = (255, 255, 0) if self.show_default_rect.collidepoint(mx, my) else (255, 255, 255)
        pygame.draw.rect(overlay, border_color, self.show_default_rect, 2)
        txt_def = self.font.render("Show Default", True, (255, 255, 255))
        overlay.blit(txt_def, (grid_origin_x + 10, show_y + 5))
        rects['show_default'] = self.show_default_rect
        # Show Active
        act_x = grid_origin_x + self.button_size[0] + 10
        self.show_active_rect = pygame.Rect(act_x, show_y, *self.button_size)
        pygame.draw.rect(overlay, (100, 100, 100), self.show_active_rect)
        border_color = (255, 255, 0) if self.show_active_rect.collidepoint(mx, my) else (255, 255, 255)
        pygame.draw.rect(overlay, border_color, self.show_active_rect, 2)
        txt_act = self.font.render("Show Active", True, (255, 255, 255))
        overlay.blit(txt_act, (act_x + 10, show_y + 5))
        rects['show_active'] = self.show_active_rect
        return rects

    def _draw_save_buttons(self, overlay, slots, grid_origin_x, grid_origin_y, mx, my):
        cols = 5
        rows = (len(slots) + cols - 1) // cols
        save_y = grid_origin_y + rows * (self.slot_size + self.margin) + self.margin + self.button_size[1] + self.margin
        rects = {}
        btn_x = grid_origin_x
        btn_y = save_y
        # Save Default
        self.save_default_rect = pygame.Rect(btn_x, btn_y, *self.button_size)
        pygame.draw.rect(overlay, (100, 100, 100), self.save_default_rect)
        border_color = (255, 255, 0) if self.save_default_rect.collidepoint(mx, my) else (255, 255, 255)
        pygame.draw.rect(overlay, border_color, self.save_default_rect, 2)
        txt_save_def = self.font.render("Save Default", True, (255, 255, 255))
        overlay.blit(txt_save_def, (btn_x + 10, btn_y + 5))
        rects['save_default'] = self.save_default_rect
        # Save Active
        save_act_x = btn_x + self.button_size[0] + 10
        self.save_active_rect = pygame.Rect(save_act_x, btn_y, *self.button_size)
        pygame.draw.rect(overlay, (100, 100, 100), self.save_active_rect)
        border_color = (255, 255, 0) if self.save_active_rect.collidepoint(mx, my) else (255, 255, 255)
        pygame.draw.rect(overlay, border_color, self.save_active_rect, 2)
        txt_save_act = self.font.render("Save Active", True, (255, 255, 255))
        overlay.blit(txt_save_act, (save_act_x + 10, btn_y + 5))
        rects['save_active'] = self.save_active_rect
        return rects
