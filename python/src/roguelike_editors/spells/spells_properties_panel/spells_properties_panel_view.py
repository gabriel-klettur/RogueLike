import pygame
import logging
import os
from typing import Dict, Any, Optional
from .spells_properties_panel_models import SpellsPropertiesPanelModel
from roguelike_ui.ui_blocker import register_blocker
from .utils.text import truncate_text
from .utils.data_map import (
    extract_data_map_to_dict,
    build_entries,
)
from .render.tabs import render_tabs
from .render.properties import render_properties_section
from .render.assets import render_assets_section


logger = logging.getLogger(__name__)
# Toggle to enable verbose per-frame/provide-call debug logs for this view
LOG_SPELLS_PROPS_DEBUG = (
    os.getenv("RL_SPELLS_PROPS_DEBUG") == "1"
    or os.getenv("RL_SPELLS_VIEW_DEBUG") == "1"
    or os.getenv("RL_SPELLS_EDITOR_DEBUG") == "1"
)
# Throttle timestamps (ms) for debug prints
_last_dt_log_ts = 0


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
        # Frame timing for particle previews
        self._last_ticks: int = pygame.time.get_ticks()
        self._dt_ms: int = 16
        self._max_dt_ms: int = 50

    def set_anchor(self, left_x: Optional[int], top_y: Optional[int]) -> None:
        """Set external anchor for the panel top-left position, mirroring Entities layout."""
        self._left_anchor_x = left_x
        self._top_anchor_y = top_y

    def draw(self, screen: pygame.Surface, model: SpellsPropertiesPanelModel, spells: Dict[str, Any], active_id: Optional[str], title_rect: Optional[pygame.Rect] = None, preview_provider=None) -> None:
        # Frame delta for previews
        now = pygame.time.get_ticks()
        self._dt_ms = max(1, now - self._last_ticks)
        # Clamp dt to avoid large spikes when panel opens or window regains focus
        self._dt_ms = min(self._dt_ms, self._max_dt_ms)
        self._last_ticks = now
        # Debug: dt for properties panel (throttled and gated)
        if LOG_SPELLS_PROPS_DEBUG and logger.isEnabledFor(logging.DEBUG):
            global _last_dt_log_ts
            now_ms = pygame.time.get_ticks()
            if now_ms - _last_dt_log_ts >= 1000:
                try:
                    logger.debug("[SpellsProps] dt_ms=%d", self._dt_ms)
                except Exception:
                    pass
                _last_dt_log_ts = now_ms

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
        data_map: Dict[str, Any] = extract_data_map_to_dict(spell_obj)

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
            entries = build_entries(
                data_map=data_map,
                editing_property=getattr(model, 'editing_property', None),
                editing_text=getattr(model, 'editing_text', None),
            )

        # Pestañas superiores
        tab_pad = (10, 5)
        tabs_h = render_tabs(
            screen=screen,
            font=self.font,
            model=model,
            panel_pos=(panel_x, panel_y),
            pad=pad,
            tab_pad=tab_pad,
        )

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
            truncated_entries = render_properties_section(
                screen=screen,
                font=self.font,
                model=model,
                view_rect=view_rect,
                entries=entries,
                active_id=active_id,
                blink_interval_ms=self.blink_interval,
                truncate_text=truncate_text,
            )
        else:
            data_map_icon = data_map if spell_obj is not None else getattr(model, 'new_spell_draft', {})
            render_assets_section(
                screen=screen,
                font=self.font,
                model=model,
                view_rect=view_rect,
                data_map_icon=data_map_icon,
                preview_provider=preview_provider,
                dt_ms=self._dt_ms,
            )

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
            t = min(1.0, max(0.0, getattr(model, 'scroll_y', 0) / max_scroll))
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
