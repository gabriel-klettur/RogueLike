import pygame

class InventoryItemsPanelView:
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

        self.add_item_rect = None
        self.delete_item_rect = None
        # Rect for unified Save button
        self.save_rect = None

    def draw(self, overlay, model, panel_rect):
        # Estado de Delete Mode para resaltar boton
        self.delete_mode_active = model.grid_model.show_delete_mode
        # Track which side is being edited to highlight buttons
        self.current_editing_side = model.editing_side
        """
        Dibuja grid de inventario, botones de mostrar y botón de guardar.
        Devuelve un dict con los rects:
          'show_default', 'show_active', 'save', 'add_item', 'delete_item'
        """
        # Obtener datos de slots y posición
        slots = self._get_slots(model)
        grid_origin_x, grid_origin_y = self._get_grid_origin(panel_rect)
        mx, my = pygame.mouse.get_pos()

        rects = {}

        # Show Default/Active above grid
        rects.update(self._draw_show_buttons(overlay, slots, grid_origin_x, grid_origin_y, mx, my))

        # Dibujar slots
        self._draw_slots(overlay, slots, grid_origin_x, grid_origin_y, mx, my)

        # Add/Delete below grid
        rects.update(self._draw_manage_buttons(overlay, slots, grid_origin_x, grid_origin_y, mx, my))

        # Dibujar Save
        rects.update(self._draw_save_buttons(overlay, slots, grid_origin_x, grid_origin_y, mx, my))
        return rects

    def _get_slots(self, model):
        # Return default or active inventory slots
        if model.editing_side == 'default':
            # Show default inventory templates
            if model.current_category == 'player':
                default_player = model.default_data.get('player', {})
                return default_player.get('slots', [])
            elif model.current_category == 'monsters':
                # Determine template of selected monster
                active_mon = model.active_data.get('monsters', {}).get(str(model.selected_eid), {})
                template_id = active_mon.get('template_id')
                for tpl_name, def_entry in model.default_data.get('monsters', {}).items():
                    if def_entry.get('template_id') == template_id:
                        inv_list = def_entry.get('inventory', [])
                        # Use min quantity for default slots
                        slots = [{'item': inv.get('item'), 'quantity': inv.get('min', 0)} for inv in inv_list]
                        # Pad slots to match active slots length
                        active_slots = active_mon.get('slots', [])
                        if len(active_slots) > len(slots):
                            slots += [None] * (len(active_slots) - len(slots))
                        return slots
            return []
        else:
            # Show active data from JSON
            active_data = model.active_data.get(model.current_category, {})
            entry = active_data.get(str(model.selected_eid), {})
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
            # Delete-mode hover highlight
            if self.delete_mode_active and slot_rect.collidepoint(mx, my) and slot:
                # draw semi-transparent red fill
                highlight = pygame.Surface((self.slot_size, self.slot_size), pygame.SRCALPHA)
                highlight.fill((255, 0, 0, 100))
                overlay.blit(highlight, (rx, ry))
                pygame.draw.rect(overlay, (255, 0, 0), slot_rect, 2)
            elif slot_rect.collidepoint(mx, my):
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
        show_y = grid_origin_y - self.button_size[1] - self.margin
        rects = {}
        # Show Default
        self.show_default_rect = pygame.Rect(grid_origin_x, show_y, *self.button_size)
        pygame.draw.rect(overlay, (100, 100, 100), self.show_default_rect)
        # Highlight default button if selected or hovered
        border_color = (255, 255, 0) if (self.current_editing_side == 'default' or self.show_default_rect.collidepoint(mx, my)) else (255, 255, 255)
        pygame.draw.rect(overlay, border_color, self.show_default_rect, 2)
        txt_def = self.font.render("Show Default", True, (255, 255, 255))
        overlay.blit(txt_def, (grid_origin_x + 10, show_y + 5))
        rects['show_default'] = self.show_default_rect
        # Show Active
        act_x = grid_origin_x + self.button_size[0] + 10
        self.show_active_rect = pygame.Rect(act_x, show_y, *self.button_size)
        pygame.draw.rect(overlay, (100, 100, 100), self.show_active_rect)
        # Highlight active button if selected or hovered
        border_color = (255, 255, 0) if (self.current_editing_side == 'active' or self.show_active_rect.collidepoint(mx, my)) else (255, 255, 255)
        pygame.draw.rect(overlay, border_color, self.show_active_rect, 2)
        txt_act = self.font.render("Show Active", True, (255, 255, 255))
        overlay.blit(txt_act, (act_x + 10, show_y + 5))
        rects['show_active'] = self.show_active_rect
        return rects

    def _draw_save_buttons(self, overlay, slots, grid_origin_x, grid_origin_y, mx, my):
        """
        Dibuja un único botón de Save centrado debajo de los botones de Show.
        Devuelve un dict con el rect: 'save'
        """
        cols = 5
        rows = (len(slots) + cols - 1) // cols
        # Y position: debajo de Show buttons
        save_y = grid_origin_y + rows * (self.slot_size + self.margin) + self.margin + self.button_size[1] + self.margin
        # Botón de ancho doble (dos botones originales + margen)
        total_width = self.button_size[0] * 2 + self.margin
        btn_x = grid_origin_x
        # Definir rect y dibujar
        self.save_rect = pygame.Rect(btn_x, save_y, total_width, self.button_size[1])
        pygame.draw.rect(overlay, (100, 100, 100), self.save_rect)
        border_color = (255, 255, 0) if self.save_rect.collidepoint(mx, my) else (255, 255, 255)
        pygame.draw.rect(overlay, border_color, self.save_rect, 2)
        # Texto centrado
        txt = self.font.render("Save", True, (255, 255, 255))
        overlay.blit(txt, (btn_x + (total_width - txt.get_width()) // 2, save_y + (self.button_size[1] - txt.get_height()) // 2))
        return {'save': self.save_rect}


    def get_slot_index(self, pos, panel_rect, count):
        """
        Retorna el índice de slot bajo la posición `pos`, o None.
        """
        grid_origin_x, grid_origin_y = self._get_grid_origin(panel_rect)
        slot_size = self.slot_size
        margin = self.margin
        for i in range(count):
            col = i % 5
            row = i // 5
            rx = grid_origin_x + col * (slot_size + margin)
            ry = grid_origin_y + row * (slot_size + margin)
            rect = pygame.Rect(rx, ry, slot_size, slot_size)
            if rect.collidepoint(pos):
                return i
        return None

    # Dibuja los botones "Add Item" y "Delete Item" debajo de los botones de guardar.
    def _draw_manage_buttons(self, overlay, slots, grid_origin_x, grid_origin_y, mx, my):
        """
        Dibuja los botones "Add Item" y "Delete Item" debajo de los botones de guardar.
        Devuelve un dict con los rects: 'add_item', 'delete_item'.
        """
        # Position manage buttons below grid
        cols = 5
        rows = (len(slots) + cols - 1) // cols
        manage_y = grid_origin_y + rows * (self.slot_size + self.margin) + self.margin
        rects = {}

        # Add Item
        self.add_item_rect = pygame.Rect(grid_origin_x, manage_y, *self.button_size)
        pygame.draw.rect(overlay, (100, 100, 100), self.add_item_rect)
        border_color = (255, 255, 0) if self.add_item_rect.collidepoint(mx, my) else (255, 255, 255)
        pygame.draw.rect(overlay, border_color, self.add_item_rect, 2)
        txt_add = self.font.render("Add Item", True, (255, 255, 255))
        overlay.blit(txt_add, (grid_origin_x + 10, manage_y + 5))
        rects['add_item'] = self.add_item_rect

        # Delete Item
        del_x = grid_origin_x + self.button_size[0] + self.margin
        self.delete_item_rect = pygame.Rect(del_x, manage_y, *self.button_size)
        # Colorear borde en modo delete
        if self.delete_mode_active:
            border_color = (255, 0, 0)
        elif self.delete_item_rect.collidepoint(mx, my):
            border_color = (255, 255, 0)
        else:
            border_color = (255, 255, 255)
        pygame.draw.rect(overlay, (100, 100, 100), self.delete_item_rect)

        pygame.draw.rect(overlay, border_color, self.delete_item_rect, 2)
        txt_del = self.font.render("Delete Item", True, (255, 255, 255))
        overlay.blit(txt_del, (del_x + 10, manage_y + 5))
        rects['delete_item'] = self.delete_item_rect
        return rects
