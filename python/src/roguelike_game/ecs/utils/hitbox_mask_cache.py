"""
Cache de máscaras de hitbox para arcos/sectores.

Optimización de rendimiento: evita recrear pygame.Surface y pygame.mask
cada frame para hitboxes con los mismos parámetros.

Uso:
    mask = get_arc_mask(radius=50, arc_angle=1.57, direction_angle=0.0)
"""
from __future__ import annotations

import math
from typing import Dict, Tuple, Optional
import pygame

import logging

logger = logging.getLogger(__name__)


# Cache global: (radius, arc_angle_rounded, segments) -> (mask, surface)
_HITBOX_MASK_CACHE: Dict[Tuple[int, int, int], Tuple[pygame.mask.Mask, pygame.Surface]] = {}

# Precisión del ángulo para cache (en centésimas de radián)
_ANGLE_PRECISION: int = 100


def _round_angle(angle: float) -> int:
    """Redondea ángulo a entero para usar como key de cache."""
    return int(round(angle * _ANGLE_PRECISION))


def get_arc_mask(
    radius: int,
    arc_angle: float,
    segments: int = 16,
    return_surface: bool = False
) -> pygame.mask.Mask | Tuple[pygame.mask.Mask, pygame.Surface]:
    """Obtiene máscara de arco desde cache o la genera.
    
    La máscara se genera centrada en (radius, radius) apuntando hacia
    la derecha (ángulo 0). Para usarla con una dirección específica,
    rotar el offset de colisión, no la máscara.
    
    Args:
        radius: Radio del arco en píxeles.
        arc_angle: Ángulo del arco en radianes (ej: π/2 = 90°).
        segments: Número de segmentos para aproximar el arco.
        return_surface: Si True, retorna también la superficie para debug.
        
    Returns:
        pygame.mask.Mask del arco, o tupla (mask, surface) si return_surface=True.
    """
    # Normalizar parámetros para cache
    radius = max(1, int(radius))
    arc_angle_key = _round_angle(arc_angle)
    segments = max(4, min(32, segments))
    
    key = (radius, arc_angle_key, segments)
    
    cached = _HITBOX_MASK_CACHE.get(key)
    if cached is not None:
        if return_surface:
            return cached
        return cached[0]
    
    # Generar máscara
    size = radius * 2
    surf = pygame.Surface((size, size), pygame.SRCALPHA)
    center = radius
    
    # Dibujar sector centrado en ángulo 0 (hacia la derecha)
    half_arc = arc_angle / 2.0
    pts = [(center, center)]
    
    for i in range(segments + 1):
        t = i / segments
        ang = -half_arc + arc_angle * t
        pts.append((
            center + math.cos(ang) * radius,
            center + math.sin(ang) * radius
        ))
    
    pygame.draw.polygon(surf, (255, 255, 255), pts)
    mask = pygame.mask.from_surface(surf)
    
    # Cachear
    _HITBOX_MASK_CACHE[key] = (mask, surf)
    
    if logger.isEnabledFor(logging.DEBUG):
        logger.debug(
            "[HitboxMaskCache] Created mask: radius=%d, arc=%.2f, segments=%d (cache size: %d)",
            radius, arc_angle, segments, len(_HITBOX_MASK_CACHE)
        )
    
    if return_surface:
        return mask, surf
    return mask


def get_rotated_arc_mask(
    radius: int,
    arc_angle: float,
    direction_angle: float,
    segments: int = 16
) -> Tuple[pygame.mask.Mask, int, int]:
    """Obtiene máscara de arco rotada hacia una dirección específica.
    
    Nota: Esta función es más costosa porque rota la superficie.
    Preferir get_arc_mask + rotación de offset cuando sea posible.
    
    Args:
        radius: Radio del arco en píxeles.
        arc_angle: Ángulo del arco en radianes.
        direction_angle: Dirección hacia donde apunta el arco (radianes).
        segments: Número de segmentos.
        
    Returns:
        Tupla (mask, offset_x, offset_y) donde offset es la corrección
        de posición tras la rotación.
    """
    # Obtener máscara base (apuntando a la derecha)
    base_mask, base_surf = get_arc_mask(radius, arc_angle, segments, return_surface=True)
    
    # Rotar superficie
    angle_deg = -math.degrees(direction_angle)  # pygame rota en sentido antihorario
    rotated = pygame.transform.rotate(base_surf, angle_deg)
    
    # Calcular offset por cambio de tamaño tras rotación
    orig_center = radius
    new_w, new_h = rotated.get_size()
    offset_x = (new_w // 2) - orig_center
    offset_y = (new_h // 2) - orig_center
    
    rotated_mask = pygame.mask.from_surface(rotated)
    
    return rotated_mask, offset_x, offset_y


def clear_hitbox_cache() -> None:
    """Limpia el cache de máscaras de hitbox."""
    _HITBOX_MASK_CACHE.clear()


def get_cache_stats() -> dict:
    """Retorna estadísticas del cache."""
    return {
        "cached_masks": len(_HITBOX_MASK_CACHE),
        "unique_radii": len(set(k[0] for k in _HITBOX_MASK_CACHE.keys())),
        "unique_angles": len(set(k[1] for k in _HITBOX_MASK_CACHE.keys())),
    }


# Pre-cachear máscaras comunes al importar el módulo
def _precache_common_masks() -> None:
    """Pre-genera máscaras para configuraciones comunes."""
    common_radii = [30, 40, 50, 60, 80, 100]
    common_arcs = [
        math.pi / 4,   # 45°
        math.pi / 3,   # 60°
        math.pi / 2,   # 90°
        2 * math.pi / 3,  # 120°
        math.pi,       # 180°
    ]
    
    for r in common_radii:
        for arc in common_arcs:
            get_arc_mask(r, arc)


# Ejecutar pre-cache (comentar si causa lag en startup)
# _precache_common_masks()
