from __future__ import annotations

from typing import Protocol, runtime_checkable, Tuple, Optional, Iterable, Any


@runtime_checkable
class CameraLike(Protocol):
    zoom: float
    offset_x: float
    offset_y: float
    screen_width: int
    screen_height: int

    def scale(self, size: Tuple[int, int]) -> Tuple[int, int]:
        ...

    def apply(self, pos: Tuple[int, int]) -> Tuple[int, int]:
        ...


@runtime_checkable
class ClockLike(Protocol):
    def get_fps(self) -> float: ...


@runtime_checkable
class StateLike(Protocol):
    clock: ClockLike
    mode: Any


@runtime_checkable
class RectLike(Protocol):
    def collidepoint(self, x: int, y: int) -> bool: ...


@runtime_checkable
class TileLike(Protocol):
    rect: RectLike
    tile_type: str


@runtime_checkable
class MapManagerLike(Protocol):
    lobby_offset: Tuple[int, int]
    tiles_in_region: Iterable[TileLike]


@runtime_checkable
class PlayerLike(Protocol):
    x: float
    y: float


@runtime_checkable
class EntitiesLike(Protocol):
    player: PlayerLike


__all__ = [
    "CameraLike",
    "StateLike",
    "MapManagerLike",
    "EntitiesLike",
]
