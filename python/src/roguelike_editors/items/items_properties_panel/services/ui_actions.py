from __future__ import annotations

import logging
from typing import Any

logger = logging.getLogger(__name__)


def after_confirm_cleanup(controller: Any) -> None:
    """Cleanup UI and state after confirming an item save.

    - Hides add-on-system selector
    - Clears draft and editing state
    - Resets selection
    - Restores editor layout and refreshes catalogs
    - Emits tutorial pulses when available
    """
    try:
        controller.model.show_add_system_selector = False
        try:
            controller.model.new_item_draft.clear()
        except Exception:
            pass
        controller.model.editing_property = None
        controller.model.editing_text = ""
        controller.model.editing_cursor = 0
        controller._selected_id = None
        controller._hovered_id = None
        if controller.editor_controller is not None:
            try:
                arm = getattr(controller.editor_controller, 'items_add_remove_model', None)
                if arm and getattr(arm, 'active_tool', None) == 'add_item_on_system':
                    arm.active_tool = None
            except Exception:
                pass
            try:
                if hasattr(controller.editor_controller, 'exit_add_items_on_system_mode'):
                    controller.editor_controller.exit_add_items_on_system_mode()
            except Exception:
                pass
            try:
                controller.editor_controller.picker_controller.model.visible = True
            except Exception:
                pass
            try:
                controller.editor_controller._refresh_items_catalog()
            except Exception:
                logger.exception("[ItemsPropertiesPanel] Failed to refresh items catalog after confirm")
            try:
                setattr(controller.editor_controller.model, 'tutorial_add_system_confirm_pulse', True)
            except Exception:
                pass
    except Exception:
        pass
