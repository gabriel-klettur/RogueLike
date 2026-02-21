from __future__ import annotations

from roguelike_editors.spawner.spawner_toolbar.spawner_toolbar_model import TOOL_SPAWNER_TEMPLATES
from roguelike_editors.spawner.spawner_toolbar.service.ui_state import apply_ui_state_ensure_manager


class SpawnerTemplatesButtonEvents:
    @staticmethod
    def on_click(controller) -> bool:
        controller.set_active(TOOL_SPAWNER_TEMPLATES)
        apply_ui_state_ensure_manager(controller)
        return True


__all__ = ["SpawnerTemplatesButtonEvents"]
