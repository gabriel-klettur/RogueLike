from roguelike_game.ecs.systems.rendering.combat.spells.arcane_flame.model import ArcaneFlameModel
from roguelike_game.ecs.components.abilities.arcane_flame_component import ArcaneFlameComponent

from .base import BaseSpellResolver


class ArcaneFlameResolver(BaseSpellResolver):
    def resolve(self, world, caster, spawn_meta, cfg, camera):
        # Crear modelo legacy ArcaneFlame en posición de spawn
        spawn_x, spawn_y = spawn_meta.get('spawn_pos', (0, 0))
        radius = cfg.get('radius', 0)
        width = radius * 2
        height = radius * 2
        duration = cfg.get('duration', 0.0)
        model = ArcaneFlameModel(spawn_x, spawn_y, width, height, duration)
        world.components.setdefault('ArcaneFlameComponent', {})[caster] = ArcaneFlameComponent(model)
