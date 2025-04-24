# File: roguelike_project/systems/combat/combat_system.py
import math
import pygame

class CombatSystem:
    """
    Ahora solo maneja la lógica de colisión de ataques cuerpo a cuerpo
    (slash_effects), sin gestionar proyectiles.
    """
    def __init__(self, state):
        self.state = state        

    def update(self):
        # Solo colisiones de slash effects contra enemigos
        for enemy in self.state.enemies:
            self.check_enemy_collisions(enemy)

    def render(self, screen, camera):
        # CombatSystem no dibuja nada directamente ahora
        return []

    # 🎯 Comprueba las colisiones de cada enemigo con las partículas de slash
    def check_enemy_collisions(self, enemy):
        for effect in self.state.systems.effects.slash_effects:
            for particle in effect.offsets:
                if self._particle_hits_enemy(enemy, particle):
                    self.apply_damage_to_enemy(enemy, 10)

    def _particle_hits_enemy(self, enemy, particle) -> bool:
        # Cálculo de la posición mundial de la partícula
        cx = self.state.player.x + self.state.player.sprite_size[0] / 2
        cy = self.state.player.y + self.state.player.sprite_size[1] * 0.5
        x = cx + particle["offset_x"] + math.cos(particle["angle"]) * particle["speed"] * particle["age"]
        y = cy + particle["offset_y"] + math.sin(particle["angle"]) * particle["speed"] * particle["age"]

        particle_rect = pygame.Rect(x, y, particle["size"], particle["size"])
        return enemy.hitbox.colliderect(particle_rect)

    # 🎯 Aplica daño e imprime en consola
    def apply_damage_to_enemy(self, enemy, damage):
        if enemy.alive:
            enemy.take_damage(damage)
            print(f"⚔️ {enemy.name} recibió {damage} de daño. Salud restante: {enemy.health}")
