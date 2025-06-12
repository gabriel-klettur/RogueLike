"""
Module: spawn_debug_system.py
Provides SpawnDebugSystem to visualize NPC spawn points
on the map when DEBUG mode is enabled.
"""

import pygame
import roguelike_engine.config.config as config
from roguelike_engine.config.config_tiles import TILE_SIZE
from roguelike_engine.utils.benchmark import benchmark

class SpawnDebugSystem:
    """
    Sistema que dibuja marcadores de spawn de NPCs en pantalla 
    si la configuración DEBUG está activada y el mundo define spawn_tiles.
    """
    def __init__(self, perf_log):
        self.perf_log = perf_log

    @benchmark(lambda self: self.perf_log, "4.2.2.SpawnDebugSystem.update")
    def update(self, world, screen, camera):
        """
        Recorre la lista world.spawn_tiles y dibuja:
          - Un rectángulo rojo alrededor del tile de spawn.
          - El ID de la entidad centrado dentro del rectángulo.
        
        Parámetros:
          world  – Mundo ECS que puede contener spawn_tiles como lista de tuplas (tx, ty, eid).
          screen – Superficie de pygame donde dibujar.
          camera – Objeto que transforma coordenadas de mundo a pantalla.
        """
        # Solo renderizar spawn debug en modo ENTITIES DEBUG (F12)
        if not config.DEBUG_ENTITIES or not hasattr(world, 'spawn_tiles'):
            return

        # Iterar cada punto de spawn (tile_x, tile_y, entity_id)
        for tx, ty, eid in world.spawn_tiles:
            # Convertir coordenadas de tile a píxeles
            world_x = tx * TILE_SIZE
            world_y = ty * TILE_SIZE

            # Transformar a coordenadas de pantalla con la cámara
            screen_x, screen_y = camera.apply((world_x, world_y))

            # Calcular tamaño del rectángulo según zoom de cámara
            size = int(TILE_SIZE * camera.zoom)
            rect = pygame.Rect(screen_x, screen_y, size, size)

            # Dibujar borde rojo del rectángulo
            pygame.draw.rect(screen, (255, 0, 0), rect, 2)

            # Preparar texto con el ID de la entidad
            font_size = max(8, size // 2)  # tamaño mínimo para legibilidad
            font = pygame.font.SysFont(None, font_size)
            text_surf = font.render(str(eid), True, (255, 0, 0))

            # Centrar el texto dentro del rectángulo
            text_rect = text_surf.get_rect(center=rect.center)
            screen.blit(text_surf, text_rect)
