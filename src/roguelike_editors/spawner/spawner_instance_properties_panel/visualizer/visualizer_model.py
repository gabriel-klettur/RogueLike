from __future__ import annotations

from typing import Optional, List, Dict

try:
    from roguelike_ui.widgets.text_input import TextInput  # type: ignore
except Exception:  # pragma: no cover
    TextInput = None  # type: ignore

try:
    import pygame  # type: ignore
except Exception:  # pragma: no cover
    pygame = None  # type: ignore


class VisualizerModel:
    """UI state for the Visuals table inside the Instance Properties panel.

    This model does NOT duplicate the spawner instance visuals mapping; that remains
    owned by InstancePropertiesModel in the parent controller. Here we only keep
    editor-only UI state like per-building visibility and cached rects for hit-testing.
    """

    def __init__(self) -> None:
        # Editor-only visibility map by building instance id
        self.editor_visibility: Dict[int, bool] = {}
        # Which display state key is currently being edited (via text input)
        self.visuals_editing_state: Optional[str] = None
        # Dedicated TextInput for the Visuals table (isolated from parent row editor)
        self.text_input: Optional[TextInput] = None  # type: ignore
        # Cached rects (panel-local coordinates) for hit testing and tooltips
        self.visuals_template_rects: List["pygame.Rect"] = [] if pygame else []
        self.visuals_plus_rects: List["pygame.Rect"] = [] if pygame else []
        self.visuals_browse_rects: List["pygame.Rect"] = [] if pygame else []
        self.visuals_eye_rects: List["pygame.Rect"] = [] if pygame else []
        self.visuals_state_rects: List["pygame.Rect"] = [] if pygame else []


__all__ = ["VisualizerModel"]
