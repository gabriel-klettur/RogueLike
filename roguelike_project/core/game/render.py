import pygame

def render_game(state):
    screen = state.screen
    screen.fill((0, 0, 0))
    screen.blit(state.background, state.camera.apply((0, 0)))

    for obstacle in state.obstacles:
        obstacle.render(screen, state.camera)

    state.player.render(screen, state.camera)
    state.player.render_hud(screen, state.camera)

    if state.show_menu:
        state.menu.draw(screen)

    # Mostrar FPS
    fps = state.clock.get_fps()
    fps_text = state.font.render(f"FPS: {int(fps)}", True, (255, 255, 255))
    pygame.draw.rect(screen, (0, 0, 0), (8, 8, 60, 22))
    screen.blit(fps_text, (10, 10))

    
    # Mostrar modo de juego
    mode_text = state.font.render(f"Modo: {state.mode}", True, (255, 255, 255))
    pygame.draw.rect(screen, (0, 0, 0), (8, 30, 130, 22))
    screen.blit(mode_text, (10, 32))

    pygame.display.flip()
