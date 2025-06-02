import pygame
from roguelike_game.config.spells_config import SPELLS
from roguelike_game.ecs.components.transform.position import Position
from roguelike_game.ecs.components.transform.velocity import Velocity
from roguelike_game.ecs.components.abilities.fireball_component import FireballComponent
from roguelike_game.ecs.components.rendering.sprite import Sprite
from roguelike_game.ecs.components.transform.scale import Scale

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
        wx = mx / camera.zoom + camera.offset_x
        wy = my / camera.zoom + camera.offset_y
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
            caster=caster
        )
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
        # Spawn healing particles VFX
        if cfg.get('vfx') == 'healing_ring':
            import random, math
            from roguelike_game.ecs.components.particles.particle_component import ParticleComponent
            # Center of caster
            pos_cmp = world.components['Position'][caster]
            cx, cy = pos_cmp.x, pos_cmp.y
            sprite_cmp = world.components['Sprite'].get(caster)
            if sprite_cmp:
                w, h = sprite_cmp.image.get_size()
                cx += w/2; cy += h/2
            # Spawn configurable particle burst
            count = cfg.get('particle_count', 20)
            speed = cfg.get('particle_speed', 1.0)
            color = tuple(cfg.get('particle_color', [0,255,100]))
            size = cfg.get('particle_size', 3)
            lifespan = cfg.get('particle_lifespan', 30)
            for _ in range(count):
                angle = random.random() * 2 * math.pi
                dx = math.cos(angle) * speed
                dy = math.sin(angle) * speed
                pid = world.create_entity()
                world.components['Position'][pid] = Position(cx, cy)
                world.components['ParticleComponent'][pid] = ParticleComponent(dx, dy, color, size, lifespan)

# Registro de resolutores por tipo de hechizo
default_resolvers = {
    'projectile': ProjectileResolver(),
    'aura': AuraResolver(),
}
SPELL_RESOLVERS = default_resolvers
