from __future__ import annotations

import logging
import pygame


def handle_visuals_picker(h, ctx, event: pygame.event.Event) -> bool:
    """Route events to the Visuals Picker overlay when it's open, consuming gameplay input."""
    logger = logging.getLogger(__name__)
    try:
        ip = getattr(h.controller, 'instance_properties', None)
        if ip is not None and getattr(getattr(ip, 'model', None), 'visuals_picker_open', False):
            handled = False
            try:
                handled = bool(ip.handle_visuals_picker_event(event, ctx.camera))
            except (AttributeError, TypeError, ValueError):
                logger.debug("handle_event: visuals_picker_event handler failed", exc_info=True)
                handled = False
            # While overlay open, always consume input types that could affect gameplay
            return True if handled or event.type in (
                pygame.MOUSEBUTTONDOWN, pygame.MOUSEBUTTONUP, pygame.MOUSEMOTION,
                pygame.MOUSEWHEEL, pygame.KEYDOWN, pygame.KEYUP
            ) else False
    except (AttributeError, TypeError):
        logger = logging.getLogger(__name__)
        logger.debug("handle_event: exception while routing to visuals picker", exc_info=True)
    return False
