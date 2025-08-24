"""Bridge to the existing tools registry used by the FSM Graph Panel toolbar.

This module re-exports the implementation under
`fsm_graph_panel/toolbar_graph_panel/services/tools_registry.py` to centralize
future imports under `fsm_graph_panel/services/tools_registry.py`.
"""
from __future__ import annotations

try:
    # Re-export everything from the existing registry (if present)
    from ..toolbar_graph_panel.services.tools_registry import *  # type: ignore
except Exception:  # pragma: no cover - safe no-op if source is missing
    # Optional: provide minimal placeholders here if needed later
    pass
