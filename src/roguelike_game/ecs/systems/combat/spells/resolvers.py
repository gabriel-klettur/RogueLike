# Path: src/roguelike_game/ecs/systems/combat/spells/resolvers.py
import pygame
from roguelike_game.config.spells_config import SPELLS
from roguelike_game.ecs.components.transform.position import Position
from roguelike_game.ecs.components.transform.velocity import Velocity
from roguelike_game.ecs.components.abilities.fireball_component import FireballComponent
from roguelike_game.ecs.components.rendering.sprite import Sprite
from roguelike_game.ecs.components.transform.scale import Scale
import random
import pygame
from roguelike_game.ecs.components.particles.particle_component import ParticleComponent
from roguelike_game.ecs.components.abilities.laser_beam_component import LaserBeamComponent
from roguelike_game.ecs.components.abilities.dash_component import DashComponent
from roguelike_game.ecs.components.particles.dash_emitter_component import DashEmitterComponent
from roguelike_game.ecs.components.combat.hitbox import HitboxComponent
from roguelike_game.ecs.components.particles.slash_emitter_component import SlashEmitterComponent
from roguelike_game.ecs.components.abilities.lightning_component import LightningComponent
from roguelike_game.ecs.components.abilities.arcane_flame_component import ArcaneFlameComponent
from roguelike_game.ecs.systems.rendering.combat.spells.arcane_flame.model import ArcaneFlameModel
import math

class BaseSpellResolver:
    def resolve(self, world, caster, spawn_meta, cfg, camera):
        """Resolución genérica de hechizo."""
        raise NotImplementedError(f"No resolver for spell type: {cfg.get('type')}")

class ProjectileResolver(BaseSpellResolver):
    def resolve(self, world, caster, spawn_meta, cfg, camera):
        # Offset desde el centro del caster
        offset = spawn_meta.get('offset', 0)
        # Posición central del caster
        pos_cmp = world.components['Position'][caster]
        cx, cy = pos_cmp.x, pos_cmp.y
        sprite_cmp = world.components['Sprite'].get(caster)
        if sprite_cmp:
            w, h = sprite_cmp.image.get_size()
            cx += w / 2; cy += h / 2
        # Dirección hacia el cursor actual
        mx, my = pygame.mouse.get_pos()
        if camera:
            zoom = getattr(camera, 'zoom', 1.0)
            offset_x = getattr(camera, 'offset_x', 0)
            offset_y = getattr(camera, 'offset_y', 0)
            wx = mx / zoom + offset_x
            wy = my / zoom + offset_y
        else:
            wx, wy = mx, my
        dx, dy = wx - cx, wy - cy
        length = (dx * dx + dy * dy) ** 0.5 or 1
        dir_x, dir_y = dx / length, dy / length
        # Calcular posición de spawn
        sx, sy = cx + dir_x * offset, cy + dir_y * offset
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

class AuraResolver(BaseSpellResolver):
    def resolve(self, world, caster, spawn_meta, cfg, camera):
        # Aplica un aura al caster
        from roguelike_game.ecs.components.abilities.aura_component import AuraComponent
        radius = cfg.get('radius', 100)
        buff = cfg.get('buff', {})
        duration = cfg.get('duration', 5.0)
        world.components.setdefault('AuraComponent', {})[caster] = AuraComponent(radius, buff, duration)

class BeamResolver(BaseSpellResolver):
    """
    Resolver for continuous beam spells: spawns beam particles and applies damage along line.
    """
    def resolve(self, world, caster, spawn_meta, cfg, camera):
        offset = spawn_meta.get('offset', 0)
        pos_cmp = world.components['Position'][caster]
        cx, cy = pos_cmp.x, pos_cmp.y
        sprite_cmp = world.components['Sprite'].get(caster)
        if sprite_cmp:
            w, h = sprite_cmp.image.get_size()
            cx += w/2; cy += h/2
        # compute world target from cursor
        mx, my = pygame.mouse.get_pos()
        wx = mx / camera.zoom + camera.offset_x
        wy = my / camera.zoom + camera.offset_y
        dx, dy = wx - cx, wy - cy
        length = (dx*dx + dy*dy)**0.5 or 1
        # Register continuous laser beam component to handle particle emission and damage over time
        # Continuous beam: no fixed duration, removed on mouse release
        world.components.setdefault('LaserBeamComponent', {})[caster] = LaserBeamComponent(
            cx, cy, wx, wy,
            particle_count=cfg.get('particle_count', 0),
            dispersion=cfg.get('particle_dispersion', 0),
            colors=cfg.get('particle_colors', []),
            lifespan=float(cfg.get('lifespan', 0)),
            scale=cfg.get('scale', 1.0),
            damage=cfg.get('damage', 0),
            duration=None
        )

class DashResolver(BaseSpellResolver):
    """Resolver for dash spells: registers DashComponent for continuous dash movement."""
    def resolve(self, world, caster, spawn_meta, cfg, camera):
        pos_cmp = world.components['Position'][caster]
        cx, cy = pos_cmp.x, pos_cmp.y
        sprite_cmp = world.components['Sprite'].get(caster)
        if sprite_cmp:
            w, h = sprite_cmp.image.get_size()
            cx += w/2; cy += h/2
        mx, my = pygame.mouse.get_pos()
        wx = mx / camera.zoom + camera.offset_x
        wy = my / camera.zoom + camera.offset_y
        dx, dy = wx - cx, wy - cy
        length = (dx*dx + dy*dy)**0.5 or 1
        dir_x, dir_y = dx/length, dy/length
        speed = cfg.get('speed', 0)
        duration = cfg.get('duration', 0)
        world.components.setdefault('DashComponent', {})[caster] = DashComponent(dir_x, dir_y, speed, duration)
        world.components.setdefault('DashEmitterComponent', {})[caster] = DashEmitterComponent(
            count=10,
            lifespan=15,
            size_range=(3, 6),
            color_choices=((200,200,255),(150,150,255),(255,255,255)),
            speed_range=(1.0, 3.0)
        )

class SlashResolver(BaseSpellResolver):
    def resolve(self, world, caster, spawn_meta, cfg, camera):
        # Recalcular centro del caster
        spawn_offset = spawn_meta.get('offset', 0)
        cfg_offset = cfg.get('offset', 0)
        offset = spawn_offset + cfg_offset
        pos_cmp = world.components['Position'][caster]
        cx, cy = pos_cmp.x, pos_cmp.y
        sprite_cmp = world.components['Sprite'].get(caster)
        if sprite_cmp:
            w, h = sprite_cmp.image.get_size()
            cx += w/2; cy += h/2
        # Dirección al cursor
        mx, my = pygame.mouse.get_pos()
        wx = mx / camera.zoom + camera.offset_x
        wy = my / camera.zoom + camera.offset_y
        dx_raw, dy_raw = wx - cx, wy - cy
        length = (dx_raw*dx_raw + dy_raw*dy_raw)**0.5 or 1
        dir_x, dir_y = dx_raw/length, dy_raw/length
        # Parámetros de configuración
        radius = cfg.get('radius', 0)
        arc_range = math.radians(cfg.get('arc_range_degrees', 120))
        count = cfg.get('particle_count', 0)
        lifespan = cfg.get('lifespan', 0)
        size_min, size_max = cfg.get('size_range', [1,1])
        base_color = cfg.get('color', [255,255,255])
        speed_mult = cfg.get('speed_multiplier', 1.0)
        # Registrar hitbox de slash para colisión
        hb_id = world.create_entity()
        real_x = cx + dir_x * offset
        real_y = cy + dir_y * offset
        world.components['Position'][hb_id] = Position(real_x, real_y)
        world.components['HitboxComponent'][hb_id] = HitboxComponent(
            owner=caster,
            offset=offset,
            radius=radius,
            arc_angle=arc_range,
            direction=(dir_x, dir_y),
            lifespan=lifespan,
            damage=cfg.get('damage', 0),
        )
        # Añadir emisor de partículas de slash
        world.components.setdefault('SlashEmitterComponent', {})[caster] = SlashEmitterComponent(
            radius=radius,
            arc_range=arc_range,
            count=count,
            lifespan=lifespan,
            size_range=(size_min, size_max),
            color=tuple(base_color),
            speed_multiplier=speed_mult,
            direction=(dir_x, dir_y),
            offset=offset
        )

class LightningResolver(BaseSpellResolver):
    def resolve(self, world, caster, spawn_meta, cfg, camera):
        # Instanciar LightningComponent en el caster
        start = spawn_meta.get('spawn_pos', (0, 0))
        mx, my = pygame.mouse.get_pos()
        if camera:
            wx = mx / camera.zoom + camera.offset_x
            wy = my / camera.zoom + camera.offset_y
        else:
            wx, wy = mx, my
        comp = LightningComponent(start, (wx, wy),
                                   cfg.get('segments', 10),
                                   cfg.get('offset', 0),
                                   cfg.get('lifetime', 0))
        world.components.setdefault('LightningComponent', {})[caster] = comp

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

# Registro de resolutores por tipo de hechizo
default_resolvers = {
    'projectile': ProjectileResolver(),
    'aura': AuraResolver(),
    'beam': BeamResolver(),
    'dash': DashResolver(),
    'slash': SlashResolver(),
    'lightning': LightningResolver(),
    'arcane_flame': ArcaneFlameResolver(),
}
SPELL_RESOLVERS = default_resolvers

# Función para resolver hechizos