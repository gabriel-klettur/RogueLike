from __future__ import annotations

from typing import Any, Optional

from .action_grid_model import ActionGridModel


class ActionGridController:
    """Synchronizes the grid model with input bindings and formats binding labels."""

    def __init__(self, *, input_config: Optional[Any] = None) -> None:
        self.input_config = input_config

    def sync_bindings(self, model: ActionGridModel) -> None:
        # Placeholder for future caching of resolved bindings
        return

    def update(self, model: ActionGridModel, *, world: Optional[Any] = None, camera: Optional[Any] = None) -> None:
        # Placeholder update (pressed states to be added later)
        return

    # --- Binding label helpers ---
    def _short_key(self, name: str | None) -> str:
        if not name or not isinstance(name, str):
            return ""
        up = name.upper()
        if up.startswith("K_"):
            # K_Q -> Q, K_SPACE -> SPACE
            return name[2:]
        if up.startswith("M_"):
            # Mouse buttons to common abbreviations
            mapping = {
                "M_LEFT": "LMB",
                "M_RIGHT": "RMB",
                "M_MIDDLE": "MMB",
                "M_X1": "X1",
                "M_X2": "X2",
            }
            return mapping.get(up, up[2:])
        return name

    def get_binding_label(self, action: str) -> str:
        """Return a compact label like 'A:Q B:E M:RMB' for the action bindings."""
        try:
            cfg = self.input_config
            if cfg is None or not hasattr(cfg, 'bindings'):
                return ""
            a = cfg.bindings.get(f"kb_{action}_a", "")
            b = cfg.bindings.get(f"kb_{action}_b", "")
            m = cfg.bindings.get(f"mouse_{action}", "")
            parts: list[str] = []
            sa = self._short_key(a)
            sb = self._short_key(b)
            sm = self._short_key(m)
            if sa:
                parts.append(f"A:{sa}")
            if sb:
                parts.append(f"B:{sb}")
            if sm:
                parts.append(f"M:{sm}")
            return " ".join(parts)
        except Exception:
            return ""
