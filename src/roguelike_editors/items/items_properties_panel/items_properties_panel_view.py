import pygame
from typing import Any, Dict, Optional
from roguelike_engine.utils.loader import load_image
from roguelike_editors.entities.entities_properties_panel.services.state_tabs_helpers import (
    build_tab_rects,
    format_tab_label,
)
from roguelike_ui.ui_blocker import register_blocker


class ItemsPropertiesPanelView:
    """Vista que renderiza el panel de propiedades de un ítem activo."""

    def __init__(self, font: pygame.font.Font):
        self.font = font
        self.blink_interval = 500
        # Tamaño fijo del panel
        self.panel_w = 420
        self.panel_h = 360

    # Helpers de texto
    def _wrap_text(self, text: str, max_width: int) -> list[str]:
        words = text.split(' ')
        lines: list[str] = []
        current = ''
        for w in words:
            test = current + (' ' if current else '') + w
            if self.font.size(test)[0] <= max_width:
                current = test
            else:
                lines.append(current)
                current = w
        if current:
            lines.append(current)
        return lines

    def _truncate_text(self, text: str, max_width: int) -> str:
        if self.font.size(text)[0] <= max_width:
            return text
        text = text.rstrip()
        while text and self.font.size(text + '...')[0] > max_width:
            text = text[:-1]
        return text + '...'

    def draw(self,
             screen: pygame.Surface,
             model,
             items: Dict[str, Any],
             active_item_id: Optional[str],
             title_rect: Optional[pygame.Rect] = None) -> None:
        margin = 20
        sw, sh = screen.get_size()
        # Permitir mostrar el panel anclado aunque no haya selección si estamos en modo add-on-system
        allow_empty_panel = getattr(model, 'show_add_system_selector', False)
        no_active_item = (not active_item_id) or (active_item_id not in items)
        if no_active_item and not allow_empty_panel:
            # Sin ítem activo y no en modo especial: ocultar completamente el panel
            model.panel_rect = None
            model.property_entries = []
            model.content_height = 0
            model.content_view_rect = None
            # Limpiar botón de confirmación
            if hasattr(model, 'confirm_button_rect'):
                model.confirm_button_rect = None
            return

        # Posicionar panel: por defecto bajo el título; si hay ancla externa, respetarla
        top_y = max(margin, (title_rect.bottom + 10) if title_rect else margin)
        left_anchor_x = getattr(self, '_left_anchor_x', None)
        top_anchor_y = getattr(self, '_top_anchor_y', None)

        item = items.get(active_item_id) if active_item_id else None
        # Obtener datos del ítem
        if item is None:
            data = {}
        elif hasattr(item, 'model_dump'):
            data = item.model_dump()
        else:
            try:
                data = item.dict()
            except Exception:
                try:
                    data = vars(item)
                except Exception:
                    data = {}

        # Construir entradas editables como pares (key, value)
        entries: list[tuple[str, str]] = []
        if item is None and getattr(model, 'show_add_system_selector', False):
            # Usar esquema completo si está disponible
            schema_keys = getattr(model, 'schema_keys', []) or []
            # Reordenar para priorizar campos principales
            preferred = ["id", "name", "description"]
            order_keys = [k for k in preferred if k in schema_keys]
            for k in schema_keys:
                if k not in order_keys:
                    order_keys.append(k)
            for k in order_keys:
                v = getattr(model, 'new_item_draft', {}).get(k, "")
                if getattr(model, 'editing_property', None) == k:
                    display_val = model.editing_text
                else:
                    display_val = str(v)
                entries.append((k, display_val))
        else:
            # Entradas basadas en el ítem actual
            order_keys = []
            for k in ("id", "name", "description"):
                if k in data:
                    order_keys.append(k)
            for k, v in data.items():
                if k in ("id", "name", "description") or v is None:
                    continue
                order_keys.append(k)
            for k in order_keys:
                v = data.get(k, "")
                if getattr(model, 'editing_property', None) == k:
                    display_val = model.editing_text
                else:
                    display_val = str(v)
                entries.append((k, display_val))

        font_h = self.font.get_height()
        panel_padding = 10
        panel_w = self.panel_w
        # Mantener en pantalla si no cabe completamente
        panel_h = self.panel_h if (top_y + self.panel_h + margin) <= sh else max(80, sh - top_y - margin)
        # Si tenemos ancla desde el editor (Add/Remove a la izquierda), usarla
        if left_anchor_x is not None:
            panel_x = min(sw - panel_w - margin, left_anchor_x + 8)
        else:
            panel_x = sw - panel_w - margin
        if top_anchor_y is not None:
            panel_y = max(margin, top_anchor_y)
        else:
            panel_y = top_y

        # Fondo del panel (fijo)
        info_surf = pygame.Surface((panel_w, panel_h), pygame.SRCALPHA)
        info_surf.fill((0, 0, 0, 200))
        screen.blit(info_surf, (panel_x, panel_y))

        # Actualizar estado de colisiones y viewport de contenido
        model.panel_rect = pygame.Rect(panel_x, panel_y, panel_w, panel_h)
        # Registrar como bloqueador de UI para evitar hover/drag debajo del panel
        try:
            if model.panel_rect and model.panel_rect.w > 0 and model.panel_rect.h > 0:
                register_blocker(model.panel_rect)
        except Exception:
            pass

        # Pestañas de tipo (arriba)
        tab_pad = (10, 5)
        model.type_tab_rects = build_tab_rects(model.type_tabs, self.font, (panel_x + panel_padding, panel_y + panel_padding), tab_pad)
        # Altura ocupada por las pestañas
        any_tab_rect = next(iter(model.type_tab_rects.values())) if model.type_tab_rects else pygame.Rect(0, 0, 0, 0)
        tabs_h = any_tab_rect.h if model.type_tab_rects else 0

        # Dibujar pestañas
        mouse_pos = pygame.mouse.get_pos()
        for label, rect in model.type_tab_rects.items():
            is_active = (model.active_type_tab == label)
            is_hover = rect.collidepoint(mouse_pos)
            if is_active or is_hover:
                surf = pygame.Surface((rect.w, rect.h), pygame.SRCALPHA)
                surf.fill((255, 255, 0, 100))
                screen.blit(surf, (rect.x, rect.y))
                pygame.draw.rect(screen, (255, 255, 0), rect, 2)
            else:
                pygame.draw.rect(screen, (100, 100, 100), rect)
                pygame.draw.rect(screen, (255, 255, 255), rect, 2)
            text_label = format_tab_label(label)
            text_surf = self.font.render(text_label, True, (0, 0, 0))
            text_x = rect.x + (rect.w - text_surf.get_width()) // 2
            text_y = rect.y + tab_pad[1]
            screen.blit(text_surf, (text_x, text_y))

        # Viewport de contenido debajo de pestañas
        view_rect = pygame.Rect(
            panel_x + panel_padding,
            panel_y + panel_padding + tabs_h + 8,
            panel_w - 2*panel_padding,
            panel_h - 2*panel_padding - tabs_h - 8,
        )
        model.content_view_rect = view_rect

        # Dibujar contenido según tab activa
        model.property_entries = []
        truncated_entries = []
        old_clip = screen.get_clip()
        screen.set_clip(view_rect)

        if model.active_type_tab == "properties":
            max_line_width = view_rect.w
            model.content_height = len(entries) * (font_h + 2)
            tx = view_rect.x
            ty0 = view_rect.y
            for idx, (key, val) in enumerate(entries):
                text_content = f"{key}: {val}"
                display_text = self._truncate_text(text_content, max_line_width)
                # Color: resaltar 'name' como antes, el resto gris claro
                color = (255, 255, 0) if key == 'name' else (200, 200, 200)
                txt_surf = self.font.render(display_text, True, color)

                # Y con scroll
                y = ty0 + idx * (font_h + 2) - model.scroll_y
                line_rect = pygame.Rect(tx, y, txt_surf.get_width(), font_h)
                # Sólo dibujar si intersecta el viewport
                if line_rect.bottom >= view_rect.top and line_rect.top <= view_rect.bottom:
                    screen.blit(txt_surf, (tx, y))
                    model.property_entries.append((line_rect, key))
                    if display_text != text_content:
                        truncated_entries.append((line_rect, text_content))
        else:
            # Tab 'assets': una única celda para el icono del ítem
            model.content_height = 0  # sin scroll
            cell_size = 96
            pad = 8
            cx = view_rect.x + pad
            cy = view_rect.y + pad
            cell_rect = pygame.Rect(cx, cy, cell_size, cell_size)
            model.asset_cell_rect = cell_rect
            # Fondo de la celda
            pygame.draw.rect(screen, (60, 60, 60), cell_rect)
            pygame.draw.rect(screen, (255, 255, 255), cell_rect, 2)
            # Cargar imagen actual (icon/icon_small/icon_large; lista->primero)
            icon_path = None
            if item is None:
                # Mostrar valor desde el borrador si existe
                try:
                    data_map = dict(getattr(model, 'new_item_draft', {}))
                except Exception:
                    data_map = {}
            elif hasattr(item, 'model_dump'):
                data_map = item.model_dump()
            else:
                try:
                    data_map = item.dict()
                except Exception:
                    try:
                        data_map = vars(item)
                    except Exception:
                        data_map = {}
            for k in ("icon", "icon_small", "icon_large"):
                if k in data_map:
                    val = data_map[k]
                    if isinstance(val, list):
                        icon_path = val[0] if val else None
                    else:
                        icon_path = val
                    break
            # Dibujar thumbnail si existe
            if icon_path:
                try:
                    thumb = load_image(str(icon_path), (cell_size - 4, cell_size - 4))
                    screen.blit(thumb, (cell_rect.x + 2, cell_rect.y + 2))
                except Exception:
                    # placeholder gris
                    ph = pygame.Surface((cell_size - 4, cell_size - 4))
                    ph.fill((100, 100, 100))
                    screen.blit(ph, (cell_rect.x + 2, cell_rect.y + 2))
            else:
                # placeholder si no hay icono
                ph = pygame.Surface((cell_size - 4, cell_size - 4))
                ph.fill((40, 40, 40))
                screen.blit(ph, (cell_rect.x + 2, cell_rect.y + 2))
            # Etiqueta
            label = self.font.render("Item Image", True, (220, 220, 220))
            screen.blit(label, (cell_rect.right + 10, cell_rect.y + 4))

        screen.set_clip(old_clip)
        self._truncated_entries = truncated_entries

        # Scrollbar si overflow (solo en propiedades)
        if model.active_type_tab == "properties" and model.content_height > view_rect.h:
            bar_w = 6
            track = pygame.Rect(view_rect.right - bar_w, view_rect.top, bar_w, view_rect.h)
            pygame.draw.rect(screen, (40, 40, 40), track)
            ratio = max(0.08, min(1.0, view_rect.h / max(1, model.content_height)))
            thumb_h = max(12, int(view_rect.h * ratio))
            max_scroll = max(1, model.content_height - view_rect.h)
            t = min(1.0, max(0.0, model.scroll_y / max_scroll))
            thumb_y = view_rect.y + int((view_rect.h - thumb_h) * t)
            thumb = pygame.Rect(track.x, thumb_y, bar_w, thumb_h)
            pygame.draw.rect(screen, (120, 120, 120), thumb)

        # Decoraciones de estado (usar rects ya scrolleados) solo en propiedades
        if model.active_type_tab == "properties":
            # Edición tiene prioridad: púrpura
            if getattr(model, 'editing_property', None):
                for rect_prop, key_prop in getattr(model, 'property_entries', []):
                    if key_prop == model.editing_property:
                        ed_rect = rect_prop.inflate(4, 0)
                        pygame.draw.rect(screen, (128, 0, 128), ed_rect, 2)
                        break
            else:
                # Hover o foco: amarillo
                target_key = getattr(model, 'hovered_property', None) or getattr(model, 'focused_property', None)
                if target_key:
                    for rect_prop, key_prop in getattr(model, 'property_entries', []):
                        if key_prop == target_key:
                            hl_rect = rect_prop.inflate(4, 0)
                            pygame.draw.rect(screen, (255, 255, 0), hl_rect, 2)
                            break

        # Tooltips (post) sólo si el ratón está sobre el viewport
        mx, my = pygame.mouse.get_pos()
        if model.active_type_tab == "properties" and model.content_view_rect and model.content_view_rect.collidepoint(mx, my):
            for rect, full_text in getattr(self, '_truncated_entries', []):
                if rect.collidepoint(mx, my):
                    tt_w = self.font.size(full_text)[0] + 8
                    tt_h = font_h + 4
                    tt_x = min(mx + 10, sw - tt_w - margin)
                    tt_y = min(my + 10, sh - tt_h - margin)
                    tooltip_surf = pygame.Surface((tt_w, tt_h), pygame.SRCALPHA)
                    tooltip_surf.fill((0, 0, 0, 220))
                    tooltip_txt = self.font.render(full_text, True, (255, 255, 255))
                    tooltip_surf.blit(tooltip_txt, (4, 2))
                    screen.blit(tooltip_surf, (tt_x, tt_y))
                    break

        # Botón de confirmación al final del panel cuando está activo el modo add-on-system
        # Se pinta siempre que el panel esté visible; si no hay ítem seleccionado, se mostrará pero la acción no hará nada
        btn_h_pad = 10
        if getattr(model, 'show_add_system_selector', False) and model.panel_rect:
            btn_w = 120
            btn_h = 32
            btn_x = model.panel_rect.right - btn_w - 10
            btn_y = model.panel_rect.bottom - btn_h - 10
            btn_rect = pygame.Rect(btn_x, btn_y, btn_w, btn_h)
            # Guardar en el modelo para hit-test desde eventos
            setattr(model, 'confirm_button_rect', btn_rect)
            # Fondo verde
            surf_btn = pygame.Surface((btn_w, btn_h), pygame.SRCALPHA)
            surf_btn.fill((0, 160, 0, 230))
            screen.blit(surf_btn, (btn_x, btn_y))
            # Borde
            pygame.draw.rect(screen, (0, 255, 0), btn_rect, 2)
            # Texto
            label = self.font.render("Confirmar", True, (255, 255, 255))
            lx = btn_x + (btn_w - label.get_width()) // 2
            ly = btn_y + (btn_h - label.get_height()) // 2
            screen.blit(label, (lx, ly))
        else:
            # No mostrar botón; limpiar rect si existiera
            if hasattr(model, 'confirm_button_rect'):
                model.confirm_button_rect = None
