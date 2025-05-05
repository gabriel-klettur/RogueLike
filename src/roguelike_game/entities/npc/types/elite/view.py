# Path: src/roguelike_game/entities/npc/types/elite/view.py

import pygame
from src.roguelike_engine.utils.loader import load_image
from src.roguelike_game.entities.npc.base.view import NPCView
import src.roguelike_engine.config as config

class EliteView(NPCView):
    SPRITE_SIZE = (512, 512)

    def __init__(self, model, sprite_paths: dict[str,str], sprite_size: tuple[int,int]):
        super().__init__(model, sprite_paths, sprite_size)
        for dir_, path in sprite_paths.items():
            self.sprites[dir_] = load_image(path, sprite_size)

    def render(self, screen, camera):
        e = self.model
        if not e.alive:
            return
        dx, dy = e.direction
        if abs(dx) > abs(dy):
            spr = self.sprites["right"] if dx > 0 else self.sprites["left"]
        else:
            spr = self.sprites["down"]  if dy > 0 else self.sprites["up"]
        scaled = pygame.transform.scale(spr, camera.scale(self.sprite_size))
        screen.blit(scaled, camera.apply((e.x, e.y)))
        # health bar y debug idénticos a MonsterView
        self._render_health_bar(screen, camera)
        if config.DEBUG:
            self.mask = pygame.mask.from_surface(spr)
            outline = self.mask.outline()
            pts = [camera.apply((e.x + x, e.y + y)) for x,y in outline]
            if len(pts) >= 3:
                pygame.draw.polygon(screen, (255,0,0), pts, 1)
