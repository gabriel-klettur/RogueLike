import pygame
import math
import random
from roguelike_project.systems.effects.particles.particle import Particle
from roguelike_project.systems.combat.explosions.electric import ElectricExplosion  # ⚡ Explosión visual

class LaserBeam:
    def __init__(self, x1, y1, x2, y2, particle_count=60, enemies=None, damage=0.25):
        self.particles = []
        self.finished = False
        self.enemies = enemies or []
        self.damage = damage
        self._damaged_ids = set()  # 🛡️ Para no dañar 2 veces al mismo enemigo

        # Calcular dirección del rayo
        dx = x2 - x1
        dy = y2 - y1
        self.distance = math.hypot(dx, dy)
        if self.distance == 0:
            self.explosion = None
            return

        self.angle = math.atan2(dy, dx)
        self.origin = (x1, y1)
        self.target = (x2, y2)

        # ⚡ Explosión eléctrica en el destino
        self.explosion = ElectricExplosion(x2, y2)

        # 🔵 Partículas visuales del láser
        for i in range(particle_count):
            t = i / particle_count
            px = x1 + t * dx + random.uniform(-4, 4)
            py = y1 + t * dy + random.uniform(-4, 4)
            color = random.choice([(0, 255, 255), (150, 255, 255), (255, 255, 255)])
            self.particles.append(Particle(
                px, py,
                angle=self.angle + random.uniform(-0.1, 0.1),
                speed=0,
                color=color,
                size=random.randint(2, 4),
                lifespan=5
            ))

    def update(self):
        
        # Actualizar partículas
        for p in self.particles:
            p.update()
        self.particles = [p for p in self.particles if p.age < p.lifespan]

        # Actualizar explosión
        if self.explosion:
            self.explosion.update()

        # 🧠 Verificar colisiones con enemigos        
        self.check_collisions()

        # Verificar si terminó
        self.finished = len(self.particles) == 0 and (self.explosion is None or self.explosion.finished)        

        return not self.finished  # Para permitir eliminación desde player.update()

    def check_collisions(self):        
        if not self.enemies:            
            return        

        for enemy in self.enemies:
            if not enemy.alive:                
                continue
            if id(enemy) in self._damaged_ids:                
                continue

            # Coordenadas del enemigo
            ex = enemy.x + enemy.sprite_size[0] / 2
            ey = enemy.y + enemy.sprite_size[1] / 2

            # Proyección del punto sobre el rayo
            x1, y1 = self.origin
            x2, y2 = self.target
            dx, dy = x2 - x1, y2 - y1
            length_sq = dx ** 2 + dy ** 2
            if length_sq == 0:                
                continue

            t = max(0, min(1, ((ex - x1) * dx + (ey - y1) * dy) / length_sq))
            closest_x = x1 + t * dx
            closest_y = y1 + t * dy
            dist = math.hypot(ex - closest_x, ey - closest_y)            

            if dist <= 100:
                enemy.take_damage(self.damage)
                self._damaged_ids.add(id(enemy))            
                

    def render(self, screen, camera):
        for p in self.particles:
            p.render(screen, camera)
        if self.explosion:
            self.explosion.render(screen, camera)
