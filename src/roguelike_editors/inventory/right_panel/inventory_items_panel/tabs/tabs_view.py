import pygame

class TabsView:
    """
    Vista para renderizar las pestañas de inventario (default/active).
    """
    def __init__(self, font, button_size, margin):
        self.font = font
        self.button_size = button_size
        self.margin = margin

    def draw_tabs(self, overlay, grid_origin_x, grid_origin_y, mx, my, active_tab, slots_count):
        """Dibuja las pestañas de default/active"""
        # Esta funcionalidad ya está en buttons_view como show_buttons
        # Mantenemos por compatibilidad pero delegamos
        cols = 5
        rows = (slots_count + cols - 1) // cols
        show_y = grid_origin_y - self.button_size[1] - self.margin
        rects = {}

        # Show Default
        show_default_rect = pygame.Rect(grid_origin_x, show_y, *self.button_size)
        pygame.draw.rect(overlay, (100, 100, 100), show_default_rect)
        border_color = (255, 255, 0) if (active_tab == 'default' or show_default_rect.collidepoint(mx, my)) else (255, 255, 255)
        pygame.draw.rect(overlay, border_color, show_default_rect, 2)
        txt_def = self.font.render("Show Default", True, (255, 255, 255))
        overlay.blit(txt_def, (grid_origin_x + 10, show_y + 5))
        rects['show_default'] = show_default_rect

        # Show Active
        act_x = grid_origin_x + self.button_size[0] + 10
        show_active_rect = pygame.Rect(act_x, show_y, *self.button_size)
        pygame.draw.rect(overlay, (100, 100, 100), show_active_rect)
        border_color = (255, 255, 0) if (active_tab == 'active' or show_active_rect.collidepoint(mx, my)) else (255, 255, 255)
        pygame.draw.rect(overlay, border_color, show_active_rect, 2)
        txt_act = self.font.render("Show Active", True, (255, 255, 255))
        overlay.blit(txt_act, (act_x + 10, show_y + 5))
        rects['show_active'] = show_active_rect

        return rects

    def get_slots_data(self, model):
        """Obtiene los datos de slots según el modelo y pestaña activa"""
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
