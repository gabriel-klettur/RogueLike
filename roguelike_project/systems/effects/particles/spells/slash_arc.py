# roguelike_project/systems/effects/particles/spells/slash_arc.py

import pygame
import math

class SlashArcEffect:
    def __init__(self, player, direction):
        self.player = player
        self.direction = direction
        self.particles = []
        self.timer = 0
        self.duration = 0.2  # duración visual en segundos
        self.offsets = []
        self.create_symmetric_slash_particles()

    def create_symmetric_slash_particles(self):
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

            self.offsets.append({
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
        delta = self.get_delta()
        self.timer += delta
        for p in self.offsets:
            p["age"] += 1

    def render(self, screen, camera):
        cx = self.player.x + self.player.sprite_size[0] / 2
        cy = self.player.y + self.player.sprite_size[1] * 0.5

        for p in self.offsets:
            if p["age"] >= p["lifespan"]:
                continue

            x = cx + p["offset_x"] + math.cos(p["angle"]) * p["speed"] * p["age"]
            y = cy + p["offset_y"] + math.sin(p["angle"]) * p["speed"] * p["age"]

            alpha = max(0, 255 * (1 - p["age"] / p["lifespan"]))
            surf = pygame.Surface((p["size"], p["size"]), pygame.SRCALPHA)
            surf.fill((*p["color"], int(alpha)))
            screen.blit(surf, camera.apply((x, y)))

    def is_finished(self):
        return all(p["age"] >= p["lifespan"] for p in self.offsets)

    def get_delta(self):
        return pygame.time.get_ticks() / 1000 - self.timer
