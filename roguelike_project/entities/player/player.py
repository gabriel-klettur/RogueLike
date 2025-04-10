# roguelike_project/entities/player/player.py

from .stats import PlayerStats
from .movement import PlayerMovement
from .renderer import PlayerRenderer
from .assets import load_character_assets
from roguelike_project.entities.combat.combat_system import CombatSystem  # 🆕 nuevo sistema

class Player:
    def __init__(self, x, y, character_name="first_hero"):
        self.x = x
        self.y = y
        self.character_name = character_name
        self.is_walking = False
        self.state = None

        self.stats = PlayerStats(character_name)
        self.sprites, self.sprite_size = load_character_assets(character_name)
        self.direction = "down"
        self.sprite = self.sprites[self.direction]

        self.rect = None
        self.hitbox = None

        self.movement = PlayerMovement(self)
        self.renderer = PlayerRenderer(self)
        self.combat = CombatSystem(self)  # ✅ Sistema de combate encapsulado

    def change_character(self, new_character_name):
        self.__init__(self.x, self.y, new_character_name)

    def move(self, dx, dy, collision_mask, obstacles):
        self.movement.move(dx, dy, collision_mask, obstacles)

    def take_damage(self):
        self.stats.take_damage()

    def restore_all(self):
        self.stats.restore_all()

    def shoot(self, angle):
        self.combat.shoot_fireball(angle)  # ✅ delegamos en combat

    def update(self, solid_tiles, enemies):
        self.combat.update(self.renderer.state)  # ✅ delegamos en combat

    def render(self, screen, camera):
        self.renderer.render(screen, camera)

    def render_hud(self, screen, camera):
        self.renderer.render_hud(screen, camera)
