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
        pass

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
