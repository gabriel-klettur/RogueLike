# core/game/render/minimap.py

import pygame
from src.roguelike_project.engine.game.systems.map import tile_loader

def render_minimap(state):
    minimap_width = 200
    minimap_height = 150
    minimap_surface = pygame.Surface((minimap_width, minimap_height))
    minimap_surface.set_alpha(180)
    minimap_surface.fill((10, 10, 10))

    # 🎚️ Zoom del minimapa (ajustable manualmente)
    minimap_zoom = 1  # Cambiá este número para acercar o alejar (ej: 3 = más alejado, 10 = más cerca)

    # 🧭 Centro del minimapa (jugador)
    center_x = minimap_width // 2
    center_y = minimap_height // 2

    player_tile_x = state.player.x // tile_loader.TILE_SIZE
    player_tile_y = state.player.y // tile_loader.TILE_SIZE

    # 🎨 Colores por tipo de tile
    tile_colors = {
        ".": (80, 80, 80),     # piso
        "R": (130, 130, 130),  # habitación
        "T": (100, 100, 100),  # túnel
        "#": (30, 30, 30),     # pared
        "D": (90, 90, 90),     # legacy dungeon
    }

    for tile in state.tiles:
        tile_x = tile.x // tile_loader.TILE_SIZE
        tile_y = tile.y // tile_loader.TILE_SIZE

        dx = (tile_x - player_tile_x) * minimap_zoom
        dy = (tile_y - player_tile_y) * minimap_zoom

        draw_x = center_x + dx
        draw_y = center_y + dy

        if 0 <= draw_x < minimap_width and 0 <= draw_y < minimap_height:
            color = tile_colors.get(tile.tile_type, (255, 0, 255))  # fallback: magenta
            pygame.draw.rect(minimap_surface, color, (draw_x, draw_y, minimap_zoom, minimap_zoom))

    # 🟢 Jugador
    pygame.draw.rect(minimap_surface, (0, 255, 0), (center_x, center_y, minimap_zoom, minimap_zoom))

    # 📍 Posición fija: arriba a la derecha
    screen_width = state.screen.get_width()
    state.screen.blit(minimap_surface, (screen_width - minimap_width - 20, 20))
