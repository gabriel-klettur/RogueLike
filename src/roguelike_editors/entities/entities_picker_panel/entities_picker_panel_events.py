import pygame

class EntitiesPickerEventHandler:
    """
    Manejador de eventos para el editor de entidades.
    """
    def __init__(self, controller):
        self.controller = controller
        self.model = controller.model
        self.view = controller.view

    def handle(self, event: pygame.event.Event) -> None:
        # Inicio de arrastre con botón derecho en cualquier parte del panel
        if event.type == pygame.MOUSEBUTTONDOWN and event.button == 3 and self.model.visible:
            if self.model.panel_rect and self.model.panel_rect.collidepoint(event.pos):
                self.view.draggable_panel.handle_event(event, header_rect=self.model.panel_rect)
                return
        # Drag move
        if event.type == pygame.MOUSEMOTION and self.view.draggable_panel.dragging:
            self.view.draggable_panel.handle_event(event)
            return
        # Drag end
        if event.type == pygame.MOUSEBUTTONUP and self.view.draggable_panel.dragging:
            self.view.draggable_panel.handle_event(event)
            return
        if event.type == pygame.KEYDOWN:
            if event.key == pygame.K_F5:
                self.model.visible = not self.model.visible
                self.model.selected_id = None
                return
            if not self.model.visible:
                return
            if event.key == pygame.K_UP:
                self.model.scroll_index = max(0, self.model.scroll_index - 1)
                return
            if event.key == pygame.K_DOWN:
                self.model.scroll_index += 1
                return

        if event.type == pygame.MOUSEBUTTONDOWN and self.model.visible and event.button == 1:
            mx, my = event.pos
            # Grid click detection with view offsets
            ox, oy = self.view.x, self.view.y
            margin = self.view.margin
            cell_size = self.view.cell_size
            tm = self.view.text_margin
            fh = self.view.font.get_height()
            ch = cell_size + tm + fh
            cols = self.view.columns
            mx_rel = mx - (ox + margin)
            my_rel = my - (oy + margin)
            if mx_rel < 0 or my_rel < 0:
                self.model.selected_id = None
            else:
                col = mx_rel // (cell_size + margin)
                row = my_rel // (ch + margin) + self.model.scroll_index
                entity_ids = list(self.model.player_stats.keys()) + list(self.model.monsters.keys())
                idx = row * cols + col
                x0 = ox + margin + col * (cell_size + margin)
                y0 = oy + margin + (row - self.model.scroll_index) * (ch + margin)
                if 0 <= col < cols and 0 <= idx < len(entity_ids) and x0 <= mx <= x0 + cell_size and y0 <= my <= y0 + cell_size:
                    self.model.selected_id = entity_ids[idx]
                else:
                    self.model.selected_id = None
            return

        if event.type == pygame.MOUSEMOTION and self.model.visible:
            mx, my = event.pos
            # Grid hover detection with view offsets
            ox, oy = self.view.x, self.view.y
            margin = self.view.margin
            cell_size = self.view.cell_size
            tm = self.view.text_margin
            fh = self.view.font.get_height()
            ch = cell_size + tm + fh
            cols = self.view.columns
            mx_rel = mx - (ox + margin)
            my_rel = my - (oy + margin)
            if mx_rel < 0 or my_rel < 0:
                self.model.hovered_id = None
            else:
                col = mx_rel // (cell_size + margin)
                row = my_rel // (ch + margin) + self.model.scroll_index
                entity_ids = list(self.model.player_stats.keys()) + list(self.model.monsters.keys())
                idx = row * cols + col
                x0 = ox + margin + col * (cell_size + margin)
                y0 = oy + margin + (row - self.model.scroll_index) * (ch + margin)
                if 0 <= col < cols and 0 <= idx < len(entity_ids) and x0 <= mx <= x0 + cell_size and y0 <= my <= y0 + cell_size:
                    self.model.hovered_id = entity_ids[idx]
                else:
                    self.model.hovered_id = None
            return

        # reset hover
        self.model.hovered_id = None
