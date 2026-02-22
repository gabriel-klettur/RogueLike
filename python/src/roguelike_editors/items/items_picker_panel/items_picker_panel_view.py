import pygame
import logging
from typing import Any, Dict
from roguelike_editors.items.items_title_panel.items_title_view import ItemsTitleView
from roguelike_ui.ui_blocker import register_blocker

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
        # El título del Items Editor se renderiza externamente (controller). Aquí solo usamos su rect.
        ext_title_rect = getattr(self, 'title_rect', None)
        if ext_title_rect is None:
            # Fallback: estimar un rect de título en (10,10) con alto basado en la fuente
            try:
                th = self.font.get_height() + 20
            except Exception:
                th = 40
            self.title_rect = pygame.Rect(10, 10, 0, th)
        else:
            self.title_rect = ext_title_rect
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
            # Si hay ancla superior (alinear con toolbars), usarla; si no, usar título
            top_anchor_y = getattr(self, '_top_anchor_y', None)
            if top_anchor_y is not None:
                grid_top = max(margin, top_anchor_y)
            else:
                grid_top = max(margin, (self.title_rect.bottom + 10) if self.title_rect else margin)
            avail_h = max(0, sh - grid_top - margin - reserve_h)
            self.picker_state.visible = True
            # Calcular ancho profesional/estético basado en celdas del PickerPanel
            cw = getattr(self.picker, 'cell_w', 64)
            ch = getattr(self.picker, 'cell_h', cw)
            pad = getattr(self.picker, 'padding', 8)
            panel_m = getattr(self.picker, 'margin', 8)
            max_cols = getattr(self.picker, 'max_columns', None)
            # Ancho disponible descontando márgenes externos e internos del panel
            # Si existe un ancla izquierda (a la derecha del Add/Remove), usarla como origen
            left_anchor_x = getattr(self, '_left_anchor_x', None)
            origin_x = margin if left_anchor_x is None else max(left_anchor_x, margin)
            avail_w = max(0, sw - origin_x - margin - 2 * panel_m)
            cols_fit = max(1, (avail_w + pad) // (cw + pad))
            if max_cols:
                cols_fit = min(cols_fit, max_cols)
            grid_area_w = cols_fit * cw + max(0, (cols_fit - 1) * pad)
            panel_w = grid_area_w + 2 * panel_m
            rect_w = min(panel_w, max(0, sw - origin_x - margin))

            # Altura ajustada al contenido: filas visibles segun cantidad de items
            try:
                item_ids = [i for i in model.items.keys() if i != "image_item_not_found"]
                count = len(item_ids)
            except Exception:
                count = 0
            rows_fit = 0 if count == 0 else ((count + cols_fit - 1) // cols_fit)
            # Siempre mantener al menos 1 fila visible aunque no haya ítems
            rows_needed = max(1, rows_fit)
            # Limitar el alto visible a 3 filas (el widget manejará el scroll si hay más contenido)
            visible_rows = min(3, rows_needed)
            grid_area_h = visible_rows * ch + max(0, (visible_rows - 1) * pad)
            panel_h = grid_area_h + 2 * panel_m
            rect_h = min(panel_h, avail_h)

            self.picker_state.rect = pygame.Rect(origin_x, grid_top, rect_w, rect_h)
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
                # Registrar como bloqueador de UI para evitar hover/drag debajo del picker
                try:
                    register_blocker(panel_rect)
                except Exception:
                    pass
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
            # Efectos de parpadeo en modo spawn: parpadean el borde del panel (amarillo)
            # y la celda seleccionada (cyan) simultáneamente cuando aplica.
            now = pygame.time.get_ticks()
            spawn_active = getattr(self, '_spawn_mode_active', getattr(model, 'spawn_mode_active', False))
            spawn_item_id = getattr(self, '_spawn_item_id', getattr(model, 'spawn_item_id', None))
            if (now // 500) % 2 == 0 and spawn_active:
                # Borde de panel en amarillo siempre que el modo spawn esté activo
                panel_rect = self.picker_state.rect
                if panel_rect and panel_rect.w > 0 and panel_rect.h > 0:
                    pygame.draw.rect(screen, (255, 255, 0), panel_rect.inflate(6, 6), 3)
                # Si hay ítem elegido para spawn, también parpadea su celda con color de selección
                if spawn_item_id is not None:
                    item_ids = [i for i in model.items.keys() if i != "image_item_not_found"]
                    try:
                        idx = item_ids.index(spawn_item_id)
                        if 0 <= idx < len(getattr(self.picker_state, 'item_rects', [])):
                            cell_rect = self.picker_state.item_rects[idx]
                            if cell_rect and self.picker_state.rect.colliderect(cell_rect.inflate(1, 1)):
                                sel_color = getattr(self.picker, 'select_color', (0, 200, 255))
                                pygame.draw.rect(screen, sel_color, cell_rect.inflate(6, 6), 3)
                    except ValueError:
                        pass
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
