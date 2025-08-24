"""
Module: spawn_debug_system.py
Provides SpawnDebugSystem to visualize NPC spawn points
when the FSM Editor is active.
"""
import pygame
import roguelike_engine.config.config as config
from roguelike_engine.config.config_tiles import TILE_SIZE
from roguelike_engine.utils.benchmark import benchmark

class SpawnDebugSystem:
    """
    Sistema que dibuja marcadores de spawn de NPCs en pantalla 
    cuando el FSM Editor está activo y el mundo define spawn_tiles.
    """
    def __init__(self, perf_log):
        self.perf_log = perf_log
        # cache for fonts and text surfaces per zoom level and eid
        self.fonts = {}
        self.text_surfs = {}
    
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
        # Solo renderizar spawn debug cuando el FSM Editor está activo (F12)
        if not config.DEBUG_ENTITIES or not getattr(world, 'spawn_tiles', None):
            return

        # view frustum culling
        view_rect = pygame.Rect(0, 0, camera.screen_width, camera.screen_height)

        # Iterar cada punto de spawn (tile_x, tile_y, entity_id)
        for tx, ty, eid in world.spawn_tiles:
            # Convertir coordenadas de tile a píxeles
            world_x = tx * TILE_SIZE
            world_y = ty * TILE_SIZE

            # Transformar a coordenadas de pantalla con la cámara
            screen_x, screen_y = camera.apply((world_x, world_y))

            # Calcular tamaño del rectángulo según zoom de cámara
            size = int(TILE_SIZE * camera.zoom)
            rect = pygame.Rect(int(screen_x), int(screen_y), size, size)
            if not rect.colliderect(view_rect):
                continue

            # Dibujar borde rojo del rectángulo
            pygame.draw.rect(screen, (255, 0, 0), rect, 2)

            # Preparar texto con el ID de la entidad
            font_size = max(8, size // 2)
            # cache font per size
            font = self.fonts.get(font_size)
            if font is None:
                font = pygame.font.SysFont(None, font_size)
                self.fonts[font_size] = font
            # cache text surface per eid and size
            key = (eid, font_size)
            text_surf = self.text_surfs.get(key)
            if text_surf is None:
                text_surf = font.render(str(eid), True, (255, 0, 0))
                self.text_surfs[key] = text_surf

            # Centrar el texto dentro del rectángulo
            text_rect = text_surf.get_rect(center=rect.center)
            screen.blit(text_surf, text_rect)