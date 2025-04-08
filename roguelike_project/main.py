import sys
import os
import time
import pygame

# Agregar el path del proyecto
sys.path.append(os.path.abspath(os.path.join(os.path.dirname(__file__), "..")))

from roguelike_project.engine.game.game import Game
from roguelike_project.config import DEBUG, FPS
from roguelike_project.utils.debug_overlay import render_debug_overlay  # NUEVO

# --- Debug Tools ---
def init_debug():
    pygame.mouse.set_visible(True)
    return {
        'handle_events': [],
        'update': [],
        'render': [],
        'frame_times': []
    }

def track_performance(perf_log, key, start_time):
    perf_log[key].append(time.perf_counter() - start_time)

# --- Main Loop ---
def main():
    pygame.init()
    screen = pygame.display.set_mode((1200, 800), pygame.HWSURFACE | pygame.DOUBLEBUF)
    pygame.display.set_caption("Roguelike")

    performance_log = init_debug() if DEBUG else None
    sample_size = 60

    game = Game(screen)

    if not hasattr(game, 'state'):
        raise RuntimeError("Game state not initialized properly!")

    while game.state.running:
        frame_start = time.perf_counter()

        # Eventos
        t = time.perf_counter()
        game.handle_events()
        if DEBUG: track_performance(performance_log, 'handle_events', t)

        # Update
        t = time.perf_counter()
        game.update()
        if DEBUG: track_performance(performance_log, 'update', t)

        # Render
        t = time.perf_counter()
        game.render()
        if DEBUG: track_performance(performance_log, 'render', t)

        # Mostrar overlay de debug (después del render principal)
        if DEBUG:
            track_performance(performance_log, 'frame_times', frame_start)
            render_debug_overlay(screen, performance_log)

        # Actualizar pantalla completa
        pygame.display.flip()
        game.state.clock.tick(FPS)

    pygame.quit()

if __name__ == "__main__":
    main()
