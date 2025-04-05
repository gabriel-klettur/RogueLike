import sys
import os
import time
import pygame

# Agregar el path del proyecto
sys.path.append(os.path.abspath(os.path.join(os.path.dirname(__file__), "..")))

from roguelike_project.core.game.game import Game
from roguelike_project.config import DEBUG, FPS

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

def print_debug_stats(perf_log, sample_size):
    avg_frame = sum(perf_log['frame_times']) / sample_size
    avg_fps = 1 / avg_frame if avg_frame > 0 else 0
    print(f"\n🎯 Performance (últimos {sample_size} frames):")
    print(f"  FPS: {avg_fps:.1f} (Objetivo: {FPS})")
    print(f"  Eventos: {sum(perf_log['handle_events'])/sample_size:.4f}s/frame")
    print(f"  Update : {sum(perf_log['update'])/sample_size:.4f}s/frame")
    print(f"  Render : {sum(perf_log['render'])/sample_size:.4f}s/frame")

    for key in perf_log:
        perf_log[key] = []

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

        game.state.clock.tick(FPS)

        # FPS tracking
        if DEBUG:
            track_performance(performance_log, 'frame_times', frame_start)
            if len(performance_log['frame_times']) >= sample_size:
                print_debug_stats(performance_log, sample_size)

    pygame.quit()

if __name__ == "__main__":
    main()
