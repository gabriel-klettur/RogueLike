import sys
import os
import pygame

# Inicializar pantalla para que convert_alpha() funcione
pygame.init()
pygame.display.set_mode((1, 1))

# Asegurar que se puede importar desde la raíz
sys.path.append(os.path.abspath(os.path.join(os.path.dirname(__file__), '..')))

from roguelike_project.engine.game.systems.map_builder import build_map

def generate_bulk_maps(n=100):
    for i in range(n):
        print(f"🔁 Generando mapa {i + 1} de {n}")
        build_map()

if __name__ == "__main__":
    generate_bulk_maps()
