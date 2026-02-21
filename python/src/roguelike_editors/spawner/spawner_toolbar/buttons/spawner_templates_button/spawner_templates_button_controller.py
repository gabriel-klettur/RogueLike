from __future__ import annotations

from .spawner_templates_button_events import SpawnerTemplatesButtonEvents


class SpawnerTemplatesButtonController:
    def on_click(self, toolbar_controller) -> bool:
        return bool(SpawnerTemplatesButtonEvents.on_click(toolbar_controller))


__all__ = ["SpawnerTemplatesButtonController"]
