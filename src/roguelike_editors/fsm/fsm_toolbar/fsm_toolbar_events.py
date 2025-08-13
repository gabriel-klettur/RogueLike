from __future__ import annotations


class FsmToolbarEventHandler:
    def handle_event(self, controller, event) -> bool:
        # TODO: hit-test buttons, change controller.model.active_tool
        return False


__all__ = ["FsmToolbarEventHandler"]
