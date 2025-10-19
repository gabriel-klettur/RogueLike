import pygame
from .base import BaseSpellResolver
from .utils import mouse_world
from roguelike_game.ecs.components.abilities.lightning_component import LightningComponent


class LightningResolver(BaseSpellResolver):
    def resolve(self, world, caster, spawn_meta, cfg, camera):
        # Instanciar LightningComponent en el caster
        start = spawn_meta.get('spawn_pos', (0, 0))
        wx, wy = mouse_world(camera)
        comp = LightningComponent(start, (wx, wy),
                                   cfg.get('segments', 10),
                                   cfg.get('offset', 0),
                                   cfg.get('lifetime', 0))
        world.components.setdefault('LightningComponent', {})[caster] = comp
