# Path: src/roguelike_game/entities/npc/views/elite_view.py
import pygame
from src.roguelike_engine.utils.loader import load_image
import src.roguelike_engine.config as config
from src.roguelike_game.entities.npc.interfaces import IView
from src.roguelike_game.entities.npc.models.elite_model import EliteModel

class EliteView(IView):
    SPRITE_SIZE = (256, 256)
    BASE_PATH = "assets/npc/monsters/barbol_elite/elite_barbol_1"

    def __init__(self, model: EliteModel):
        self.model = model
        base = EliteView.BASE_PATH
        sz = EliteView.SPRITE_SIZE
        self.sprites = {
            "up":    load_image(f"{base}_top.png",    sz),
            "down":  load_image(f"{base}_down.png",  sz),
            "left":  load_image(f"{base}_left.png",  sz),
            "right": load_image(f"{base}_right.png", sz),
        }
        self.mask = None

    def render(self, screen, camera):
        e = self.model
        if not e.alive:
            return
        if not camera.is_in_view(e.x, e.y, EliteView.SPRITE_SIZE):
            return
        dx, dy = e.direction
        if abs(dx) > abs(dy):
            sprite = self.sprites["right"] if dx > 0 else self.sprites["left"]
        else:
            sprite = self.sprites["down"] if dy > 0 else self.sprites["up"]
        self.mask = pygame.mask.from_surface(sprite)
        scaled = pygame.transform.scale(sprite, camera.scale(EliteView.SPRITE_SIZE))
        screen.blit(scaled, camera.apply((e.x, e.y)))
        # Salud y debug idéntico a MonsterView
        # Podrías extraer método común si deseas
        # Render health bar:
        x, y = camera.apply((e.x + 18, e.y - 20))
        bar_w = int(EliteView.SPRITE_SIZE[0] * camera.zoom * 0.9)
        bar_h = int(20 * camera.zoom)
        pygame.draw.rect(screen, (40,40,40), (x, y, bar_w, bar_h))
        fill = int(bar_w * (e.health / e.max_health))
        pygame.draw.rect(screen, (0,255,0), (x, y, fill, bar_h))
        pygame.draw.rect(screen, (0,0,0), (x, y, bar_w, bar_h), 1)
        font = pygame.font.SysFont('Arial', int(18 * camera.zoom))
        surf = font.render(f"{e.health}/{e.max_health}", True, (255,0,0))
        rect = surf.get_rect(center=(x+bar_w//2, y+bar_h//2))
        if fill > rect.width * 0.8:
            screen.blit(surf, rect)
        else:
            rect.left = x + bar_w + 2
            screen.blit(surf, rect)
        if config.DEBUG and self.mask:
            outline = self.mask.outline()
            pts = [camera.apply((e.x + px, e.y + py)) for px, py in outline]
            if len(pts) >= 3:
                pygame.draw.polygon(screen, (255,0,0), pts, 1)