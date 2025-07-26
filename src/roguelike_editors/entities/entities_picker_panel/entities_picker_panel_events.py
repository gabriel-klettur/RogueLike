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
            screen = pygame.display.get_surface()
            sw, sh = screen.get_size() if screen else (0, 0)
            margin = 20; cell_size = 64; tm = 4; fh = self.view.font.get_height(); ch = cell_size + tm + fh; cols = 12
            if mx < margin or my < margin:
                self.model.selected_id = None
            else:
                col = (mx - margin) // (cell_size + margin)
                row = (my - margin + self.model.scroll_index * (ch + margin)) // (ch + margin)
                entity_ids = list(self.model.player_stats.keys()) + list(self.model.monsters.keys())
                idx = row * cols + col
                x0 = margin + col * (cell_size + margin)
                y0 = margin + (row - self.model.scroll_index) * (ch + margin)
                if 0 <= col < cols and 0 <= idx < len(entity_ids) and x0 <= mx <= x0 + cell_size and y0 <= my <= y0 + cell_size:
                    self.model.selected_id = entity_ids[idx]
                else:
                    self.model.selected_id = None
            return

        if event.type == pygame.MOUSEMOTION and self.model.visible:
            mx, my = event.pos
            margin = 20; cell_size = 64; tm = 4; fh = self.view.font.get_height(); ch = cell_size + tm + fh; cols = 12
            if mx < margin or my < margin:
                self.model.hovered_id = None
            else:
                col = (mx - margin) // (cell_size + margin)
                row = (my - margin + self.model.scroll_index * (ch + margin)) // (ch + margin)
                entity_ids = list(self.model.player_stats.keys()) + list(self.model.monsters.keys())
                idx = row * cols + col
                x0 = margin + col * (cell_size + margin)
                y0 = margin + (row - self.model.scroll_index) * (ch + margin)
                if 0 <= col < cols and 0 <= idx < len(entity_ids) and x0 <= mx <= x0 + cell_size and y0 <= my <= y0 + cell_size:
                    self.model.hovered_id = entity_ids[idx]
                else:
                    self.model.hovered_id = None
            return

        # reset hover
        self.model.hovered_id = None
