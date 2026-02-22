"""UI helper functions for Entities editor.
"""
from __future__ import annotations


def hide_assets_picker_and_clear_properties(properties_controller) -> None:
    """Hide the assets picker (if visible) and clear properties panel state.

    This keeps the properties panel from drawing or intercepting events while
    the user is in map actions like spawn/delete.
    """
    # Hide assets picker if present
    try:
        properties_controller.assets_picker_controller.hide()
    except Exception:
        pass
    # Clear properties panel model state
    try:
        pm = properties_controller.model
        pm.editing_property = None
        pm.focused_property = None
        pm.hovered_property = None
        pm.panel_rect = None
        pm.selected_id = None
        pm.hovered_entity_id = None
    except Exception:
        pass
