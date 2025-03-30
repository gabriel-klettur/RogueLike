import sys
import os

# Agregar la carpeta 'roguelike_project' al sys.path
base_path = os.path.abspath(os.path.dirname(__file__))
sys.path.insert(0, os.path.join(base_path, 'roguelike_project'))

# Lanzar el juego real
from main import main

if __name__ == "__main__":
    main()