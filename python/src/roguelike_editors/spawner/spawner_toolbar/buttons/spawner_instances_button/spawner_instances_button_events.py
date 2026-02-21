from __future__ import annotations

from roguelike_editors.spawner.spawner_toolbar.spawner_toolbar_model import TOOL_SPAWNER_INSTANCES
from roguelike_editors.spawner.spawner_toolbar.service.ui_state import apply_ui_state_basic


class SpawnerInstancesButtonEvents:
    @staticmethod
    def on_key_toggle(controller) -> bool:
        try:
            import pygame  # type: ignore
        except Exception:
            return False
        new_state = None if controller.is_active(TOOL_SPAWNER_INSTANCES) else TOOL_SPAWNER_INSTANCES
        controller.set_active(new_state)
        apply_ui_state_basic(controller)
        return True

    @staticmethod
    def on_click(controller) -> bool:
        controller.set_active(TOOL_SPAWNER_INSTANCES)
        apply_ui_state_basic(controller)
        return True


__all__ = ["SpawnerInstancesButtonEvents"]
