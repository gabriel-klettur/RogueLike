import json, os
import pygame
from roguelike_ui.widgets.list_panel_ui import ListPanelUI

class MapItemsUI:
    """
    Widget para listar y seleccionar instancias de ítems en el mapa.
    """
    def __init__(self, font: pygame.font.Font, json_path: str):
        self.font = font
        self.json_path = json_path
        self.data: dict = {}
        self.list_ui = ListPanelUI(font)
        self.selected_instance: str | None = None

    def load(self):
        try:
            with open(self.json_path, 'r') as f:
                self.data = json.load(f)
        except Exception:
            self.data = {}
        display = []
        for inst_id, inst in self.data.items():
            # Mostrar posición de los pies o coordenadas de tile si falta posición
            if 'position' in inst:
                coords = inst['position']
                x = coords.get('x')
                y = coords.get('y')
            else:
                tile = inst.get('tile', {})
                x = tile.get('x')
                y = tile.get('y')
            display.append(f"{inst_id}: {inst.get('item_id')} @({x},{y})")
        self.list_ui.set_items(display)

    def draw(self, surface: pygame.Surface, rect: pygame.Rect) -> None:
        self.load()
        self.list_ui.draw(surface, rect)
        # Dibujar borde naranja alrededor de las coordenadas @(...)
        mx, my = pygame.mouse.get_pos()
        idx = self.list_ui.get_selected((mx, my))
        if idx is not None and 0 <= idx < len(self.list_ui.items):
            text = self.list_ui.items[idx]
            if '@(' in text and ')' in text:
                try:
                    start = text.find('@(')
                    end = text.find(')', start)
                    if end != -1:
                        prefix = text[:start]
                        coords = text[start:end+1]
                        line_h = self.font.get_linesize()
                        y0 = self.list_ui.rect.y - self.list_ui.panel.scroll_offset
                        text_x = self.list_ui.rect.x + self.list_ui.panel.margin
                        prefix_w = self.font.size(prefix)[0]
                        coords_w = self.font.size(coords)[0]
                        pos_r = pygame.Rect(text_x + prefix_w, y0 + idx * line_h, max(coords_w, 1), line_h)
                        pygame.draw.rect(surface, (255, 165, 0), pos_r, 2)
                except Exception:
                    pass

    def handle_event(self, event: pygame.event.Event) -> str | None:
        # Forward scroll/wheel events to list_ui
        self.list_ui.handle_event(event)
        if event.type == pygame.MOUSEBUTTONDOWN and event.button == 1:
            idx = self.list_ui.get_selected(event.pos)
            if idx is not None:
                inst_id = list(self.data.keys())[idx]
                self.selected_instance = inst_id
                return inst_id
        return None
