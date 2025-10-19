from __future__ import annotations

import pygame
from typing import Any, Tuple


def start_drag(editor: Any, building: Any, world_x: float, world_y: float) -> None:
    """Initialize dragging state and offsets for the given building."""
    editor.selected_building = building
    editor.dragging = True
    editor.offset_x = world_x - building.x
    editor.offset_y = world_y - building.y


def start_resize(editor: Any, building: Any, mouse_start: Tuple[int, int]) -> None:
    """Initialize resizing state and store initial size and origin."""
    editor.selected_building = building
    editor.resizing = True
    editor.resize_origin = mouse_start
    editor.initial_size = building.image.get_size()


def update(editor: Any, camera: Any, resize_tool: Any, split_tool: Any) -> None:
    """Update dragging/resizing/split-dragging according to current editor flags."""
    if editor.dragging and editor.selected_building:
        mx, my = pygame.mouse.get_pos()
        wx = mx / camera.zoom + camera.offset_x
        wy = my / camera.zoom + camera.offset_y
        b = editor.selected_building
        b.x = wx - editor.offset_x
        b.y = wy - editor.offset_y
        b.rect.topleft = (b.x, b.y)
    elif editor.resizing and editor.selected_building:
        resize_tool.update_resizing(pygame.mouse.get_pos())
    elif editor.split_dragging:
        split_tool.update_drag(pygame.mouse.get_pos(), camera)
