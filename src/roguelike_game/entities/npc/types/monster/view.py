# Path: src/roguelike_game/entities/npc/types/monster/view.py

import pygame
from roguelike_engine.utils.loader import load_image
from roguelike_game.entities.npc.base.view import NPCView
import roguelike_engine.config as config

class MonsterView(NPCView):
    SPRITE_SIZE = (256, 256)

    def __init__(self, model, sprite_paths: dict[str,str], sprite_size: tuple[int,int]):
        super().__init__(model, sprite_paths, sprite_size)
        # Cargar sprites usando las rutas pasadas por YAML
        for dir_, path in sprite_paths.items():
            self.sprites[dir_] = load_image(path, sprite_size)

    def render(self, screen, camera):
        m = self.model
        if not m.alive:
            return
        # salto de visibilidad omitido por brevedad...
        # elegir sprite según direction
        dx, dy = m.direction
        if abs(dx) > abs(dy):
            spr = self.sprites["right"] if dx > 0 else self.sprites["left"]
        else:
            spr = self.sprites["down"]  if dy > 0 else self.sprites["up"]
        # dibujar sprite
        scaled = pygame.transform.scale(spr, camera.scale(self.sprite_size))
        screen.blit(scaled, camera.apply((m.x, m.y)))
        # render health bar:
        self._render_health_bar(screen, camera)
        # debug outline
        if config.DEBUG:
            # Dibuja la máscara del sprite (rojo)
            self.mask = pygame.mask.from_surface(spr)
            outline = self.mask.outline()
            pts = [camera.apply((m.x + x, m.y + y)) for x,y in outline]
            if len(pts) >= 3:
                pygame.draw.polygon(screen, (255,0,0), pts, 1)
            # Dibuja el hitbox de pies (verde)
            # Usar el hitbox real de pies (igual que colisión)
            rect = m.movement.hitbox()
            topleft = camera.apply(rect.topleft)
            size = camera.scale((rect.width, rect.height))
            scaled_rect = pygame.Rect(topleft, size)
            pygame.draw.rect(screen, (0,255,0), scaled_rect, 2)
