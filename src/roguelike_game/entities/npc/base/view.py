#Path: src/roguelike_game/entities/npc/base/view.py

import pygame
from src.roguelike_game.entities.npc.interfaces import IView
from src.roguelike_engine.utils.loader import load_image

class BaseNPCView(IView):
    """
    Renderiza un NPC a partir de un dict de rutas de sprite.
    """
    def __init__(self, model, sprite_paths: dict[str,str], size: tuple[int,int]):
        self.model = model
        # load_image ya busca en ASSETS_DIR
        self.sprites = {
            name: load_image(path, size)
            for name, path in sprite_paths.items()
        }
        self.mask = None

    def render(self, screen, camera):
        m = self.model
        if hasattr(m, "alive") and not m.alive:
            return
        # determina key de sprite: 'all' o según m.direction
        key = "all"
        if hasattr(m, "direction") and m.direction in self.sprites:
            dir_x, dir_y = m.direction
            if abs(dir_x) > abs(dir_y):
                key = "right" if dir_x > 0 else "left"
            else:
                key = "down" if dir_y > 0 else "up"
        sprite = self.sprites.get(key) or next(iter(self.sprites.values()))
        self.mask = pygame.mask.from_surface(sprite)
        scaled = pygame.transform.scale(sprite, camera.scale(sprite.get_size()))
        screen.blit(scaled, camera.apply((m.x, m.y)))
        # (añade aquí health-bar genérica si quieres)
