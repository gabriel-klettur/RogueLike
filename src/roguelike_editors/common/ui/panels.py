import pygame
from typing import Tuple


def draw_translucent_panel(
    surface: pygame.Surface,
    rect: pygame.Rect,
    *,
    bg_rgba: Tuple[int, int, int, int] = (24, 26, 32, 170),
    border_rgba: Tuple[int, int, int, int] = (255, 255, 255, 30),
    radius: int = 8,
    shadow: bool = True,
    shadow_offset: Tuple[int, int] = (2, 2),
    shadow_alpha: int = 120,
    shadow_inflate: int = 6,
    border_width: int = 1,
) -> None:
    """
    Dibuja un panel translúcido con esquinas redondeadas, contorno tenue y sombra opcional.

    - surface: superficie destino
    - rect: pygame.Rect destino
    - bg_rgba: color de fondo con alpha
    - border_rgba: color de borde con alpha (si border_width > 0)
    - radius: radio de esquinas
    - shadow: si dibuja sombra
    - shadow_offset: desplazamiento de la sombra (x, y)
    - shadow_alpha: opacidad de la sombra [0..255]
    - shadow_inflate: expansión de la sombra respecto al rect
    - border_width: ancho de borde (0 para sin borde)
    """
    if rect.width <= 0 or rect.height <= 0:
        return

    # Sombra (simple, sin blur real) dibujada en surface de alpha independiente
    if shadow:
        sx, sy = shadow_offset
        shadow_rect = rect.inflate(shadow_inflate, shadow_inflate).move(sx, sy)
        shadow_surf = pygame.Surface((shadow_rect.width, shadow_rect.height), pygame.SRCALPHA)
        # Negro con alpha configurable
        pygame.draw.rect(
            shadow_surf,
            (0, 0, 0, shadow_alpha),
            pygame.Rect(0, 0, shadow_rect.width, shadow_rect.height),
            border_radius=max(0, radius + shadow_inflate // 2),
        )
        surface.blit(shadow_surf, shadow_rect.topleft)

    # Panel principal con alpha
    panel_surf = pygame.Surface((rect.width, rect.height), pygame.SRCALPHA)
    pygame.draw.rect(
        panel_surf,
        bg_rgba,
        pygame.Rect(0, 0, rect.width, rect.height),
        border_radius=radius,
    )
    # Borde tenue
    if border_width > 0 and border_rgba[3] > 0:
        pygame.draw.rect(
            panel_surf,
            border_rgba,
            pygame.Rect(0, 0, rect.width, rect.height),
            width=border_width,
            border_radius=radius,
        )

    surface.blit(panel_surf, rect.topleft)
