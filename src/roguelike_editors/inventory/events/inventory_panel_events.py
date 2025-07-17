import pygame
from types import SimpleNamespace
from roguelike_editors.inventory.controller.inventory_panel_controller import InventoryPanelController
from roguelike_editors.inventory.view.inventory_panel_view import InventoryPanelView
from roguelike_editors.inventory.model.inventory_panel_model import InventoryPanelModel

class InventoryPanelEventHandler:
    """
    Manejador de eventos para el panel izquierdo (tabs + listado).
    """
    def __init__(self, editor_controller, controller: InventoryPanelController, view: InventoryPanelView, model: InventoryPanelModel):
        self.editor_controller = editor_controller
        self.controller = controller
        self.view = view
        self.model = model
        # Double-click tracking
        self.last_pos_click_time = 0
        self.last_pos_click_idx = -1
        self.double_click_ms = 800

    def handle(self, event):
        # Recentrar cámara si estaba en enfoque de monstrue
        if self.editor_controller.model.camera_focus_target is not None and event.type in (pygame.MOUSEBUTTONDOWN, pygame.MOUSEBUTTONUP, pygame.MOUSEMOTION, pygame.MOUSEWHEEL):
            # Centrar de nuevo al jugador
            player_eid = self.editor_controller.world.player_entity
            pos_map = self.editor_controller.world.components.get('Position', {})
            if player_eid in pos_map:
                pos = pos_map[player_eid]
                self.editor_controller.game.camera.update(SimpleNamespace(x=pos.x, y=pos.y))
            self.editor_controller.model.camera_focus_target = None
        # Click izquierdo
        if event.type == pygame.MOUSEBUTTONDOWN and event.button == 1:
            mx, my = event.pos
            # Tabs
            for rect, cat in self.view.tab_rects:
                if rect.collidepoint(mx, my):
                    self.controller.change_category(cat)
                    self.editor_controller.model.current_category = cat
                    return True
            # Listado
            if self.view.panel_rect.collidepoint(mx, my):
                if self.model.current_category == 'monsters':
                    line_h = self.view.font.get_linesize()
                    idx = (my - self.view.panel_rect.y + self.view.scroll_panel.scroll_offset) // line_h
                    items = self.controller.get_items_list()
                    if idx < 0 or idx >= len(items):
                        return False
                    print(f"[DEBUG PanelEvent] idx={idx}, item={items[idx]!r}, clicks={getattr(event, 'clicks', None)}, event_dict={event.__dict__}")
                    # Manual double-click detection
                    now = pygame.time.get_ticks()
                    print(f"[DEBUG PanelEvent] dt={(now - self.last_pos_click_time)}ms, last_idx={self.last_pos_click_idx}, threshold={self.double_click_ms}")
                    if idx == self.last_pos_click_idx and (now - self.last_pos_click_time) <= self.double_click_ms and items[idx].lstrip().startswith('Pos:'):
                        print(f"[DEBUG PanelEvent] Detected double-click on idx={idx}")
                        # Parse coordinates
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
                        # Select entity group
                        temp_idx = idx
                        while temp_idx > 0 and items[temp_idx].startswith(' '):
                            temp_idx -= 1
                        eid_raw = items[temp_idx].strip().split(' ')[0]
                        self.controller.select_entity(eid_raw)
                        return True
                    # Update click tracking for single-click
                    self.last_pos_click_time = now
                    self.last_pos_click_idx = idx
                    # Detección de doble clic en Pos
                    if False and getattr(event, 'clicks', 1) == 2 and items[idx].lstrip().startswith('Pos:'):
                        # Parsear coordenadas
                        coord_text = items[idx].strip().split('Pos:')[1].strip()
                        coords = coord_text.strip('()').split(',')
                        try:
                            x = float(coords[0].strip()); y = float(coords[1].strip())
                        except (ValueError, IndexError):
                            x = y = None
                        if x is not None:
                            target = SimpleNamespace(x=x, y=y)
                            self.editor_controller.game.camera.update(target)
                            self.editor_controller.model.camera_focus_target = target
                        # Seleccionar entidad del grupo
                        temp_idx = idx
                        while temp_idx > 0 and items[temp_idx].startswith(' '):
                            temp_idx -= 1
                        eid_raw = items[temp_idx].strip().split(' ')[0]
                        self.controller.select_entity(eid_raw)
                        return True
                    if 0 <= idx < len(items):
                        # Encontrar inicio del grupo
                        start_idx = idx
                        while start_idx > 0 and items[start_idx].startswith(' '):
                            start_idx -= 1
                        raw = items[start_idx].strip()
                        eid = raw.split(' ')[0]
                        self.controller.select_entity(eid)
                        return True
                # Bloquear clic en otras partes del panel de listado
                return True
        # Bloquear hovers dentro del panel
        if event.type == pygame.MOUSEMOTION:
            mx, my = event.pos
            if any(rect.collidepoint(mx, my) for rect, _ in self.view.tab_rects) or self.view.panel_rect.collidepoint(mx, my):
                return True
        return False