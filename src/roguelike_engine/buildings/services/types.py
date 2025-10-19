from typing import Literal, Protocol, runtime_checkable
import pygame

# Type aliases for readability across the buildings package
CollisionMap = list[list[str]]
VisualStateMap = dict[str, str]
StateThresholds = list[dict]
RectList = list[pygame.Rect]

# Collider scope type for per-building collision scope
ColliderScope = Literal["CG", "CU"]


@runtime_checkable
class CameraProtocol(Protocol):
    """
    Minimal camera protocol used by Buildings components.
    Implementations must provide:
    - zoom: float
    - scale(size: (w,h)) -> (w,h): returns scaled size for current zoom
    - apply(pos: (x,y)) -> (sx,sy): converts world coords to screen coords
    """
    zoom: float

    def scale(self, size: tuple[int, int]) -> tuple[int, int]:
        ...

    def apply(self, pos: tuple[int, int]) -> tuple[int, int]:
        ...
