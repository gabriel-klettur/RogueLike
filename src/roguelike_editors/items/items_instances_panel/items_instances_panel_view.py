import pygame
import logging
from typing import Any

from .items_instances_panel_model import ItemsInstancesPanelModel


class ItemsInstancesPanelView:
    """
    Renderiza el panel inferior compuesto por:
    - Lista de instancias de ítems en el mapa
    - Editor de parámetros de la instancia seleccionada
    """
    def __init__(self) -> None:
        pass

    def layout(self, screen: pygame.Surface, model: ItemsInstancesPanelModel) -> None:
        sw, sh = screen.get_size()
        margin = model.margin
        params_h = int(sh * model.params_h_frac)
        list_h = int(sh * model.list_h_frac)
        params_rect = pygame.Rect(margin, sh - margin - params_h, sw - 2 * margin, params_h)
        list_rect = pygame.Rect(margin, params_rect.y - margin - list_h, sw - 2 * margin, list_h)
        model.params_rect = params_rect
        model.list_rect = list_rect
        # Debug
        logging.getLogger(__name__).debug(f"[InstancesPanelView.layout] sw={sw} sh={sh} list_rect={list_rect} params_rect={params_rect}")

    def draw(self, screen: pygame.Surface, model: ItemsInstancesPanelModel, map_ui: Any, params_ui: Any) -> None:
        if not model.visible:
            return
        # Calcular layout
        self.layout(screen, model)
        # Dibujar fondos semitransparentes y bordes para visibilidad
        list_rect = model.list_rect
        params_rect = model.params_rect
        logging.getLogger(__name__).debug(f"[InstancesPanelView.draw] visible={model.visible} list_rect={list_rect} params_rect={params_rect}")
        if list_rect:
            bg = pygame.Surface(list_rect.size, pygame.SRCALPHA)
            bg.fill((20, 20, 20, 180))
            screen.blit(bg, list_rect.topleft)
            pygame.draw.rect(screen, (255, 0, 0), list_rect, 3)
        if params_rect:
            bg2 = pygame.Surface(params_rect.size, pygame.SRCALPHA)
            bg2.fill((20, 20, 20, 180))
            screen.blit(bg2, params_rect.topleft)
            pygame.draw.rect(screen, (0, 255, 0), params_rect, 3)

        # Etiquetas de sección y rects de contenido sin solaparse con título
        header_pad = 6
        header_h = 0
        if map_ui and hasattr(map_ui, 'font'):
            label_font = map_ui.font
            list_label = label_font.render("Instancias en mapa", True, (255, 255, 0))
            if list_rect:
                screen.blit(list_label, (list_rect.x + 8, list_rect.y + 4))
                header_h = max(header_h, list_label.get_height() + header_pad)
            params_label = label_font.render("Parámetros", True, (255, 255, 0))
            if params_rect:
                screen.blit(params_label, (params_rect.x + 8, params_rect.y + 4))
                header_h = max(header_h, params_label.get_height() + header_pad)
        # Big debug labels (temporary)
        dbg_font = pygame.font.SysFont(None, 28)
        if list_rect:
            txt = dbg_font.render("[INSTANCES LIST]", True, (255, 50, 50))
            screen.blit(txt, (list_rect.centerx - txt.get_width()//2, list_rect.centery - txt.get_height()//2))
        if params_rect:
            txt2 = dbg_font.render("[PARAMS PANEL]", True, (50, 255, 50))
            screen.blit(txt2, (params_rect.centerx - txt2.get_width()//2, params_rect.centery - txt2.get_height()//2))

        # Contenido: inset para dejar espacio del encabezado
        if map_ui and list_rect:
            content_list_rect = pygame.Rect(list_rect.x + 6, list_rect.y + header_h, list_rect.width - 12, list_rect.height - header_h - 6)
            map_ui.draw(screen, content_list_rect)
        # Dibujar params (placeholder si no hay selección)
        if params_rect:
            if params_ui and getattr(map_ui, 'selected_instance', None):
                content_params_rect = pygame.Rect(params_rect.x + 6, params_rect.y + header_h, params_rect.width - 12, params_rect.height - header_h - 6)
                params_ui.draw(screen, content_params_rect)
            else:
                # Hint cuando no hay selección
                hint_font = map_ui.font if map_ui and hasattr(map_ui, 'font') else pygame.font.SysFont(None, 18)
                hint = hint_font.render("Selecciona una instancia para editar parámetros", True, (200, 200, 200))
                screen.blit(hint, (params_rect.x + 8, params_rect.y + (params_rect.height // 2) - 10))
