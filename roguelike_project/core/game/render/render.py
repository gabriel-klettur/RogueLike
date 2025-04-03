import pygame
from roguelike_project.core.game.multiplayer.multiplayer import render_remote_players
from roguelike_project.core.game.render.minimap import render_minimap
from roguelike_project.utils.mouse import draw_mouse_crosshair
from roguelike_project.config import DEBUG

def render_game(state):
    screen = state.screen
    screen.fill((0, 0, 0))    

    for tile in state.tiles:
        tile.render(screen, state.camera)

    for obstacle in state.obstacles:
        obstacle.render(screen, state.camera)

    for projectile in state.player.projectiles:
        projectile.render(screen, state.camera)

    for enemy in state.enemies:
        enemy.render(screen, state.camera)

    state.player.render(screen, state.camera)
    state.player.render_hud(screen, state.camera)

    draw_mouse_crosshair(screen, state.camera)
    render_remote_players(state)

    if state.show_menu:
        state.menu.draw(screen)

    render_minimap(state)

    if DEBUG:
        # FPS
        fps = state.clock.get_fps()
        fps_text = state.font.render(f"FPS: {int(fps)}", True, (255, 255, 255))
        pygame.draw.rect(screen, (0, 0, 0), (8, 8, 130, 22))
        screen.blit(fps_text, (10, 10))

        # Modo
        mode_text = state.font.render(f"Modo: {state.mode}", True, (255, 255, 255))
        pygame.draw.rect(screen, (0, 0, 0), (8, 30, 130, 22))
        screen.blit(mode_text, (10, 32))

        # Posición del jugador
        player_x = round(state.player.x)
        player_y = round(state.player.y)
        pos_text = state.font.render(f"Pos: ({player_x}, {player_y})", True, (255, 255, 255))
        pygame.draw.rect(screen, (0, 0, 0), (8, 52, 180, 22))
        screen.blit(pos_text, (10, 54))

        # Posición del mouse
        mouse_x, mouse_y = pygame.mouse.get_pos()
        world_x = round(mouse_x / state.camera.zoom + state.camera.offset_x)
        world_y = round(mouse_y / state.camera.zoom + state.camera.offset_y)
        mouse_text = state.font.render(f"Mouse: ({world_x}, {world_y})", True, (255, 255, 255))
        pygame.draw.rect(screen, (0, 0, 0), (8, 74, 200, 22))
        screen.blit(mouse_text, (10, 76))

        # Coordenadas del tile
        tile_col = int(world_x // 64)  # TILE_SIZE = 64
        tile_row = int(world_y // 64)
        tile_text = "?"

        # Buscar el tipo de tile correspondiente
        for tile in state.tiles:
            if tile.rect.collidepoint(world_x, world_y):
                tile_text = tile.tile_type
                break

        # Mostrar info del tile
        tile_info = state.font.render(
            f"Tile: ({tile_col}, {tile_row}) Tipo: '{tile_text}'", True, (255, 255, 255)
        )
        pygame.draw.rect(screen, (0, 0, 0), (8, 96, 250, 22))
        screen.blit(tile_info, (10, 98))

    pygame.display.flip()
