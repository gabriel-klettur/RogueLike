#!/usr/bin/env python3
"""
Tool para generar sprites animados a partir de sistemas de partículas de hechizos.
"""
import os
import sys
import time
import pygame
import inspect
from importlib import import_module

# Configuración de generación de sprites
DEFAULT_NUM_FRAMES = 16  # Número de frames para cada animación
default_SEED = 0        # Semilla para generador aleatorio
DEFAULT_MAX_DURATION = 10.0  # Duración máxima de animación en segundos

# Headless mode
os.environ['SDL_VIDEODRIVER'] = 'dummy'
pygame.init()

# Paths
ROOT = os.path.dirname(os.path.dirname(__file__))  # proyecto raíz
tmp = os.path.dirname(__file__)
SRC_PATH = os.path.abspath(os.path.join(ROOT, 'src'))
sys.path.insert(0, SRC_PATH)

SPELLS_DIR = os.path.join(SRC_PATH, 'roguelike_game', 'ecs', 'systems', 'rendering', 'combat', 'spells')
OUTPUT_DIR = os.path.join(ROOT, 'assets', 'particles_sprites')
os.makedirs(OUTPUT_DIR, exist_ok=True)

class DummyCamera:
    def is_in_view(self, x, y, size):
        return True
    def apply(self, pos):
        return (int(pos[0]), int(pos[1]))


def camel_case(snake: str) -> str:
    return ''.join(word.title() for word in snake.split('_'))


def generate_sprites(spell_name: str, num_frames: int = DEFAULT_NUM_FRAMES, max_duration: float = DEFAULT_MAX_DURATION, seed: int = default_SEED):
    model_module = f'roguelike_game.ecs.systems.rendering.combat.spells.{spell_name}.model'
    view_module = f'roguelike_game.ecs.systems.rendering.combat.spells.{spell_name}.view'
    try:
        mmod = import_module(model_module)
        vmod = import_module(view_module)
    except ModuleNotFoundError:
        print(f"Skipping {spell_name}: módulos no encontrados.")
        return
    ModelClass = getattr(mmod, camel_case(spell_name) + 'Model')
    ViewClass = getattr(vmod, camel_case(spell_name) + 'View')
    # Instanciar modelo con valores por defecto
    sig = inspect.signature(ModelClass.__init__)
    width = int(getattr(ModelClass.__init__, 'width', None) or 256)
    height = int(getattr(ModelClass.__init__, 'height', None) or 256)
    # Construir kwargs basados en nombre de parámetro
    params = list(sig.parameters.values())[1:]  # omitir self
    x0, y0 = width // 2, height // 2
    kwargs = {}
    for param in params:
        name = param.name
        if name == 'x':
            kwargs[name] = x0
        elif name == 'y':
            kwargs[name] = y0
        elif name.endswith('_pos'):
            kwargs[name] = (x0, y0)
        elif name == 'direction':
            kwargs[name] = pygame.math.Vector2(0, -1)
        elif name == 'count':
            kwargs[name] = num_frames
        elif name == 'width':
            kwargs[name] = width
        elif name == 'height':
            kwargs[name] = height
        elif name in ('max_duration', 'duration', 'lifespan'):
            kwargs[name] = max_duration
        elif name == 'seed':
            kwargs[name] = seed
        elif param.default is inspect._empty:
            print(f"Warning: parámetro inesperado {name} para {spell_name}, usando semilla")
            kwargs[name] = seed
        # else: parámetro opcional con valor por defecto
    model = ModelClass(**kwargs)
    view = ViewClass(model)
    surf = pygame.Surface((width, height), pygame.SRCALPHA)
    cam = DummyCamera()

    print(f"Generando {spell_name} ({num_frames} frames)")
    has_update = hasattr(model, 'update')
    for i in range(num_frames):
        if has_update:
            try:
                model.update()
            except Exception:
                pass
        surf.fill((0,0,0,0))
        view.render(surf, cam)
        filename = f"{spell_name}_{i}.png"
        path = os.path.join(OUTPUT_DIR, filename)
        pygame.image.save(surf, path)
        print(f" Guardado: {path}")


def main():
    spells = [d for d in os.listdir(SPELLS_DIR)
              if os.path.isdir(os.path.join(SPELLS_DIR, d))]
    for spell in spells:
        generate_sprites(spell)


if __name__ == '__main__':
    main()
