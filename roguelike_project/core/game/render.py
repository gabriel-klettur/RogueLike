# core/game/render.py

import pygame
from roguelike_project.core.game.multiplayer import render_remote_players
from roguelike_project.map import map_loader  # ✅ Importado para TILE_SIZE

def render_game(state):
    screen = state.screen
    screen.fill((0, 0, 0))    

    for tile in state.tiles:
        tile.render(screen, state.camera)

    for obstacle in state.obstacles:
        obstacle.render(screen, state.camera)

    state.player.render(screen, state.camera)
    state.player.render_hud(screen, state.camera)

    render_remote_players(state)

    if state.show_menu:
        state.menu.draw(screen)

    # FPS
    fps = state.clock.get_fps()
    fps_text = state.font.render(f"FPS: {int(fps)}", True, (255, 255, 255))
    pygame.draw.rect(screen, (0, 0, 0), (8, 8, 60, 22))
    screen.blit(fps_text, (10, 10))

    # Modo
    mode_text = state.font.render(f"Modo: {state.mode}", True, (255, 255, 255))
    pygame.draw.rect(screen, (0, 0, 0), (8, 30, 130, 22))
    screen.blit(mode_text, (10, 32))

    render_minimap(state)  # ✅ Minimapa al final

    pygame.display.flip()

def render_minimap(state):
    minimap_width = 200
    minimap_height = 150
    minimap_surface = pygame.Surface((minimap_width, minimap_height))
    minimap_surface.set_alpha(180)
    minimap_surface.fill((10, 10, 10))

    tile_size = 4  # escala en el minimapa
    center_x = minimap_width // 2
    center_y = minimap_height // 2

    player_tile_x = state.player.x // map_loader.TILE_SIZE
    player_tile_y = state.player.y // map_loader.TILE_SIZE

    for tile in state.tiles:
        tile_x = tile.x // map_loader.TILE_SIZE
        tile_y = tile.y // map_loader.TILE_SIZE

        dx = (tile_x - player_tile_x) * tile_size
        dy = (tile_y - player_tile_y) * tile_size

        draw_x = center_x + dx
        draw_y = center_y + dy

        if 0 <= draw_x < minimap_width and 0 <= draw_y < minimap_height:
            color = (60, 60, 60) if tile.tile_type == "." else (150, 50, 50)
            pygame.draw.rect(minimap_surface, color, (draw_x, draw_y, tile_size, tile_size))

    # Dibuja el jugador en el centro
    pygame.draw.rect(minimap_surface, (0, 255, 0), (center_x, center_y, tile_size, tile_size))

    # Mostrar minimapa en pantalla (arriba a la derecha)
    screen_width = state.screen.get_width()
    state.screen.blit(minimap_surface, (screen_width - minimap_width - 20, 20))
