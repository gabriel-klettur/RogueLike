from typing import Tuple


def intersect(room1: Tuple[int, int, int, int], room2: Tuple[int, int, int, int]) -> bool:
    """Return True if two axis-aligned rectangles (x1,y1,x2,y2) intersect."""
    x1a, y1a, x2a, y2a = room1
    x1b, y1b, x2b, y2b = room2
    return (
        x1a <= x2b and x2a >= x1b and
        y1a <= y2b and y2a >= y1b
    )


def center_of(room: Tuple[int, int, int, int]) -> Tuple[int, int]:
    """Return the integer center (cx, cy) of a rectangle (x1,y1,x2,y2)."""
    x1, y1, x2, y2 = room
    return (x1 + x2) // 2, (y1 + y2) // 2
