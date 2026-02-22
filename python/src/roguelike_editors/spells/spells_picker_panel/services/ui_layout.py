from __future__ import annotations

from typing import Any, Optional

import pygame

from roguelike_editors.entities.services.constants import UI_MARGIN


def get_assets_anchor_rect(controller: Any) -> Optional[pygame.Rect]:
    """Compute anchor rect so the Assets picker appears below and aligned to the Spells Picker grid.

    Primary anchor: picker's grid_rect (left x, bottom y + margin, same width).
    Fallbacks: asset cell rect, then properties panel rect.
    """
    try:
        grid_rect = getattr(controller.view, "grid_rect", None)
        if grid_rect is not None:
            return pygame.Rect(grid_rect.x, grid_rect.bottom + UI_MARGIN, grid_rect.w, 0)
    except Exception:
        pass

    try:
        cell = getattr(controller.spells_properties_controller.model, "asset_cell_rect", None)
        if cell:
            return cell
    except Exception:
        pass

    try:
        return getattr(controller.spells_properties_controller.model, "panel_rect", None)
    except Exception:
        return None


def picker_left_anchor_x(controller: Any) -> Optional[int]:
    """Compute the left x position for the picker so it sits to the right of Add/Remove panel."""
    try:
        if not getattr(controller.model, "picker_visible", False):
            return None
        tb_widget = getattr(controller.spells_toolbar_view, "widget", None)
        if tb_widget is None:
            return None
        tb_pos = tb_widget.panel.pos or (tb_widget.x, tb_widget.y)
        tb_w, _ = tb_widget.panel.surface.get_size()
        arm_widget = getattr(controller.spells_add_remove_view, "widget", None)
        if arm_widget is None:
            return tb_pos[0] + tb_w + UI_MARGIN
        arm_w, _ = arm_widget.panel.surface.get_size()
        return tb_pos[0] + tb_w + UI_MARGIN + arm_w + UI_MARGIN
    except Exception:
        return None


def set_properties_anchor(controller: Any) -> None:
    """Anchor the properties panel to the right of the picker grid, or reset if grid is absent."""
    grid_rect = getattr(controller.view, "grid_rect", None)
    try:
        if grid_rect is not None:
            left_x = grid_rect.right + UI_MARGIN
            top_y = grid_rect.y
            controller.spells_properties_controller.view.set_anchor(left_x, top_y)
        else:
            controller.spells_properties_controller.view.set_anchor(None, None)
    except Exception:
        pass
