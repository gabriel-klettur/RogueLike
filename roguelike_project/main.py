import sys
import os
import time
sys.path.append(os.path.abspath(os.path.join(os.path.dirname(__file__), "..")))

import pygame
from roguelike_project.core.game.game import Game
from roguelike_project.config import DEBUG

from roguelike_project.config import FPS

def main():
    pygame.init()                                               # Inicializa Pygame
    screen = pygame.display.set_mode((1200, 800), pygame.HWSURFACE | pygame.DOUBLEBUF)               # Crea la ventana del juego
    pygame.display.set_caption("Roguelike")                     # Establece el título de la ventana    

    if DEBUG:                                                   # Si estamos en modo debug
        pygame.mouse.set_visible(True)                         # Oculta el cursor del ratón
        performance_log = {
            'handle_events': [],
            'update': [],
            'render': [],
            'frame_times': []
        }
        sample_size = 60  # Track last 60 frames (1 second at 60FPS)       


    
    game = Game(screen)                                         # Crea una instancia del juego

    # Safety check
    if not hasattr(game, 'state'):
        raise RuntimeError("Game state not initialized properly!")
    
    while game.state.running:                                         # Bucle principal del juego 
        frame_start = time.perf_counter()
        event_start = time.perf_counter()
        game.handle_events()                                    # Maneja los eventos (teclado, ratón, etc.)
        if DEBUG:
            event_time = time.perf_counter() - event_start
            performance_log['handle_events'].append(event_time)

        update_start = time.perf_counter()
        game.update()                                           # Actualiza la lógica del juego (movimiento, colisiones, etc.)
        if DEBUG:
            update_time = time.perf_counter() - update_start
            performance_log['update'].append(update_time)      

        render_start = time.perf_counter()
        game.render()                                           # Renderiza la pantalla (dibuja los objetos en la ventana)

        if DEBUG:
            render_time = time.perf_counter() - render_start
            performance_log['render'].append(render_time)

        game.state.clock.tick(FPS)                                          # Controla la velocidad de fotogramas (60 FPS)            
        if DEBUG:
            frame_time = time.perf_counter() - frame_start
            performance_log['frame_times'].append(frame_time)
            
            # Print performance stats every second
            if len(performance_log['frame_times']) >= sample_size:
                avg_frame = sum(performance_log['frame_times']) / sample_size
                avg_fps = 1 / avg_frame if avg_frame > 0 else 0
                print(f"\nPerformance (Last {sample_size} frames):")
                print(f"  FPS: {avg_fps:.1f} (Target: {FPS})")
                print(f"  Events: {sum(performance_log['handle_events'])/sample_size:.4f}s/frame")
                print(f"  Update: {sum(performance_log['update'])/sample_size:.4f}s/frame")
                print(f"  Render: {sum(performance_log['render'])/sample_size:.4f}s/frame")
                
                # Reset tracking
                for key in performance_log:
                    performance_log[key] = []

    pygame.quit()

if __name__ == "__main__":
    main()
