import pygame
import random
import math


class HealingParticle:
    def __init__(self, player, offset_x):
        self.player = player
        self.offset = pygame.Vector2(offset_x, 0)
        self.age = 0
        self.lifespan = 60  # Duración de la partícula

        self.size = random.randint(4, 8)
        self.color = random.choice([
            (0, 255, 100),
            (100, 255, 150),
            (0, 200, 100)
        ])

        # Movimiento inverso a la dirección del jugador
        if self.player.is_walking:
            move_dir = self.player.movement.last_move_dir
            self.extra_velocity = -0.5 * move_dir
        else:
            self.extra_velocity = pygame.Vector2(0, 0)

    def update(self):
        self.age += 1

    def is_dead(self):
        return self.age >= self.lifespan

    def render(self, screen, camera):
        sprite_w = self.player.sprite_size[0]
        base_pos = pygame.Vector2(self.player.x + sprite_w / 2, self.player.y + 120)

        vertical_rise = pygame.Vector2(0, -self.age * 2.0)
        horizontal_shift = self.extra_velocity * self.age

        world_pos = base_pos + self.offset + vertical_rise + horizontal_shift

        alpha = max(0, 255 * (1 - self.age / self.lifespan))
        surf = pygame.Surface((self.size, self.size), pygame.SRCALPHA)
        surf.fill((*self.color, int(alpha)))
        screen.blit(surf, camera.apply(world_pos))


class HealingAura:
    def __init__(self, player):
        self.player = player
        self.particles = []
        self.timer = 0
        self.duration = 120          # Total de frames de vida del aura
        self.elipse_lifespan = 100   # La elipse desaparece antes que las partículas

    def update(self):
        self.timer += 1

        if self.timer <= self.duration:
            for _ in range(3):
                # Distribución elíptica horizontal
                angle = random.uniform(0, 2 * math.pi)
                radius = random.uniform(0, 1) ** 0.5  # Distribución suave al centro
                a = self.player.sprite_size[0] // 2
                offset_x = math.cos(angle) * radius * a
                self.particles.append(HealingParticle(self.player, offset_x))

        for p in self.particles:
            p.update()

        self.particles = [p for p in self.particles if not p.is_dead()]

    def render(self, screen, camera):
        # 🌿 Dibujar el óvalo base
        sprite_w = self.player.sprite_size[0]
        base_x = self.player.x + sprite_w / 2
        base_y = self.player.y + 96  # pies

        world_pos = camera.apply((base_x, base_y))

        ellipse_width, _ = camera.scale((sprite_w, 1))
        ellipse_height = int(ellipse_width * 0.3)

        # ✨ Alpha dinámico para que desaparezca antes
        alpha = max(0, 255 * (1 - self.timer / self.elipse_lifespan))
        oval_surface = pygame.Surface((ellipse_width, ellipse_height), pygame.SRCALPHA)
        pygame.draw.ellipse(oval_surface, (0, 255, 100, int(alpha)), (0, 0, ellipse_width, ellipse_height))

        # 🎯 Desplazamiento fino en Y para que toque bien el suelo
        elipse_offset_y = 10  # 🔽 Ajuste sutil adicional hacia abajo

        screen.blit(oval_surface, (
            world_pos[0] - ellipse_width // 2,
            world_pos[1] - ellipse_height // 4 + elipse_offset_y
        ))

        # ✨ Dibujar partículas
        for p in self.particles:
            p.render(screen, camera)

    def is_empty(self):
        return self.timer >= self.duration and len(self.particles) == 0
