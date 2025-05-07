# Path: src/roguelike_game/systems/combat/spells/sphere_magic_shield/view.py
import pygame
from pygame import Rect

from roguelike_game.systems.combat.spells.sphere_magic_shield.model import SphereMagicShieldModel

class SphereMagicShieldView:
    """
    Dibuja un círculo semitransparente alrededor del jugador
    cuyo radio varía según el controlador.
    """
    def __init__(self, model: SphereMagicShieldModel):
        self.model = model

    def render(self, screen, camera):
        # Si ya acabó, no dibujamos nada
        if self.model.is_finished():
            return None

        # Centro del jugador en mundo
        px = self.model.player.x + self.model.player.sprite_size[0] // 2
        py = self.model.player.y + self.model.player.sprite_size[1] // 2
        radius = self.model.radius

        # Creamos una superficie con canal alfa
        surf = pygame.Surface((radius * 2, radius * 2), pygame.SRCALPHA)
        # El escudo va desvaneciéndose al final
        alpha = int(150 * (1 - self.model.elapsed() / self.model.duration))
        pygame.draw.circle(
            surf,
            (*self.model.color, alpha),
            (radius, radius),
            radius,
            width=4
        )

        # Calculamos la esquina superior izquierda en mundo
        world_tl = (px - radius, py - radius)
        # Convertimos a coordenadas de pantalla
        screen_tl = camera.apply(world_tl)

        # Bliteamos y devolvemos el rect sucio
        screen.blit(surf, screen_tl)
        return Rect(screen_tl, (radius * 2, radius * 2))