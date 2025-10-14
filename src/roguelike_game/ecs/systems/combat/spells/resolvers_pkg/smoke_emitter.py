from .base import BaseSpellResolver
from .utils import get_entity_center
from roguelike_game.ecs.systems.rendering.combat.spells.smoke_emitter.model import SmokeEmitterModel
from roguelike_game.ecs.components.abilities.smoke_emitter_component import SmokeEmitterComponent


class SmokeEmitterResolver(BaseSpellResolver):
    def resolve(self, world, caster, spawn_meta, cfg, camera):
        # Resolver para emisor de humo
        cx, cy = get_entity_center(world, caster)
        color = tuple(cfg.get('particle_color', (200,200,200)))
        emit_rate = cfg.get('emit_rate', 2)
        model = SmokeEmitterModel(cx, cy, color, emit_rate)
        world.components.setdefault('SmokeEmitterComponent', {})[caster] = SmokeEmitterComponent(model)
