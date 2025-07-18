import pygame
import json
import os


class InventoryItemsPanelEventHandler:
    """
    Manejador de eventos para flujo de añadir/eliminar ítems en el grid.
    """
    def __init__(self, grid_controller):
        self.controller = grid_controller
        self.model = grid_controller.model
        self.editor_view = grid_controller.editor_controller.view

    def handle(self, event):
        """
        Retorna True si el evento fue consumido por el flujo de add/delete.
        """
        # Handle delete quantity input
        if self.model.show_delete_mode:
            # Click en campo de cantidad
            if event.type == pygame.MOUSEBUTTONDOWN and event.button == 1:
                dq_input = self.editor_view.grid_view.delete_qty_input
                if hasattr(dq_input, 'last_rect') and dq_input.last_rect and dq_input.last_rect.collidepoint(event.pos):
                    dq_input.activate(initial_text=str(self.model.delete_quantity), select_all=True)
                    return True
            # Handle text input events for delete quantity
            if self.editor_view.grid_view.delete_qty_input.handle_event(event):
                try:
                    self.model.delete_quantity = int(self.editor_view.grid_view.delete_qty_input.text)
                except ValueError:
                    self.model.delete_quantity = 1
                return True
        # Modo Delete: toggle y acción
        if event.type == pygame.MOUSEBUTTONUP and event.button == 1:
            mx, my = event.pos
            # Toggle delete mode
            if hasattr(self.editor_view, 'delete_item_rect') and self.editor_view.delete_item_rect and self.editor_view.delete_item_rect.collidepoint(mx, my):
                # Toggle delete mode and init quantity input
                self.model.show_delete_mode = not self.model.show_delete_mode
                self.model.show_delete_quantity_input = self.model.show_delete_mode
                self.model.delete_quantity = 1
                # Prepare TextInput widget
                dq_input = self.editor_view.grid_view.delete_qty_input
                dq_input.text = str(self.model.delete_quantity)
                dq_input.cursor = len(dq_input.text)
                dq_input.selection_start = dq_input.cursor
                dq_input.selection_end = dq_input.cursor
                dq_input.active = False
                return True
            # Skip cancellation when clicking on quantity input
            dq_rect = getattr(self.editor_view.grid_view, 'delete_qty_input_rect', None)
            if dq_rect and dq_rect.collidepoint(mx, my):
                return True
            if self.model.show_delete_mode:
                # Obtener lista de slots actuales
                slots = self.editor_view.grid_view._get_slots(self.controller.editor_controller.model)
                # Calcular índice de slot dinámicamente
                idx = self.editor_view.grid_view.get_slot_index((mx, my), self.editor_view.left_panel_rect, len(slots))
                if idx is not None and slots[idx]:
                    self.controller.delete_item(idx, self.model.delete_quantity)
                # Desactivar modo delete
                self.model.show_delete_mode = False
                self.model.show_delete_quantity_input = False
                return True
        # MVC item selection panel event handling
        panel_model = getattr(self.editor_view, 'item_panel_model', None)
        
        panel_view = getattr(self.editor_view, 'item_panel_view', None)
        # Close MVC panel on click outside
        # Close MVC panel on click outside (mousedown or mouseup)
        if panel_model and panel_model.show_panel and panel_view and event.type in (pygame.MOUSEBUTTONDOWN, pygame.MOUSEBUTTONUP) and event.button == 1:
            mx, my = event.pos
            # If click outside the panel and header, close panel
            if not panel_view.panel_rect.collidepoint(mx, my) and not panel_view.header_rect.collidepoint(mx, my):
                panel_model.show_panel = False
                self.model.show_item_list = False
                return True
        if panel_model and panel_model.show_panel and panel_view:
            # Input focus for quantity input
            if event.type == pygame.MOUSEBUTTONDOWN and event.button == 1:
                if hasattr(panel_view, 'input_rect') and panel_view.input_rect.collidepoint(event.pos):
                    panel_view.text_input.activate(initial_text=str(panel_model.quantity), select_all=True)
                    return True
            # Handle text input events
            if panel_view.text_input.handle_event(event):
                try:
                    panel_model.quantity = int(panel_view.text_input.text)
                except ValueError:
                    panel_model.quantity = 0
                return True
            # Drag header
            if event.type == pygame.MOUSEBUTTONDOWN and event.button == 1:
                if panel_view.header_rect.collidepoint(event.pos):
                    panel_model.dragging = True
                    panel_model.drag_start_pos = pygame.Vector2(event.pos) - panel_model.drag_offset
                    return True
            if event.type == pygame.MOUSEMOTION and panel_model.dragging:
                panel_model.drag_offset = pygame.Vector2(event.pos) - panel_model.drag_start_pos
                return True
            if event.type == pygame.MOUSEBUTTONUP and event.button == 1 and panel_model.dragging:
                panel_model.dragging = False
                return True
            # Scroll wheel
            if event.type in (pygame.MOUSEBUTTONDOWN, pygame.MOUSEWHEEL):
                if panel_view.scroll_panel.handle_event(event):
                    return True
            # Tab click handling
            if event.type == pygame.MOUSEBUTTONUP and event.button == 1:
                mx, my = event.pos
                for rect, label in zip(panel_view.tab_rects, ['default', 'ground']):
                    if rect.collidepoint(mx, my):
                        panel_model.current_tab = label
                        panel_view.scroll_panel.scroll_offset = 0
                        return True

            # Consume hover inside panel to block underlying UI
            if event.type == pygame.MOUSEMOTION:
                if panel_view.panel_rect.collidepoint(event.pos):
                    return True
            # Consume clicks inside panel to block underlying UI
            if event.type == pygame.MOUSEBUTTONDOWN and event.button == 1:
                if panel_view.panel_rect.collidepoint(event.pos):
                    return True
            # Click selection or confirm
            if event.type == pygame.MOUSEBUTTONUP and event.button == 1:
                mx, my = event.pos
                panel_rect = panel_view.panel_rect

                # Confirm add
                btn_rect = panel_view.add_button_rect
                if btn_rect and btn_rect.collidepoint(mx, my):
                    # Confirm via panel controller
                    item, qty = self.editor_view.item_panel_controller.confirm()
                    # Add to grid
                    self.controller.select_item(item)
                    self.controller.confirm_quantity(qty)
                     # Si venimos de ground tab, remover item del suelo y guardar
                    if panel_model.current_tab == 'ground':
                        active_map = self.controller.editor_controller.model.active_data.get('map', {})
                        for key, entry in list(active_map.items()):
                            if entry.get('item_id') == item and entry.get('quantity', 0) >= qty:
                                remaining = entry.get('quantity', 0) - qty
                                if remaining > 0:
                                    entry['quantity'] = remaining
                                    active_map[key] = entry
                                else:
                                    active_map.pop(key)
                                break
                        # Persistir cambios de active_data a JSON
                        map_path = self.controller.editor_controller.paths['map']['active']
                        os.makedirs(os.path.dirname(map_path), exist_ok=True)
                        with open(map_path, 'w', encoding='utf-8') as f:
                            json.dump(self.controller.editor_controller.model.active_data.get('map', {}), f, ensure_ascii=False, indent=2)
                    panel_model.show_panel = False
                    return True

                # Select item in scroll list
                scroll_rect = panel_view.scroll_panel.rect
                if scroll_rect.collidepoint(mx, my):
                    line_h = self.editor_view.font.get_linesize()
                    idx = (my - scroll_rect.y + panel_view.scroll_panel.scroll_offset) // line_h
                    items = panel_view.scroll_panel.items
                    if 0 <= idx < len(items):
                        self.editor_view.item_panel_controller.select_item(items[idx])
                    return True

                # Consume other clicks inside panel
                if panel_rect.collidepoint(mx, my):
                    return True
        # Detectar click en 'Add Item'
        # Detectar click en 'Add Item'
        if event.type == pygame.MOUSEBUTTONUP and event.button == 1:
            mx, my = event.pos
            if hasattr(self.editor_view, 'add_item_rect') and self.editor_view.add_item_rect and not self.model.show_item_list and self.editor_view.add_item_rect.collidepoint(mx, my):
                self.controller.start_add_item()
                return True
        # Manejo arrastre del panel de ítems
        if self.model.show_item_list:
            if event.type == pygame.MOUSEBUTTONDOWN and event.button == 1:
                mx, my = event.pos
                header = getattr(self.editor_view, 'item_list_header_rect', None)
                if header and header.collidepoint(mx, my):
                    self.editor_view.item_list_dragging = True
                    self.editor_view.item_list_drag_start_pos = pygame.Vector2(mx, my) - self.editor_view.item_list_drag_offset
                    return True
            if event.type == pygame.MOUSEMOTION and getattr(self.editor_view, 'item_list_dragging', False):
                mx, my = event.pos
                self.editor_view.item_list_drag_offset = pygame.Vector2(mx, my) - self.editor_view.item_list_drag_start_pos
                return True
            if event.type == pygame.MOUSEBUTTONUP and event.button == 1 and getattr(self.editor_view, 'item_list_dragging', False):
                self.editor_view.item_list_dragging = False
                return True
        # Manejo scroll y selección de ítem
        if self.model.show_item_list:
            # scroll con rueda
            if event.type in (pygame.MOUSEBUTTONDOWN, pygame.MOUSEWHEEL):
                if panel_view.scroll_panel.handle_event(event):
                    return True
            if event.type == pygame.MOUSEBUTTONUP and event.button == 1:
                mx, my = event.pos
                panel_rect = getattr(self.editor_view, 'item_list_panel_rect', None)
                if panel_rect and panel_rect.collidepoint(mx, my):
                    # calcular índice de ítem
                    line_h = self.editor_view.font.get_linesize()
                    idx = (my - panel_rect.y + panel_view.scroll_panel.scroll_offset) // line_h
                    items = panel_view.scroll_panel.items
                    if 0 <= idx < len(items):
                        self.controller.select_item(items[idx])
                else:
                    self.model.show_item_list = False
                return True
        # Flujo de ingreso de cantidad
        if self.model.show_quantity_input:
            # Clic en botón 'Add to Inventory'
            if event.type == pygame.MOUSEBUTTONUP and event.button == 1:
                mx, my = event.pos
                btn_rect = getattr(self.editor_view, 'add_to_inventory_button_rect', None)
                if btn_rect and btn_rect.collidepoint(mx, my):
                    self.controller.confirm_quantity(self.model.quantity)
                    return True
            if event.type == pygame.KEYDOWN:
                if event.key == pygame.K_RETURN:
                    self.controller.confirm_quantity(self.model.quantity)
                    return True
                elif event.key == pygame.K_BACKSPACE:
                    qty_str = str(self.model.quantity)
                    qty_str = qty_str[:-1] if len(qty_str) > 1 else '1'
                    self.model.quantity = int(qty_str)
                    return True
                elif hasattr(event, 'unicode') and event.unicode.isdigit():
                    self.model.quantity = self.model.quantity * 10 + int(event.unicode)
                    return True
                elif event.key == pygame.K_ESCAPE:
                    self.model.show_quantity_input = False
                    self.model.show_item_list = False
                    return True
            # Consumir otros eventos mientras ingresa cantidad
            return True
        return False

   