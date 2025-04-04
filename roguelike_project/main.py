import sys
import os
sys.path.append(os.path.abspath(os.path.join(os.path.dirname(__file__), "..")))

import pygame
from roguelike_project.core.game.game import Game
from roguelike_project.config import DEBUG

from roguelike_project.config import FPS

def main():
    pygame.init()                                               # Inicializa Pygame
    screen = pygame.display.set_mode((1200, 800))               # Crea la ventana del juego
    pygame.display.set_caption("Roguelike")                     # Establece el título de la ventana    

    if DEBUG:                                                   # Si estamos en modo debug
        pygame.mouse.set_visible(True)                         # Oculta el cursor del ratón

    game = Game(screen)                                         # Crea una instancia del juego

    while game.state.running:                                         # Bucle principal del juego 
        game.handle_events()                                    # Maneja los eventos (teclado, ratón, etc.)
        game.update()                                           # Actualiza la lógica del juego (movimiento, colisiones, etc.)              
        game.render()                                           # Renderiza la pantalla (dibuja los objetos en la ventana)
        game.state.clock.tick(FPS)                                          # Controla la velocidad de fotogramas (60 FPS)                    

    pygame.quit()

if __name__ == "__main__":
    main()
