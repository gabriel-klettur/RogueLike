from __future__ import annotations


class FsmGraphPanelEventHandler:
    def handle_event(self, controller, event) -> bool:
        # TODO: MMB pan, Ctrl+Wheel zoom, LMB select/drag, connect tool
        return False


__all__ = ["FsmGraphPanelEventHandler"]
