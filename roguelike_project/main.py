import pygame
from core.game import Game

def main():
    pygame.init()                                               # Inicializa Pygame
    screen = pygame.display.set_mode((1200, 800))                # Crea la ventana del juego
    pygame.display.set_caption("Roguelike")                     # Establece el título de la ventana
    clock = pygame.time.Clock()                                 # Crea un reloj para controlar la velocidad de fotogramas

    game = Game(screen)                                         # Crea una instancia del juego

    while game.running:                                         # Bucle principal del juego 
        game.handle_events()                                    # Maneja los eventos (teclado, ratón, etc.)
        game.update()                                           # Actualiza la lógica del juego (movimiento, colisiones, etc.)              
        game.render()                                           # Renderiza la pantalla (dibuja los objetos en la ventana)
        game.clock.tick(60)                                          # Controla la velocidad de fotogramas (60 FPS)                    

    pygame.quit()

if __name__ == "__main__":
    main()
