import sys
import os
import time
from contextlib import nullcontext
sys.path.append(os.path.abspath(os.path.join(os.path.dirname(__file__), "..")))

import pygame
from roguelike_project.core.game.game import Game
from roguelike_project.config import DEBUG, FPS
from roguelike_project.utils.performance_tracker import PerformanceTracker

def main():
    pygame.init()
    screen = pygame.display.set_mode((1200, 800), pygame.HWSURFACE | pygame.DOUBLEBUF)
    pygame.display.set_caption("Roguelike")
    
    if DEBUG:
        pygame.mouse.set_visible(True)

    game = Game(screen)
    
    # Safety check
    if not hasattr(game, 'state'):
        raise RuntimeError("Game state not initialized properly!")
    
    with PerformanceTracker() if DEBUG else nullcontext() as tracker:
        while game.state.running:
            with tracker.time_section('handle_events') if DEBUG else nullcontext():
                game.handle_events()
            
            with tracker.time_section('update') if DEBUG else nullcontext():
                game.update()
            
            with tracker.time_section('render') if DEBUG else nullcontext():
                game.render()
            
            game.state.clock.tick(FPS)
            if DEBUG:
                tracker.track_frame()

    pygame.quit()

if __name__ == "__main__":
    main()
