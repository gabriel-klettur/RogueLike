from __future__ import annotations
from dataclasses import dataclass
from typing import Callable
import pygame
from roguelike_engine.buildings.services.types import CameraProtocol


@dataclass(slots=True)
class RenderablePart:
    """
    Pequeña estructura de datos para representar una parte renderizable de un edificio
    con su posición absoluta (x, y), su z para ordenación y una imagen de referencia.

    El método "render" debe dibujar la parte en pantalla usando la cámara provista.
    Contrato esperado: render(screen: pygame.Surface, camera: CameraProtocol) -> None
    """
    x: int
    y: int
    z: int
    image: pygame.Surface
    render: Callable[[pygame.Surface, CameraProtocol], None]


__all__ = ["RenderablePart"]
