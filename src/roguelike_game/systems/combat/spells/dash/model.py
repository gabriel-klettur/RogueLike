# Path: src/roguelike_game/systems/combat/spells/dash/model.py
import pygame
import time
import math
import random

from roguelike_game.systems.particles.particle import Particle

class DashModel:
    """
    Modelo MVC para el dash: mueve al jugador y emite partículas durante la duración.
    """
    def __init__(self, player, direction, dash_speed=2000, duration=0.2, emit_rate=0.01):
        self.player = player
        self.direction = direction.normalize() if isinstance(direction, pygame.Vector2) and direction.length() else pygame.Vector2(0, 0)
        self.dash_speed = dash_speed
        self.duration = duration
        self.emit_rate = emit_rate
        self.start_time = time.time()
        self.last_emit = 0.0
        self.particles: list[Particle] = []
        self.active = True

    def elapsed(self):
        return time.time() - self.start_time

    def is_finished(self) -> bool:
        # Concluye cuando ha pasado la duración y no quedan partículas vivas
        return self.elapsed() >= self.duration and not self.particles

    def update(self):
        now = self.elapsed()
        delta_time = self.player.state.clock.get_time() / 1000
        # Movimiento de dash mientras está activo
        if now < self.duration:
            move_dist = self.dash_speed * delta_time
            self.player.x += self.direction.x * move_dist
            self.player.y += self.direction.y * move_dist
        else:
            self.active = False
        # Emisión de partículas
        if self.active and now - self.last_emit >= self.emit_rate:
            self._emit_particle()
            self.last_emit = now
        # Actualizar cada partícula
        for p in self.particles:
            p.update()
        # Filtrar las muertas
        self.particles = [p for p in self.particles if p.age < p.lifespan]

    def _emit_particle(self):
        px = self.player.x + self.player.sprite_size[0] / 2
        py = self.player.y + self.player.sprite_size[1]
        base_angle = math.degrees(math.atan2(self.direction.y, self.direction.x))
        for _ in range(2):
            angle = math.radians(base_angle + 180 + random.uniform(-30, 30))
            p = Particle(
                px + random.uniform(-5, 5),
                py + random.uniform(-5, 5),
                angle,
                random.uniform(1, 3),
                random.choice([(200,200,255),(150,150,255),(255,255,255)]),
                random.randint(3,6),
                lifespan=15
            )
            self.particles.append(p)