import pygame
import logging
from typing import Any
from roguelike_ui.ui_blocker import register_blocker

from .items_instances_panel_model import ItemsInstancesPanelModel


class ItemsInstancesPanelView:
    """
    Renderiza el panel inferior compuesto por:
    - Lista de instancias de ítems en el mapa
    - Editor de parámetros de la instancia seleccionada
    """
    def __init__(self) -> None:
        # Snapshots to throttle logging
        self._last_list_rect: pygame.Rect | None = None
        self._last_params_rect: pygame.Rect | None = None
        self._last_visible: bool | None = None

    def layout(self, screen: pygame.Surface, model: ItemsInstancesPanelModel) -> None:
        sw, sh = screen.get_size()
        margin = model.margin
        # Nuevo layout: solo un panel inferior (lista de instancias)
        params_rect = None
        list_h = int(sh * model.list_h_frac)
        list_rect = pygame.Rect(margin, sh - margin - list_h, sw - 2 * margin, list_h)
        model.list_rect = list_rect
        model.params_rect = None
        # Debug only when rects change
        if (self._last_list_rect != list_rect) or (self._last_params_rect != params_rect):
            logging.getLogger(__name__).debug(
                f"[InstancesPanelView.layout] sw={sw} sh={sh} list_rect={list_rect} params_rect={params_rect}"
            )
            self._last_list_rect = list_rect.copy()
            self._last_params_rect = None

    def draw(self, screen: pygame.Surface, model: ItemsInstancesPanelModel, map_ui: Any, params_ui: Any) -> None:
        if not model.visible:
            return
        # Calcular layout
        self.layout(screen, model)
        # Dibujar fondos semitransparentes y bordes para visibilidad
        list_rect = model.list_rect
        params_rect = model.params_rect
        # Log only when visibility or rects change
        if (self._last_visible != model.visible) or (self._last_list_rect != list_rect) or (self._last_params_rect != params_rect):
            logging.getLogger(__name__).debug(
                f"[InstancesPanelView.draw] visible={model.visible} list_rect={list_rect} params_rect={params_rect}"
            )
            self._last_visible = model.visible
        if list_rect:
            bg = pygame.Surface(list_rect.size, pygame.SRCALPHA)
            bg.fill((20, 20, 20, 180))
            screen.blit(bg, list_rect.topleft)
            # Registrar como bloqueador de UI para evitar hover/drag debajo de la lista
            try:
                register_blocker(list_rect)
            except Exception:
                pass

        # Etiquetas de sección y rects de contenido sin solaparse con título
        header_pad = 6
        header_h = 0
        if map_ui and hasattr(map_ui, 'font'):
            label_font = map_ui.font
            list_label = label_font.render("Instancias en mapa", True, (255, 255, 0))
            if list_rect:
                screen.blit(list_label, (list_rect.x + 8, list_rect.y + 4))
                header_h = max(header_h, list_label.get_height() + header_pad)
        # Eliminar etiquetas y textos de debug grandes

        # Contenido: inset para dejar espacio del encabezado
        if map_ui and list_rect:
            content_list_rect = pygame.Rect(list_rect.x + 6, list_rect.y + header_h, list_rect.width - 12, list_rect.height - header_h - 6)
            map_ui.draw(screen, content_list_rect)
        # Panel de parámetros eliminado
