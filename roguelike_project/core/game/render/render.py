# core/game/render/render.py

import pygame
from roguelike_project.core.game.multiplayer.multiplayer import render_remote_players
from roguelike_project.core.game.render.minimap import render_minimap
from roguelike_project.utils.mouse import draw_mouse_crosshair

def render_game(state):
    screen = state.screen
    screen.fill((0, 0, 0))    

    for tile in state.tiles:
        tile.render(screen, state.camera)

    for obstacle in state.obstacles:
        obstacle.render(screen, state.camera)

    state.player.render(screen, state.camera)
    state.player.render_hud(screen, state.camera)

    draw_mouse_crosshair(screen, state.camera)

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

    render_minimap(state)  # ✅ Minimapa ahora importado desde archivo separado

    pygame.display.flip()
