from dataclasses import dataclass, field
import pygame


@dataclass
class BuildingCollidersPanelModel:
    active: bool = False
    picker_open: bool = False
    picker_pos: tuple[int, int] | None = None
    picker_dragging: bool = False
    picker_drag_offset: tuple[int, int] = (0, 0)
    picker_panel_size: tuple[int, int] = (0, 0)
    picker_rects: dict[str, pygame.Rect] = field(default_factory=dict)

    choice: str | None = None  # '#' sólido, '.' caminable
    brush_dragging: bool = False
    active_building = None

    def reset_runtime(self):
        self.picker_dragging = False
        self.brush_dragging = False
        self.picker_rects.clear()
