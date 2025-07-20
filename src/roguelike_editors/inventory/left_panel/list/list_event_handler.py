import pygame
from types import SimpleNamespace


class ListEventHandler:
    """
    Manejador de eventos para la lista del panel izquierdo.
    """
    def __init__(self, editor_controller, controller, view, model):
        self.editor_controller = editor_controller
        self.controller = controller
        self.view = view
        self.model = model
        # Double-click tracking
        self.last_pos_click_time = 0
        self.last_pos_click_idx = -1
        self.double_click_ms = 800

    def handle(self, event):
        """
        Maneja clicks en la lista, incluyendo selección y doble-click en Pos.
        """
        if event.type == pygame.MOUSEBUTTONDOWN and event.button == 1:
            mx, my = event.pos
            if self.view.panel_rect.collidepoint(mx, my):
                # Solo categoría 'monsters' tiene lógica compleja
                if self.model.current_category == 'monsters':
                    line_h = self.view.font.get_linesize()
                    idx = (my - self.view.panel_rect.y + self.view.list_view.scroll_panel.scroll_offset) // line_h
                    items = self.controller.get_items_list()
                    if idx < 0 or idx >= len(items):
                        return False
                    # Debug prints
                    now = pygame.time.get_ticks()
                    # Detección manual de doble-click en Pos
                    if idx == self.last_pos_click_idx and (now - self.last_pos_click_time) <= self.double_click_ms and items[idx].lstrip().startswith('Pos:'):
                        coord_text = items[idx].strip().split('Pos:')[1].strip()
                        coords = coord_text.strip('()').split(',')
                        try:
                            x, y = float(coords[0].strip()), float(coords[1].strip())
                        except (ValueError, IndexError):
                            x = y = None
                        if x is not None:
                            target = SimpleNamespace(x=x, y=y)
                            self.editor_controller.game.camera.update(target)
                            self.editor_controller.model.camera_focus_target = target
                        # Reset click tracking
                        self.last_pos_click_time = 0
                        self.last_pos_click_idx = -1
                        # Seleccionar entidad del grupo
                        temp_idx = idx
                        while temp_idx > 0 and items[temp_idx].startswith(' '):
                            temp_idx -= 1
                        eid_raw = items[temp_idx].strip().split(' ')[0]
                        self.controller.select_entity(eid_raw)
                        self.editor_controller.model.editing_side = 'active'
                        return True
                    # Actualizar tracking de click
                    self.last_pos_click_time = now
                    self.last_pos_click_idx = idx
                    # Selección simple
                    if 0 <= idx < len(items):
                        start_idx = idx
                        while start_idx > 0 and items[start_idx].startswith(' '):
                            start_idx -= 1
                        raw = items[start_idx].strip()
                        eid = raw.split(' ')[0]
                        self.controller.select_entity(eid)
                        self.editor_controller.model.editing_side = 'active'
                        return True
                # Bloquear clic dentro del panel de listado
                return True
        return False
