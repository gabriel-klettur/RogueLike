import pygame
from roguelike_game.ecs.components.transform.position import Position
from roguelike_game.ecs.components.transform.velocity import Velocity
from roguelike_game.ecs.components.abilities.fireball_component import FireballComponent
from roguelike_game.ecs.components.rendering.sprite import Sprite
from roguelike_game.ecs.components.transform.scale import Scale

from .base import BaseSpellResolver
from .utils import get_entity_center, mouse_world, direction_from_to, spawn_at_offset


class ProjectileResolver(BaseSpellResolver):
    def resolve(self, world, caster, spawn_meta, cfg, camera):
        # Offset desde el centro del caster
        offset = spawn_meta.get('offset', 0)
        # Posición central del caster
        cx, cy = get_entity_center(world, caster)
        # Dirección hacia el cursor actual
        wx, wy = mouse_world(camera)
        dir_x, dir_y, _ = direction_from_to(cx, cy, wx, wy)
        # Calcular posición de spawn
        sx, sy = spawn_at_offset(cx, cy, dir_x, dir_y, offset)
        # Crear entidad de proyectil
        fid = world.create_entity()
        world.components['Position'][fid] = Position(sx, sy)
        speed = cfg.get('speed', 0)
        world.components['Velocity'][fid] = Velocity(dir_x * speed, dir_y * speed)
        world.components['FireballComponent'][fid] = FireballComponent(
            dir_x * speed, dir_y * speed,
            damage=cfg.get('damage', 0),
            lifespan=cfg.get('lifespan', 0),
            caster=caster,
            spell_key=spawn_meta.get('spell'),
            spawn_pos=(sx, sy)
        )
        # Destruir automáticamente luego de range si es configurado
        max_range = cfg.get('range', 0)
        if max_range:
            # programar expiración por rango en el componente (se maneja en FireballSystem)
            pass
        sprite_path = cfg.get('sprite')
        if sprite_path:
            img = pygame.image.load(sprite_path).convert_alpha()
            world.components['Sprite'][fid] = Sprite(img)
            world.components['Scale'][fid] = Scale(scale=cfg.get('scale', 1.0))
