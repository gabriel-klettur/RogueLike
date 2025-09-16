from __future__ import annotations

"""Dibujo de overlays auxiliares para la vista del Spawner Editor."""

import logging
import pygame
from typing import Optional
from . import theme

logger = logging.getLogger(__name__)


def render_hint_overlay(view, screen: pygame.Surface, title_rect: Optional[pygame.Rect], tb_rect: Optional[pygame.Rect], mgr_rect: Optional[pygame.Rect], inst_rect: Optional[pygame.Rect]) -> None:
    """Dibuja el hint inferior con una breve ayuda de uso."""
    try:
        c = view.controller
        if c.font:
            base_y = (title_rect.bottom + 6) if title_rect else 10
            if tb_rect is not None:
                base_y = max(base_y, tb_rect.bottom + 6)
            if mgr_rect is not None:
                base_y = max(base_y, mgr_rect.bottom + 6)
            if inst_rect is not None:
                base_y = max(base_y, inst_rect.bottom + 6)
            text = c.font.render(theme.HINT_TEXT, True, theme.COLOR_HINT)
            screen.blit(text, (10, base_y))
    except (AttributeError, TypeError, ValueError, pygame.error):
        logger.debug("render_hint_overlay: error while drawing hint", exc_info=True)


def render_zone_change_confirmation(view, screen: pygame.Surface) -> None:
    """Dibuja el overlay de confirmación de cambio de zona."""
    try:
        c = view.controller
        pending = getattr(c.model, 'pending_zone_confirm', None)
        if not pending:
            return
        overlay = pygame.Surface(screen.get_size(), pygame.SRCALPHA)
        overlay.fill((*theme.COLOR_BLACK, theme.MODAL_BACKDROP_ALPHA))
        screen.blit(overlay, (0, 0))
        orig_zone = str(pending.get('orig_zone'))
        prop_zone = str(pending.get('proposed_zone'))
        lines = [
            theme.ZONE_CONFIRM_LINE_1.format(prop_zone=prop_zone),
            theme.ZONE_CONFIRM_LINE_2.format(orig_zone=orig_zone),
            theme.ZONE_CONFIRM_LINE_3,
        ]
        font = getattr(c, 'font', None)
        if not font:
            return
        max_w = 0
        rendered = []
        for ln in lines:
            surf = font.render(ln, True, theme.COLOR_WHITE)
            rendered.append(surf)
            max_w = max(max_w, surf.get_width())
        pad = 14
        line_h = rendered[0].get_height()
        total_h = line_h * len(rendered) + pad * 2
        total_w = max_w + pad * 2
        vw, vh = screen.get_size()
        rect = pygame.Rect((vw - total_w) // 2, (vh - total_h) // 2, total_w, total_h)
        pygame.draw.rect(screen, theme.ZONE_PANEL_BG, rect)
        pygame.draw.rect(screen, theme.ZONE_PANEL_BORDER, rect, 2)
        y = rect.top + pad
        for surf in rendered:
            x = rect.left + (rect.width - surf.get_width()) // 2
            screen.blit(surf, (x, y))
            y += line_h
    except (AttributeError, TypeError, ValueError, pygame.error):
        logger.debug("render_zone_change_confirmation: error while drawing zone confirm", exc_info=True)


def render_delete_instance_confirmation(view, screen: pygame.Surface) -> None:
    """Dibuja el overlay de confirmación de eliminación de instancia."""
    try:
        c = view.controller
        pending_del = getattr(c.model, 'pending_delete_confirm', None)
        if not pending_del:
            return
        overlay = pygame.Surface(screen.get_size(), pygame.SRCALPHA)
        overlay.fill((*theme.COLOR_BLACK, theme.MODAL_BACKDROP_ALPHA))
        screen.blit(overlay, (0, 0))
        tpl = str(pending_del.get('template_id'))
        zone = str(pending_del.get('zone'))
        lt = pending_del.get('local_tile') or (0, 0)
        lines = [
            theme.DELETE_CONFIRM_LINE_1,
            f"Template: '{tpl}' | Zone: '{zone}' | Tile: ({lt[0]}, {lt[1]})",
            theme.DELETE_CONFIRM_LINE_3,
        ]
        font = getattr(c, 'font', None)
        if not font:
            return
        max_w = 0
        rendered = []
        for ln in lines:
            surf = font.render(ln, True, theme.DELETE_TEXT)
            rendered.append(surf)
            max_w = max(max_w, surf.get_width())
        pad = 14
        line_h = rendered[0].get_height()
        total_h = line_h * len(rendered) + pad * 2
        total_w = max_w + pad * 2
        vw, vh = screen.get_size()
        rect = pygame.Rect((vw - total_w) // 2, (vh - total_h) // 2, total_w, total_h)
        pygame.draw.rect(screen, theme.DELETE_PANEL_BG, rect)
        pygame.draw.rect(screen, theme.DELETE_PANEL_BORDER, rect, 2)
        y = rect.top + pad
        for surf in rendered:
            x = rect.left + (rect.width - surf.get_width()) // 2
            screen.blit(surf, (x, y))
            y += line_h
    except (AttributeError, TypeError, ValueError, pygame.error):
        logger.debug("render_delete_instance_confirmation: error while drawing delete confirm", exc_info=True)


def render_visuals_picker(view, screen: pygame.Surface) -> None:
    """Dibuja/actualiza el picker de visuales cuando está abierto."""
    try:
        c = view.controller
        ip = getattr(c, 'instance_properties', None)
        if ip is not None and getattr(getattr(ip, 'model', None), 'visuals_picker_open', False):
            cam = getattr(c, 'game', None)
            cam = getattr(cam, 'camera', None)
            if cam is not None:
                try:
                    inst_rect_anchor = getattr(view, '_last_instances_rect', None)
                    picker = ip.get_visuals_picker()
                    if picker is not None and inst_rect_anchor is not None:
                        picker.set_anchors(left_x=int(inst_rect_anchor.left), top_y=int(inst_rect_anchor.bottom + 6), reserved_bottom_h=0)
                except (AttributeError, TypeError, ValueError):
                    logger.debug("render_visuals_picker: failed to set anchors", exc_info=True)
                ip.render_visuals_picker(screen, cam)
    except (AttributeError, TypeError, ValueError, pygame.error):
        logger.debug("render_visuals_picker: error while drawing picker", exc_info=True)
