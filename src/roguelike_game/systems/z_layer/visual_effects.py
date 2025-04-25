# src.roguelike_project/systems/z_layer/visual_effects.py

"""
Efectos visuales basados en capas Z.
Se aplican solo si el modo DEBUG está activo.
"""

import pygame
import src.roguelike_engine.config as config
from .logic import is_above, is_below

def apply_z_visual_effect(entity, player, screen, camera, z_state):
    """
    Aplica efectos visuales (bordes, sombras) según la capa Z relativa al jugador.
    Solo activo si config.DEBUG == True.
    """
    if not config.DEBUG:
        return

    screen_pos = camera.apply((entity.x, entity.y))
    sprite_size = getattr(entity, "sprite_size", (64, 64))
    entity_rect = pygame.Rect(screen_pos, camera.scale(sprite_size))

    if is_above(entity, player, z_state):
        # 🔵 Glow azul para entidades por encima
        pygame.draw.rect(screen, (100, 100, 255), entity_rect, 2)

    elif is_below(entity, player, z_state):
        # ⚫ Sombra tenue
        shadow = pygame.Surface(entity_rect.size, flags=pygame.SRCALPHA)
        shadow.fill((0, 0, 0, 80))
        screen.blit(shadow, entity_rect.topleft)

    else:
        # ⚪ Resalte blanco solo si está en la misma capa
        pygame.draw.rect(screen, (255, 255, 255), entity_rect, 1)
