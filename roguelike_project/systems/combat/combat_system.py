# roguelike_project/systems/combat/combat_system.py
import math
import pygame

from roguelike_project.systems.combat.view.effects.animations.projectiles_system import ProjectilesManager

class CombatSystem:
    def __init__(self, state):
        self.state = state
        self.projectiles = ProjectilesManager(state)

    def update(self):
        self.projectiles.update()
        for enemy in self.state.enemies:
            self.check_enemy_collisions(enemy)

    def render(self, screen, camera):
        return self._render_projectiles(screen, camera)

    def _render_projectiles(self, screen, camera):
        return self.projectiles.render(screen, camera)

    #🎯 Método para comprobar las colisiones de los enemigos con los efectos de los ataques
    def check_enemy_collisions(self, enemy):
        for effect in self.state.systems.effects.slash_effects:
            for particle in effect.offsets:
                if self.check_collision_with_enemy(enemy, particle):
                    self.apply_damage_to_enemy(enemy, 10)  # Aplica daño

    # Método para verificar la colisión entre la partícula y el enemigo
    def check_collision_with_enemy(self, enemy, particle):
        # Coordenadas de la partícula
        cx = self.state.player.x + self.state.player.sprite_size[0] / 2
        cy = self.state.player.y + self.state.player.sprite_size[1] * 0.5
        x = cx + particle["offset_x"] + math.cos(particle["angle"]) * particle["speed"] * particle["age"]
        y = cy + particle["offset_y"] + math.sin(particle["angle"]) * particle["speed"] * particle["age"]

        # Crear un rectángulo para la partícula
        particle_rect = pygame.Rect(x, y, particle["size"], particle["size"])

        # Verificar si la partícula toca el hitbox del enemigo
        if enemy.hitbox.colliderect(particle_rect):
            print(f"💥 ¡Colisión con enemigo en {enemy.x}, {enemy.y}!")
            return True
        return False

    # 🎯 Aplicación de daño al enemigo
    def apply_damage_to_enemy(self, enemy, damage):
        if enemy.alive:
            enemy.take_damage(damage)
            print(f"⚔️ {enemy.name} recibió {damage} de daño. Salud restante: {enemy.health}")