# Path: src/roguelike_game/main.py

import pygame
from collections import defaultdict

from roguelike_game.game.game import Game
from roguelike_engine.config import FPS, SCREEN_WIDTH, SCREEN_HEIGHT
from roguelike_engine.utils.benchmark import benchmark

def init_debug():
    pygame.mouse.set_visible(True)
    return defaultdict(list)

def main():
    pygame.init()
    screen = pygame.display.set_mode(
        (SCREEN_WIDTH, SCREEN_HEIGHT),
        pygame.HWSURFACE | pygame.DOUBLEBUF
    )
    pygame.display.set_caption("Roguelike")

    # -------- Inicializar performance_log --------
    performance_log = init_debug()

    # Creamos el juego pasándole el log
    game = Game(screen, perf_log=performance_log)
    if not hasattr(game, 'state'):
        raise RuntimeError("Game state not initialized properly!")

    # —— Envuelve SIEMPRE los métodos clave, para que las métricas
    #    sigan funcionando aunque DEBUG cambie más tarde ——
    game.handle_events = benchmark(performance_log, "1.handle_events")(game.handle_events)
    game.update        = benchmark(performance_log, "2.update")(game.update)
    game.render        = benchmark(performance_log, "3.total_render")(game.render)

    # Bucle principal
    while game.state.running:
        game.handle_events()
        game.update()

        # Renderizamos todo (tilemap, HUD, debug overlay, etc.)
        game.render(performance_log)

        # 👉 Actualizamos el título de la ventana con los FPS reales
        fps = game.clock.get_fps()
        pygame.display.set_caption(f"Roguelike - FPS: {fps:0.1f}")

        pygame.display.flip()
        game.clock.tick(FPS)

    pygame.quit()

if __name__ == "__main__":
    main()
