import pygame
import json
from typing import Dict, Any, Optional
from .spells_properties_panel_models import SpellsPropertiesPanelModel
from roguelike_editors.entities.services.constants import UI_MARGIN
from roguelike_engine.utils.loader import load_image
from roguelike_editors.entities.entities_properties_panel.services.state_tabs_helpers import (
    build_tab_rects,
    format_tab_label,
)
from roguelike_ui.ui_blocker import register_blocker


class SpellsPropertiesPanelView:
    def __init__(self, font: pygame.font.Font):
        self.font = font
        self.blink_interval = 500
        # Optional anchors set by editor
        self._left_anchor_x: Optional[int] = None
        self._top_anchor_y: Optional[int] = None
        # Match Items panel fixed size
        self.panel_w = 420
        self.panel_h = 360

    def set_anchor(self, left_x: Optional[int], top_y: Optional[int]) -> None:
        """Set external anchor for the panel top-left position, mirroring Entities layout."""
        self._left_anchor_x = left_x
        self._top_anchor_y = top_y

    # Helpers de texto (como Items)
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

    def draw(self, screen: pygame.Surface, model: SpellsPropertiesPanelModel, spells: Dict[str, Any], active_id: Optional[str], title_rect: Optional[pygame.Rect] = None) -> None:
        # Permitir panel visible sin selección si está activo el modo add-on-system
        allow_empty_panel = getattr(model, 'show_add_system_selector', False)
        no_active = (not active_id) or (active_id not in spells)
        if no_active and not allow_empty_panel:
            model.panel_rect = None
            model.property_entries = []
            model.content_height = 0
            model.content_view_rect = None
            if hasattr(model, 'confirm_button_rect'):
                model.confirm_button_rect = None
            return

        sw, sh = screen.get_size()
        margin = 20
        pad = 10
        font_h = self.font.get_height()

        # Posicionar panel: bajo el título y anclado a la derecha (o a anclas externas)
        top_y = max(margin, (title_rect.bottom + 10) if title_rect else margin)
        panel_w = self.panel_w
        # Si no cabe completo, ajusta altura
        panel_h = self.panel_h if (top_y + self.panel_h + margin) <= sh else max(80, sh - top_y - margin)

        if self._left_anchor_x is not None:
            panel_x = min(sw - panel_w - margin, self._left_anchor_x + 8)
        else:
            panel_x = sw - panel_w - margin
        if self._top_anchor_y is not None:
            panel_y = max(margin, self._top_anchor_y)
        else:
            panel_y = top_y

        # Fondo del panel
        info_surf = pygame.Surface((panel_w, panel_h), pygame.SRCALPHA)
        info_surf.fill((0, 0, 0, 200))
        screen.blit(info_surf, (panel_x, panel_y))

        # Actualizar rect y bloquear UI subyacente
        model.panel_rect = pygame.Rect(panel_x, panel_y, panel_w, panel_h)
        try:
            if model.panel_rect and model.panel_rect.w > 0 and model.panel_rect.h > 0:
                register_blocker(model.panel_rect)
        except Exception:
            pass

        # Preparar datos del hechizo activo o del borrador
        spell_obj = spells.get(active_id) if active_id else None
        if spell_obj is None:
            data_map: Dict[str, Any] = {}
        elif isinstance(spell_obj, dict):
            # Spells cargados desde JSON vienen como dict
            data_map = spell_obj
        elif hasattr(spell_obj, 'model_dump'):
            data_map = spell_obj.model_dump()
        else:
            try:
                data_map = spell_obj.dict()
            except Exception:
                try:
                    data_map = vars(spell_obj)
                except Exception:
                    data_map = {}

        # Entradas a mostrar
        entries: list[tuple[str, str]] = []
        if spell_obj is None and getattr(model, 'show_add_system_selector', False):
            schema_keys = getattr(model, 'schema_keys', []) or []
            preferred = ["id", "name", "description"]
            order_keys = [k for k in preferred if k in schema_keys]
            for k in schema_keys:
                if k not in order_keys:
                    order_keys.append(k)
            for k in order_keys:
                v = getattr(model, 'new_spell_draft', {}).get(k, "")
                display_val = model.editing_text if getattr(model, 'editing_property', None) == k else str(v)
                entries.append((k, display_val))
        else:
            # Helpers for nested display
            def get_by_path(d: Dict[str, Any], path: str, default: Any = "") -> Any:
                cur: Any = d
                for part in path.split('.'):  # dotted path
                    if not isinstance(cur, dict) or part not in cur:
                        cur = None
                        break
                    cur = cur[part]
                if cur is None:
                    # Fallbacks for legacy flat fields
                    if path == 'vfx.sprite.path':
                        return d.get('sprite', default)
                    if path == 'vfx.sprite.scale':
                        return d.get('scale', default)
                    # particles fallbacks
                    fb_map = {
                        'vfx.particles.count': 'particle_count',
                        'vfx.particles.dispersion': 'particle_dispersion',
                        'vfx.particles.colors': 'particle_colors',
                        'vfx.particles.lifespan': 'particle_lifespan',
                        'vfx.particles.speed': 'particle_speed',
                    }
                    if path in fb_map:
                        return d.get(fb_map[path], default)
                    return default
                return cur

            def fmt_val(v: Any) -> str:
                if isinstance(v, (dict, list)):
                    try:
                        return json.dumps(v, ensure_ascii=False)
                    except Exception:
                        return str(v)
                if v is None:
                    return ""
                return str(v)

            # Ordered keys grouped by sections
            keys: list[str] = []
            keys += ["id", "name", "type"]
            keys += [
                "timings.prepare", "timings.channel", "timings.cooldown",
            ]
            keys += [
                "rules.allow_movement", "rules.lock_cast_direction", "rules.interruptible",
                "rules.automatic", "rules.automatic_cast_punish",
            ]
            keys += [
                "constraints.max_instances", "constraints.allow_overlap",
            ]
            keys += [
                "effect.damage", "effect.range", "effect.speed", "effect.duration", "effect.lifetime",
                "effect.radius", "effect.distance", "effect.arc_range_degrees", "effect.buff",
            ]
            keys += [
                "vfx.preset", "vfx.sprite.path", "vfx.sprite.scale",
                "vfx.particles.count", "vfx.particles.dispersion", "vfx.particles.colors",
                "vfx.particles.lifespan", "vfx.particles.speed", "vfx.particles.size_range",
                "vfx.particles.color", "vfx.particles.emit_rate",
            ]
            keys += [
                "meta.offset", "meta.speed_multiplier", "meta.segments",
            ]

            for k in keys:
                raw_val = get_by_path(data_map, k, "") if "." in k else data_map.get(k, "")
                display_val = model.editing_text if getattr(model, 'editing_property', None) == k else fmt_val(raw_val)
                entries.append((k, display_val))

        # Pestañas superiores
        tab_pad = (10, 5)
        model.type_tab_rects = build_tab_rects(model.type_tabs, self.font, (panel_x + pad, panel_y + pad), tab_pad)
        any_tab_rect = next(iter(model.type_tab_rects.values())) if model.type_tab_rects else pygame.Rect(0, 0, 0, 0)
        tabs_h = any_tab_rect.h if model.type_tab_rects else 0

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

        # Viewport de contenido
        view_rect = pygame.Rect(
            panel_x + pad,
            panel_y + pad + tabs_h + 8,
            panel_w - 2 * pad,
            panel_h - 2 * pad - tabs_h - 8,
        )
        model.content_view_rect = view_rect

        # Contenido según pestaña
        model.property_entries = []
        truncated_entries: list[tuple[pygame.Rect, str]] = []
        old_clip = screen.get_clip()
        screen.set_clip(view_rect)

        if model.active_type_tab == "properties":
            max_line_w = view_rect.w
            font_h_local = self.font.get_height()
            line_h = font_h_local + 2
            # Construir líneas
            lines: list[tuple[str, bool]] = []
            title_text = active_id if active_id else ""
            if title_text:
                lines.append((title_text, True))
            for k, v in entries:
                text_content = f"{k}: {v}"
                lines.append((text_content, False))
            model.content_height = len(lines) * line_h

            y = view_rect.y - model.scroll_y
            for text, is_title in lines:
                if y + line_h < view_rect.y:
                    y += line_h
                    continue
                if y > view_rect.bottom:
                    break
                color = (255, 255, 0) if (is_title or text.startswith("name:")) else (200, 200, 200)
                display_text = self._truncate_text(text, max_line_w)
                txt_surf = self.font.render(display_text, True, color)
                screen.blit(txt_surf, (view_rect.x, y))
                if not is_title and ": " in text:
                    key = text.split(": ", 1)[0]
                    line_rect = pygame.Rect(view_rect.x, y, min(txt_surf.get_width(), max_line_w), font_h_local)
                    model.property_entries.append((line_rect, key))
                    if display_text != text:
                        truncated_entries.append((line_rect, text))
                y += line_h

            # Decoraciones de estado
            if getattr(model, 'editing_property', None):
                for rect_prop, key_prop in getattr(model, 'property_entries', []):
                    if key_prop == model.editing_property:
                        ed_rect = rect_prop.inflate(4, 0)
                        pygame.draw.rect(screen, (128, 0, 128), ed_rect, 2)
                        t = pygame.time.get_ticks()
                        if (t % self.blink_interval) < (self.blink_interval // 2):
                            pre = f"{key_prop}: "
                            caret_x = ed_rect.x + self.font.size(pre + model.editing_text[:model.editing_cursor])[0]
                            pygame.draw.line(screen, (255, 255, 255), (caret_x, ed_rect.y), (caret_x, ed_rect.y + self.font.get_height()), 2)
                        break
            else:
                target_key = getattr(model, 'hovered_property', None) or getattr(model, 'focused_property', None)
                if target_key:
                    for rect_prop, key_prop in getattr(model, 'property_entries', []):
                        if key_prop == target_key:
                            hl_rect = rect_prop.inflate(4, 0)
                            pygame.draw.rect(screen, (255, 255, 0), hl_rect, 2)
                            break
        else:
            # Tab 'assets': celda para el icono del hechizo
            model.content_height = 0
            cell_size = 96
            pad_cell = 8
            cx = view_rect.x + pad_cell
            cy = view_rect.y + pad_cell
            cell_rect = pygame.Rect(cx, cy, cell_size, cell_size)
            model.asset_cell_rect = cell_rect
            pygame.draw.rect(screen, (60, 60, 60), cell_rect)
            pygame.draw.rect(screen, (255, 255, 255), cell_rect, 2)

            # Obtener ruta del icono principal (vfx.sprite.path o 'sprite' legado)
            data_map_icon = data_map if spell_obj is not None else getattr(model, 'new_spell_draft', {})
            icon_path = None
            # Nested
            try:
                vfx = data_map_icon.get('vfx', {}) if isinstance(data_map_icon, dict) else {}
                if isinstance(vfx, dict):
                    spr = vfx.get('sprite', {})
                    if isinstance(spr, dict):
                        icon_path = spr.get('path')
            except Exception:
                icon_path = None
            # Fallback to flat sprite
            if not icon_path and isinstance(data_map_icon, dict):
                icon_path = data_map_icon.get('sprite')
            if icon_path:
                try:
                    thumb = load_image(str(icon_path), (cell_size - 4, cell_size - 4))
                    screen.blit(thumb, (cell_rect.x + 2, cell_rect.y + 2))
                except Exception:
                    ph = pygame.Surface((cell_size - 4, cell_size - 4))
                    ph.fill((100, 100, 100))
                    screen.blit(ph, (cell_rect.x + 2, cell_rect.y + 2))
            else:
                ph = pygame.Surface((cell_size - 4, cell_size - 4))
                ph.fill((40, 40, 40))
                screen.blit(ph, (cell_rect.x + 2, cell_rect.y + 2))
            label = self.font.render("Spell Image (vfx.sprite.path)", True, (220, 220, 220))
            screen.blit(label, (cell_rect.right + 10, cell_rect.y + 4))

        # Restaurar clip
        screen.set_clip(old_clip)

        # Scrollbar si overflow (solo propiedades)
        if model.active_type_tab == "properties" and model.content_height > (model.content_view_rect.h if model.content_view_rect else 0):
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

        # Tooltips por truncado
        mx, my = pygame.mouse.get_pos()
        if model.active_type_tab == "properties" and model.content_view_rect and model.content_view_rect.collidepoint(mx, my):
            for rect, full_text in truncated_entries:
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

        # Botón Confirmar en modo add-on-system
        if getattr(model, 'show_add_system_selector', False) and model.panel_rect:
            btn_w = 120
            btn_h = 32
            btn_x = model.panel_rect.right - btn_w - 10
            btn_y = model.panel_rect.bottom - btn_h - 10
            btn_rect = pygame.Rect(btn_x, btn_y, btn_w, btn_h)
            setattr(model, 'confirm_button_rect', btn_rect)
            surf_btn = pygame.Surface((btn_w, btn_h), pygame.SRCALPHA)
            surf_btn.fill((0, 160, 0, 230))
            screen.blit(surf_btn, (btn_x, btn_y))
            pygame.draw.rect(screen, (0, 255, 0), btn_rect, 2)
            label = self.font.render("Confirmar", True, (255, 255, 255))
            lx = btn_x + (btn_w - label.get_width()) // 2
            ly = btn_y + (btn_h - label.get_height()) // 2
            screen.blit(label, (lx, ly))
        else:
            if hasattr(model, 'confirm_button_rect'):
                model.confirm_button_rect = None
