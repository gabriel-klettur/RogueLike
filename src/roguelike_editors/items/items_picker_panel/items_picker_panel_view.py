import pygame
import logging
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
        # Debug snapshots to avoid log spam
        self._last_grid_rect = None
        self._last_reserved_h = None

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
        # Nota: Se elimina el overlay global para no oscurecer todo el editor
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
            # Reservar espacio inferior exacto si el orquestador lo proporciona
            reserve_h = getattr(self, '_reserved_bottom_h', None)
            if reserve_h is None:
                # Fallback: aproximación por fracciones si no fue inyectado
                params_h = sh // 4
                list_h = sh // 4
                reserve_h = params_h + list_h + 2 * margin
            grid_top = max(margin, (self.title_rect.bottom + 10) if self.title_rect else margin)
            grid_h = max(0, sh - grid_top - margin - reserve_h)
            self.picker_state.visible = True
            # Calcular ancho profesional/estético basado en celdas del PickerPanel
            cw = getattr(self.picker, 'cell_w', 64)
            pad = getattr(self.picker, 'padding', 8)
            panel_m = getattr(self.picker, 'margin', 8)
            max_cols = getattr(self.picker, 'max_columns', None)
            # Ancho disponible descontando márgenes externos e internos del panel
            avail_w = max(0, sw - 2 * margin - 2 * panel_m)
            cols_fit = max(1, (avail_w + pad) // (cw + pad))
            if max_cols:
                cols_fit = min(cols_fit, max_cols)
            grid_area_w = cols_fit * cw + max(0, (cols_fit - 1) * pad)
            panel_w = grid_area_w + 2 * panel_m
            rect_w = min(panel_w, max(0, sw - 2 * margin))
            self.picker_state.rect = pygame.Rect(margin, grid_top, rect_w, grid_h)
            # Log only when values change
            try:
                rect_changed = (self._last_grid_rect != self.picker_state.rect)
                reserve_changed = (self._last_reserved_h != reserve_h)
            except Exception:
                rect_changed = True
                reserve_changed = True
            if rect_changed or reserve_changed:
                logging.getLogger(__name__).debug(
                    f"[ItemPickerPanelView] grid_rect={self.picker_state.rect} reserve_h={reserve_h} title_bottom={(self.title_rect.bottom if self.title_rect else None)}"
                )
                # Store snapshots
                self._last_grid_rect = self.picker_state.rect.copy()
                self._last_reserved_h = reserve_h
            # Fondo semitransparente del panel (solo detrás del picker)
            panel_rect = self.picker_state.rect
            if panel_rect and panel_rect.w > 0 and panel_rect.h > 0:
                bg = pygame.Surface(panel_rect.size, pygame.SRCALPHA)
                bg.fill((20, 20, 20, 180))
                screen.blit(bg, panel_rect.topleft)
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
