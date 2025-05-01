# Path: src/roguelike_game/systems/combat/spells/teleport/view.py
import pygame
import time
from pygame import Surface
from src.roguelike_game.systems.combat.spells.teleport.model import TeleportModel

class TeleportView:
    def __init__(self, model: TeleportModel):
        self.model = model

    def render(self, screen, camera):
        # si ya acabó, nada que hacer
        if self.model.is_finished():
            return

        elapsed = time.time() - self.model.start_time
        total = self.model.lifespan
        # factor 0→1 en cada fase
        t = min(1.0, elapsed / total)

        # calculamos radio del anillo: de 0 a  max_radius
        max_radius = 60
        radius = int(max_radius * t)

        # atenuación del anillo
        alpha = int(255 * (1 - t))

        # superficie transparente para dibujar el anillo
        surf = Surface(screen.get_size(), pygame.SRCALPHA)
        col  = (0, 200, 255, alpha)

        # definimos centro según fase
        center_world = (self.model.start_pos if self.model.phase == "out" else self.model.end_pos)
        center_px = camera.apply(center_world)

        # dibujamos anillo (grosor 4px)
        pygame.draw.circle(surf, col, center_px, radius, width=4)

        # opcional: fundimos el sprite del jugador
        #   — si quieres que desaparezca durante “out”, y reaparezca en “in”:
        #   podrías ajustar el alpha del sprite aquí.

        screen.blit(surf, (0,0))