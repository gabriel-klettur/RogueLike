from __future__ import annotations

from .spawner_instances_button_events import SpawnerInstancesButtonEvents


class SpawnerInstancesButtonController:
    def on_key_toggle(self, toolbar_controller) -> bool:
        return bool(SpawnerInstancesButtonEvents.on_key_toggle(toolbar_controller))

    def on_click(self, toolbar_controller) -> bool:
        return bool(SpawnerInstancesButtonEvents.on_click(toolbar_controller))


__all__ = ["SpawnerInstancesButtonController"]
