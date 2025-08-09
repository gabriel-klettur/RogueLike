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
        """Dibuja las pestañas de default/active con el mismo estilo que las del panel izquierdo."""
        # Coordenada Y: encima del grid
        show_y = grid_origin_y - max(self.button_size[1], 24) - self.margin
        rects = {}

        # Estilo alineado con left_panel.tabs.tabs_view
        padding = 10
        tab_gap = 5
        tab_x = grid_origin_x

        # Texto y medidas dinámicas
        txt_def = self.font.render("Show Default", True, (255, 255, 255))
        w_def, h_def = txt_def.get_size()
        def_rect = pygame.Rect(tab_x, show_y, w_def + padding * 2, h_def + padding // 2)
        # Relleno según activo
        def_fill = (100, 100, 100) if active_tab == 'default' else (50, 50, 50)
        pygame.draw.rect(overlay, def_fill, def_rect)
        # Borde blanco y, si activo, remarcar en amarillo (como en las pestañas izquierdas)
        pygame.draw.rect(overlay, (255, 255, 255), def_rect, 2)
        if active_tab == 'default':
            pygame.draw.rect(overlay, (255, 255, 0), def_rect, 2)
        # Texto centrado verticalmente con padding horizontal
        overlay.blit(txt_def, (def_rect.x + padding, def_rect.y + (def_rect.height - h_def) // 2))
        rects['show_default'] = def_rect

        # Siguiente pestaña
        tab_x += def_rect.width + tab_gap
        txt_act = self.font.render("Show Active", True, (255, 255, 255))
        w_act, h_act = txt_act.get_size()
        act_rect = pygame.Rect(tab_x, show_y, w_act + padding * 2, h_act + padding // 2)
        act_fill = (100, 100, 100) if active_tab == 'active' else (50, 50, 50)
        pygame.draw.rect(overlay, act_fill, act_rect)
        pygame.draw.rect(overlay, (255, 255, 255), act_rect, 2)
        if active_tab == 'active':
            pygame.draw.rect(overlay, (255, 255, 0), act_rect, 2)
        overlay.blit(txt_act, (act_rect.x + padding, act_rect.y + (act_rect.height - h_act) // 2))
        rects['show_active'] = act_rect

        return rects

    def get_slots_data(self, model):
        
        """Obtiene los datos de slots según el modelo y pestaña activa"""
        if model.editing_side == 'default':
            # Show default inventory templates
            if model.current_category == 'player':
                default_player = model.default_data.get('player', {}) or {}
                classes = default_player.get('classes')
                if isinstance(classes, dict) and classes:
                    sel_cls = getattr(model, 'selected_default_player_class', None)
                    if sel_cls in classes:
                        return classes[sel_cls].get('slots', []) or []
                    # Fallback: first available class
                    first_tpl = next(iter(classes.values()))
                    return first_tpl.get('slots', []) or []
                # Legacy single-template fallback
                slots = default_player.get('slots', [])
                return slots
            elif model.current_category == 'monsters':
                # Prefer explicit selection from left list (template_id)
                sel_tid = getattr(model, 'selected_default_template_id', None)
                if sel_tid:
                    for def_entry in model.default_data.get('monsters', {}).values():
                        if def_entry.get('template_id') == sel_tid:
                            inv_list = def_entry.get('inventory', [])
                            slots = [{'item': inv.get('item'), 'quantity': inv.get('min', 0)} for inv in inv_list]
                            return slots
                # Fallback: use template of selected active monster (if any)
                active_mon = model.active_data.get('monsters', {}).get(str(model.selected_eid), {})
                template_id = active_mon.get('template_id')
                for def_entry in model.default_data.get('monsters', {}).values():
                    if def_entry.get('template_id') == template_id:
                        inv_list = def_entry.get('inventory', [])
                        slots = [{'item': inv.get('item'), 'quantity': inv.get('min', 0)} for inv in inv_list]
                        return slots
            
            return []
        else:
            # Show active data from JSON
            active_data = model.active_data.get(model.current_category, {})
            entry = active_data.get(str(model.selected_eid), {})
            # Fallback for legacy single-entry player active JSON
            if not entry and model.current_category == 'player' and isinstance(active_data, dict) and active_data:
                try:
                    entry = next(iter(active_data.values())) or {}
                except Exception:
                    entry = {}
            slots = entry.get('slots', [])
            return slots
