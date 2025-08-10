import pygame
from typing import Any, Dict
from roguelike_editors.items.items_title_panel.items_title_view import ItemsTitleView

class ItemPickerPanelView:
    """
    Renderiza la UI del editor de ítems: overlay, barra de título y PickerPanel.
    El panel de propiedades es responsabilidad de ItemsPropertiesPanel.
    """
    def __init__(self, assets: Dict[str, pygame.Surface], font: pygame.font.Font):
        # Diccionario de superficies de Pygame para cada ID de ítem
        self.assets = assets
        # Fuente tipográfica para renderizado de texto
        self.font = font
        # Professional title bar (lazy state binding)
        self.title_view: ItemsTitleView | None = None
        # PickerPanel bridge (injected by controller)
        self.picker = None
        self.picker_state = None

    # ===== Métodos de dibujo modularizados =====
    def _draw_overlay(self, screen: pygame.Surface) -> None:
        """
        Dibuja un fondo semitransparente que atenúa la escena principal.
        """
        overlay = pygame.Surface(screen.get_size(), pygame.SRCALPHA)
        overlay.fill((0, 0, 0, 180))  # Negro con 180/255 alfa
        screen.blit(overlay, (0, 0))

    def draw(self, screen: pygame.Surface, model: Any) -> None:
        """
        Punto de entrada para renderizar la vista completa.
        """
        if not model.visible:
            return
        # Dim background first
        self._draw_overlay(screen)
        # Ensure title view is bound to current state and render title at top-left above overlay
        if self.title_view is None:
            self.title_view = ItemsTitleView(None, model)
        else:
            self.title_view.state = model
        self.title_rect = self.title_view.render(screen)
        # --- PickerPanel rendering ---
        if self.picker and self.picker_state:
            sw, sh = screen.get_size()
            margin = 20
            # Reservar espacio inferior para panel de lista y params (mismo cálculo que controller)
            params_h = sh // 4
            list_h = sh // 4
            reserve_h = params_h + list_h + 2 * margin
            grid_top = max(margin, (self.title_rect.bottom + 10) if self.title_rect else margin)
            grid_h = max(0, sh - grid_top - margin - reserve_h)
            self.picker_state.visible = True
            self.picker_state.rect = pygame.Rect(margin, grid_top, max(0, sw - 2 * margin), grid_h)
            # Sincronizar selección del modelo hacia el panel (si existe)
            # para mantener resalte cuando seleccionamos desde otras UI (map_ui)
            if getattr(model, 'selected_item_id', None) is not None:
                item_ids = [i for i in model.items.keys() if i != "image_item_not_found"]
                try:
                    self.picker_state.selected_index = item_ids.index(model.selected_item_id)
                except ValueError:
                    pass
            # Renderizar la grilla
            self.picker.render(screen, self.picker_state)
            # Mapear hover del panel hacia el modelo para info panel
            if self.picker_state.hovered_index is not None:
                item_ids = [i for i in model.items.keys() if i != "image_item_not_found"]
                if 0 <= self.picker_state.hovered_index < len(item_ids):
                    model.hovered_item_id = item_ids[self.picker_state.hovered_index]
                else:
                    model.hovered_item_id = None
            else:
                model.hovered_item_id = None
        # Info panel y edición inline ahora son responsabilidad de ItemsPropertiesPanel
        return
