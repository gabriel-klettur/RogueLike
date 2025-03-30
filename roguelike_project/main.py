import pygame
from core.game import Game

def main():
    pygame.init()                                               # Inicializa Pygame
    screen = pygame.display.set_mode((1200, 800))                # Crea la ventana del juego
    pygame.display.set_caption("Roguelike")                     # Establece el título de la ventana    

    game = Game(screen)                                         # Crea una instancia del juego

    while game.state.running:                                         # Bucle principal del juego 
        game.handle_events()                                    # Maneja los eventos (teclado, ratón, etc.)
        game.update()                                           # Actualiza la lógica del juego (movimiento, colisiones, etc.)              
        game.render()                                           # Renderiza la pantalla (dibuja los objetos en la ventana)
        game.state.clock.tick(60)                                          # Controla la velocidad de fotogramas (60 FPS)                    

    pygame.quit()

if __name__ == "__main__":
    main()
