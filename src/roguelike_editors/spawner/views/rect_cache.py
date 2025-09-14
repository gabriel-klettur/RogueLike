from __future__ import annotations

"""Helpers para manejar el caché de rects en la vista del Spawner Editor."""

import logging

logger = logging.getLogger(__name__)


def reset_last_rects(view) -> None:
    """Reinicia los rects caché de la UI para el frame actual en `view`."""
    try:
        view._last_title_rect = None
        view._last_toolbar_rect = None
        view._last_instance_toolbar_rect = None
        view._last_manager_rect = None
        view._last_instances_rect = None
        view._last_properties_rect = None
        view._last_selected_delete_rect = None
        view._last_selected_resize_rect = None
        view._last_selected_reset_rect = None
        view._last_z_bottom_minus_rect = None
        view._last_z_bottom_plus_rect = None
        view._last_z_top_minus_rect = None
        view._last_z_top_plus_rect = None
        view._last_split_handle_rect = None
    except AttributeError:
        logger.debug("reset_last_rects: failed to reset one or more cached rects", exc_info=True)
