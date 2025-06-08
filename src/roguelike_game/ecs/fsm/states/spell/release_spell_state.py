from roguelike_game.ecs.fsm.state import State
from roguelike_game.ecs.components.transform.position import Position
from roguelike_game.ecs.components.transform.velocity import Velocity
from roguelike_game.ecs.components.abilities.fireball_component import FireballComponent
from roguelike_game.ecs.fsm.states.spell.resolve_spell_state import ResolveSpellState
from roguelike_game.config.spells_config import SPELLS
import pygame
from roguelike_game.ecs.components.rendering.sprite import Sprite
from roguelike_game.ecs.components.transform.scale import Scale

class ReleaseSpellState(State):
    def enter(self, entity):
        # Cargar configuración según hechizo actual
        ctx = self.fsm.context
        spell_key = ctx.get('spell')
        cfg = SPELLS.get(spell_key, {})
        world = entity.world
        dx, dy = ctx.get('direction', (1, 0))
        # Proyectiles spawnean desde el centro del caster
        if cfg.get('type') == 'projectile':
            pos_cmp = world.components['Position'][entity.id]
            sprite_cmp = world.components.get('Sprite', {}).get(entity.id)
            if sprite_cmp:
                w, h = sprite_cmp.image.get_size()
                spawn_x, spawn_y = pos_cmp.x + w/2, pos_cmp.y + h/2
            else:
                spawn_x, spawn_y = pos_cmp.x, pos_cmp.y
        else:
            spawn_x, spawn_y = ctx.get('spawn_pos', (0, 0))
        fid = world.create_entity()
        # Mantener spawn_pos como centro de la fireball
        world.components['Position'][fid] = Position(spawn_x, spawn_y)
        speed = cfg.get('speed', 0)
        world.components['Velocity'][fid] = Velocity(dx * speed, dy * speed)
        world.components['FireballComponent'][fid] = FireballComponent(
            dx * speed, dy * speed,
            damage=cfg.get('damage', 0),
            lifespan=cfg.get('lifespan', 0),
            caster=entity.id,
            spell_key=spell_key,
            spawn_pos=(spawn_x, spawn_y)
        )
        # Añadir sprite y aplicar scale si existe ruta
        sprite_path = cfg.get('sprite')
        if sprite_path:
            img = pygame.image.load(sprite_path).convert_alpha()
            world.components['Sprite'][fid] = Sprite(img)
            world.components['Scale'][fid] = Scale(scale=cfg.get('scale', 1.0))
        ctx['fireball_id'] = fid

    def execute(self, entity, dt):
        # Transición inmediata a fase de resolución
        self.fsm.change_state(ResolveSpellState(), entity)

    def exit(self, entity):
        pass