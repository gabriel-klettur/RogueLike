from __future__ import annotations

from typing import Any


class FsmGraphPanelController:
    """
    Orchestrator controller for FSM Graph Panel (stub).
    Intended to delegate to events.*, services.tools_registry, and sub-views.
    """

    def __init__(self) -> None:
        # TODO: wire model, view, events, and active tool registry
        pass

    def handle_event(self, event: Any) -> bool:
        """Return True if the event was consumed."""
        return False
