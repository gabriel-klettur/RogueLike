# Path: src/roguelike_game/entities/npc/views/monster_view.py
import pygame
from src.roguelike_engine.utils.loader import load_image
import src.roguelike_engine.config as config
from src.roguelike_game.entities.npc.interfaces import IView

class MonsterView(IView):
    SPRITE_SIZE = (256, 256)
    SPRITE_PATH = "assets/npc/monsters/barbol/barbol_1"

    def __init__(self, model):
        self.model = model
        # Cargar sprites una sola vez
        base = MonsterView.SPRITE_PATH
        sz = MonsterView.SPRITE_SIZE
        self.sprites = {
            "up":    load_image(f"{base}_top.png",    sz),
            "down":  load_image(f"{base}_down.png",  sz),
            "left":  load_image(f"{base}_left.png",  sz),
            "right": load_image(f"{base}_right.png", sz),
        }
        self.mask = None

    def render(self, screen, camera):
        m = self.model
        if not m.alive:
            return
        # Si no está en vista, saltar
        if not camera.is_in_view(m.x, m.y, MonsterView.SPRITE_SIZE):
            return
        # Seleccionar sprite según direction
        dx, dy = m.direction
        if abs(dx) > abs(dy):
            sprite = self.sprites["right"] if dx > 0 else self.sprites["left"]
        else:
            sprite = self.sprites["down"] if dy > 0 else self.sprites["up"]
        # Actualizar máscara
        self.mask = pygame.mask.from_surface(sprite)
        # Dibujar sprite escalado
        scaled = pygame.transform.scale(sprite, camera.scale(MonsterView.SPRITE_SIZE))
        screen.blit(scaled, camera.apply((m.x, m.y)))
        # Barra de salud
        self._render_health_bar(screen, camera)
        # Debug
        if config.DEBUG and self.mask:
            outline = self.mask.outline()
            pts = [camera.apply((m.x + x, m.y + y)) for x, y in outline]
            if len(pts) >= 3:
                pygame.draw.polygon(screen, (255,0,0), pts, 1)

    def _render_health_bar(self, screen, camera):
        m = self.model
        x, y = camera.apply((m.x + 18, m.y - 20))
        bar_w = int(MonsterView.SPRITE_SIZE[0] * camera.zoom * 0.9)
        bar_h = int(20 * camera.zoom)
        # Fondo
        pygame.draw.rect(screen, (40,40,40), (x, y, bar_w, bar_h))
        fill = int(bar_w * (m.health / m.max_health))
        pygame.draw.rect(screen, (0,255,0), (x, y, fill, bar_h))
        pygame.draw.rect(screen, (0,0,0), (x, y, bar_w, bar_h), 1)
        # Texto
        text = f"{m.health}/{m.max_health}"
        font = pygame.font.SysFont('Arial', int(18 * camera.zoom))
        surf = font.render(text, True, (255,0,0))
        rect = surf.get_rect(center=(x + bar_w//2, y + bar_h//2))
        if fill > rect.width * 0.8:
            screen.blit(surf, rect)
        else:
            rect.left = x + bar_w + 2
            screen.blit(surf, rect)