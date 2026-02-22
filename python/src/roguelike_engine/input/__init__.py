"""Engine-level input routing and handlers.

Public API:
- handle_events: main event router for engine/editor contexts.
- handle_keyboard: engine keyboard handler (zoom +/-).
- handle_mouse: engine mouse handler (wheel zoom, MMB pan in editors).
"""

from .events import handle_events
from .keyboard import handle_keyboard
from .mouse import handle_mouse

__all__ = [
    "handle_events",
    "handle_keyboard",
    "handle_mouse",
]
