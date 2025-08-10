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
        Maneja clicks en la lista. Para 'monsters', un solo click sobre 'Pos:' inicia un
        "press-and-hold": se oculta el Inventory Editor, centra la cámara en el monstruo
        mientras se mantenga presionado, y al soltar se restaura el estado.
        """
        if event.type == pygame.MOUSEBUTTONDOWN and event.button == 1:
            mx, my = event.pos
            if self.view.panel_rect.collidepoint(mx, my):
                # Solo categoría 'monsters' tiene lógica compleja
                if self.model.current_category == 'monsters':
                    # En modo 'Show Default' mostramos plantillas; permitir seleccionar template
                    if getattr(self.editor_controller.model, 'editing_side', 'active') == 'default':
                        line_h = self.view.font.get_linesize()
                        idx = (my - self.view.panel_rect.y + self.view.list_view.scroll_panel.scroll_offset) // line_h
                        items = self.controller.get_items_list()
                        if idx < 0 or idx >= len(items):
                            return True
                        # Identificar la línea raíz (no indentada) del grupo clicado
                        start_idx = idx
                        while start_idx > 0 and items[start_idx].startswith(' '):
                            start_idx -= 1
                        root = items[start_idx].strip()
                        # Extraer Template ID y guardarlo en el modelo
                        if 'Template:' in root:
                            try:
                                tid = root.split('Template:')[1].strip()
                                self.editor_controller.model.selected_default_template_id = tid
                            except Exception:
                                pass
                        return True
                    line_h = self.view.font.get_linesize()
                    idx = (my - self.view.panel_rect.y + self.view.list_view.scroll_panel.scroll_offset) // line_h
                    items = self.controller.get_items_list()
                    if idx < 0 or idx >= len(items):
                        return False
                    # Click sobre 'Pos:' soporta doble clic (salto instantáneo) o press-and-hold
                    line_text = items[idx].lstrip()
                    if line_text.startswith('Pos:'):
                        coord_text = items[idx].strip().split('Pos:')[1].strip()
                        coords = coord_text.strip('()').split(',')
                        try:
                            x, y = float(coords[0].strip()), float(coords[1].strip())
                        except (ValueError, IndexError):
                            x = y = None
                        if x is not None:
                            now = pygame.time.get_ticks()
                            is_double = (idx == self.last_pos_click_idx) and (now - self.last_pos_click_time <= self.double_click_ms)
                            # Seleccionar entidad del grupo (línea raíz previa)
                            temp_idx = idx
                            while temp_idx > 0 and items[temp_idx].startswith(' '):
                                temp_idx -= 1
                            eid_raw = items[temp_idx].strip().split(' ')[0]
                            self.controller.select_entity(eid_raw)
                            self.editor_controller.model.editing_side = 'active'

                            if is_double:
                                # Doble clic: fijar objetivo de cámara persistente
                                target = SimpleNamespace(x=x, y=y)
                                # Guardar en el modelo para que tests puedan asertar
                                setattr(self.model, 'camera_focus_target', target)
                                self.editor_controller.game.camera.update(target)
                                # No activar press-and-hold en doble clic
                                self.last_pos_click_time = 0
                                self.last_pos_click_idx = -1
                                return True
                            else:
                                # Un solo clic: iniciar press-and-hold
                                target = SimpleNamespace(x=x, y=y)
                                self.editor_controller.game.camera.update(target)
                                self.editor_controller.model.overlay_hidden_while_hold = True
                                self.editor_controller.model.holding_pos_focus = True
                                # Registrar para posible doble clic
                                self.last_pos_click_time = now
                                self.last_pos_click_idx = idx
                                return True
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
                elif self.model.current_category == 'player':
                    # En modo 'Show Default' y categoría player, permitir seleccionar la clase
                    if getattr(self.editor_controller.model, 'editing_side', 'active') == 'default':
                        line_h = self.view.font.get_linesize()
                        idx = (my - self.view.panel_rect.y + self.view.list_view.scroll_panel.scroll_offset) // line_h
                        items = self.controller.get_items_list()
                        if idx < 0 or idx >= len(items):
                            return True
                        # Identificar la línea raíz del grupo clicado
                        start_idx = idx
                        while start_idx > 0 and items[start_idx].startswith(' '):
                            start_idx -= 1
                        root = items[start_idx].strip()
                        if root.startswith('Class:'):
                            try:
                                cls_name = root.split('Class:')[1].strip()
                                # Si hay separadores adicionales ej. "Class: name | Capacity: N"
                                if '|' in cls_name:
                                    cls_name = cls_name.split('|')[0].strip()
                                self.editor_controller.model.selected_default_player_class = cls_name
                            except Exception:
                                pass
                        return True
                elif self.model.current_category == 'map':
                    # Click en línea con posición @(<x>,<y>): centrar cámara mientras se mantenga el click
                    line_h = self.view.font.get_linesize()
                    idx = (my - self.view.panel_rect.y + self.view.list_view.scroll_panel.scroll_offset) // line_h
                    items = self.controller.get_items_list()
                    if 0 <= idx < len(items):
                        line_text = items[idx]
                        if '@(' in line_text and ')' in line_text:
                            try:
                                coords_txt = line_text.split('@(')[1].split(')')[0]
                                xs, ys = coords_txt.split(',')
                                x, y = float(xs.strip()), float(ys.strip())
                                target = SimpleNamespace(x=x, y=y)
                                self.editor_controller.game.camera.update(target)
                                self.editor_controller.model.overlay_hidden_while_hold = True
                                self.editor_controller.model.holding_pos_focus = True
                                return True
                            except Exception:
                                # Si no se puede parsear, ignorar
                                pass
                    return True
                # Bloquear clic dentro del panel de listado
                return True
        # Al soltar el click izquierdo, restaurar overlay y cámara si veníamos de press-and-hold
        if event.type == pygame.MOUSEBUTTONUP and event.button == 1:
            if self.editor_controller.model.holding_pos_focus:
                # Mostrar nuevamente el overlay
                self.editor_controller.model.overlay_hidden_while_hold = False
                self.editor_controller.model.holding_pos_focus = False
                # Volver a centrar la cámara en el jugador (normalidad)
                player_eid = getattr(self.editor_controller.world, 'player_entity', None)
                pos_map = self.editor_controller.world.components.get('Position', {})
                if player_eid in pos_map:
                    pos = pos_map[player_eid]
                    self.editor_controller.game.camera.update(SimpleNamespace(x=pos.x, y=pos.y))
                return True
        return False
