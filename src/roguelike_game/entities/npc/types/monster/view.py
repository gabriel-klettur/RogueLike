# Path: src/roguelike_game/entities/npc/types/monster/view.py

import pygame
from src.roguelike_engine.utils.loader import load_image
import src.roguelike_engine.config as config
from src.roguelike_game.entities.npc.interfaces import IView

class MonsterView(IView):
    """
    Sprite dinámico según config.yaml.
    Recibe sprite_paths={'up':..., 'down':...,…} y opcional sprite_size.
    """
    def __init__(self, model, sprite_paths: dict[str,str], sprite_size: tuple[int,int]=None):
        self.model = model
        self.sprite_size = sprite_size
        self.sprites = {}
        for dir_, rel in sprite_paths.items():
            sz = sprite_size or None
            self.sprites[dir_] = load_image(rel, sz)
        self.mask = None

    def render(self, screen, camera):
        m = self.model
        if not m.alive:
            return
        size = self.sprite_size or (0,0)
        if not camera.is_in_view(m.x, m.y, size):
            return

        dx, dy = m.direction
        if "all" in self.sprites:
            sprite = self.sprites["all"]
        else:
            if abs(dx) > abs(dy):
                sprite = self.sprites["right"] if dx > 0 else self.sprites["left"]
            else:
                sprite = self.sprites["down"] if dy > 0 else self.sprites["up"]

        self.mask = pygame.mask.from_surface(sprite)
        w, h = self.sprite_size or sprite.get_size()
        scaled = pygame.transform.scale(sprite, camera.scale((w,h)))
        screen.blit(scaled, camera.apply((m.x, m.y)))

        # health-bar genérica
        self._render_health_bar(screen, camera)

        if config.DEBUG and self.mask:
            pts = self.mask.outline()
            pts = [camera.apply((m.x + x, m.y + y)) for x,y in pts]
            if len(pts) >= 3:
                pygame.draw.polygon(screen, (255,0,0), pts, 1)

    def _render_health_bar(self, screen, camera):
        m = self.model
        x, y = camera.apply((m.x + 18, m.y - 20))
        w = int((self.sprite_size or (0,0))[0] * camera.zoom * 0.9)
        h = int(20 * camera.zoom)
        pygame.draw.rect(screen, (40,40,40), (x,y,w,h))
        fill = int(w * (m.health / m.max_health))
        pygame.draw.rect(screen, (0,255,0), (x,y,fill,h))
        pygame.draw.rect(screen, (0,0,0), (x,y,w,h), 1)
        font = pygame.font.SysFont('Arial', int(18 * camera.zoom))
        surf = font.render(f"{m.health}/{m.max_health}", True, (255,0,0))
        rect = surf.get_rect(center=(x + w//2, y + h//2))
        if fill > rect.width * 0.8:
            screen.blit(surf, rect)
        else:
            rect.left = x + w + 2
            screen.blit(surf, rect)
