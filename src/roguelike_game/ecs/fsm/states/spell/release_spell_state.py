from roguelike_game.ecs.fsm.state import State
from roguelike_game.ecs.components.transform.position import Position
from roguelike_game.ecs.components.transform.velocity import Velocity
from roguelike_game.ecs.components.abilities.fireball_component import FireballComponent
from roguelike_game.ecs.components.rendering.sprite import Sprite
from roguelike_game.ecs.fsm.states.spell.resolve_spell_state import ResolveSpellState
from roguelike_game.config.spells_config import SPELLS

cfg = SPELLS['fireball']

class ReleaseSpellState(State):
    def enter(self, entity):
        # Generar fireball directamente usando contexto de sub-FSM
        ctx = self.fsm.context
        world = entity.world
        dx, dy = ctx.get('direction', (1, 0))
        spawn_x, spawn_y = ctx.get('spawn_pos', (0, 0))
        fid = world.create_entity()
        world.components['Position'][fid] = Position(spawn_x, spawn_y)
        speed = cfg['speed']
        world.components['Velocity'][fid] = Velocity(dx * speed, dy * speed)
        world.components['FireballComponent'][fid] = FireballComponent(
            dx * speed, dy * speed,
            damage=cfg['damage'],
            lifespan=cfg['lifespan'],
            caster=entity.id
        )
        sprite_path = cfg.get('sprite', "assets/projectiles/fireball.png")
        world.components['Sprite'][fid] = Sprite(sprite_path)
        ctx['fireball_id'] = fid

    def execute(self, entity, dt):
        # Transición inmediata a fase de resolución
        self.fsm.change_state(ResolveSpellState(), entity)

    def exit(self, entity):
        pass