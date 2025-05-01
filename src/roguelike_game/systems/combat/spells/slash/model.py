# Path: src/roguelike_game/systems/combat/spells/slash/model.py
import math
import pygame

class SlashModel:
    def __init__(self, x, y, direction, lifespan=0.3):
        self.x = x
        self.y = y
        self.direction = direction
        self.lifespan = lifespan
        self.start_time = pygame.time.get_ticks() / 1000
        self.particles = []

        self._generate_symmetric_particles()

    def _generate_symmetric_particles(self):
        base_angle = math.atan2(self.direction.y, self.direction.x)
        arc_range = math.radians(120)
        num_particles = 30
        radius = 28

        for i in range(num_particles):
            t = (i / (num_particles - 1)) - 0.5
            angle = base_angle + t * arc_range
            offset_x = math.cos(angle) * radius
            offset_y = math.sin(angle) * radius

            scale_factor = 1 - abs(t) * 2
            speed = 1 + scale_factor * 2
            size = int(4 + scale_factor * 8)
            color = (100 + int(155 * scale_factor), 220, 255)

            self.particles.append({
                "angle": angle,
                "speed": speed,
                "size": size,
                "color": color,
                "offset_x": offset_x,
                "offset_y": offset_y,
                "age": 0,
                "lifespan": 15
            })

    def update(self):
        for p in self.particles:
            p["age"] += 1

    def is_finished(self):
        return all(p["age"] >= p["lifespan"] for p in self.particles)