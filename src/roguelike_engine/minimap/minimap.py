# Path: src/roguelike_engine/minimap/minimap.py

import pygame
from roguelike_engine.config_tiles import TILE_SIZE


def render_minimap(state, screen):
    """
    Dibuja un minimapa en la esquina superior derecha mostrando los tiles cercanos al jugador.
    """
    minimap_width = 200
    minimap_height = 150
    minimap_surface = pygame.Surface((minimap_width, minimap_height))
    minimap_surface.set_alpha(180)
    minimap_surface.fill((10, 10, 10))

    # Zoom del minimapa (en unidades de tiles)
    minimap_zoom = 1  # Ajustar para cambiar nivel de detalle

    # Centro en coordenadas de píxel (jugador)
    center_x = minimap_width // 2
    center_y = minimap_height // 2

    # Convertir posición del jugador a coordenadas de tile
    player_tile_x = int(state.player.x) // TILE_SIZE
    player_tile_y = int(state.player.y) // TILE_SIZE

    # Colores por tipo de tile
    tile_colors = {
        ".": (80, 80, 80),    # piso
        "O": (130, 130, 130), # habitación
        "=": (100, 100, 100), # túnel
        "#": (30, 30, 30),    # pared
        "D": (90, 90, 90),    # dungeon genérico
    }

    # Dibujar cada tile
    for tile in state.tiles:
        # Cada tile.x, tile.y están en píxeles
        tile_x = int(tile.x) // TILE_SIZE
        tile_y = int(tile.y) // TILE_SIZE

        dx = (tile_x - player_tile_x) * minimap_zoom
        dy = (tile_y - player_tile_y) * minimap_zoom

        draw_x = center_x + dx
        draw_y = center_y + dy

        if 0 <= draw_x < minimap_width and 0 <= draw_y < minimap_height:
            color = tile_colors.get(tile.tile_type, (255, 0, 255))
            pygame.draw.rect(
                minimap_surface,
                color,
                (draw_x, draw_y, minimap_zoom, minimap_zoom)
            )

    # Dibujar jugador en el centro
    pygame.draw.rect(
        minimap_surface,
        (0, 255, 0),
        (center_x, center_y, minimap_zoom, minimap_zoom)
    )

    # Mostrar en pantalla
    screen_width = screen.get_width()
    screen.blit(
        minimap_surface,
        (screen_width - minimap_width - 20, 20)
    )
