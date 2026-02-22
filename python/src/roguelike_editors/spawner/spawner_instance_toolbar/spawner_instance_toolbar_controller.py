from __future__ import annotations

"""Spawner Instance Toolbar Controller.

Provides actions for the spawner instances toolbar used by the editor.
Currently implements a debounced toggle for the Remove Spawner tool that the
unit tests rely on.
"""

from typing import Optional
import time


class SpawnerInstanceToolbarController:
    """Controller for spawner instance toolbar actions.

    Parameters
    ----------
    editor_controller:
        The high-level editor controller that owns the shared `model` used
        across spawner editor views.
    debounce_seconds:
        Minimum time required between consecutive toggles of remove mode.
    """

    def __init__(self, editor_controller, debounce_seconds: float = 0.7) -> None:
        self.editor_controller = editor_controller
        self.model = getattr(editor_controller, "model", None)
        self.debounce_seconds = debounce_seconds
        self._last_remove_toggle_ts: Optional[float] = None

    def on_remove_spawner(self) -> None:
        """Toggle remove mode with debounce.

        - First call turns remove mode ON.
        - Calls within the debounce window are ignored.
        - After the debounce window, the mode toggles OFF on the next call.
        """
        now = float(time.time())
        last = self._last_remove_toggle_ts
        if last is not None and (now - last) < self.debounce_seconds:
            # Within debounce window: ignore
            return
        self._last_remove_toggle_ts = now

        if self.model is None:
            return

        current = bool(getattr(self.model, "remove_mode_active", False))
        setattr(self.model, "remove_mode_active", not current)
