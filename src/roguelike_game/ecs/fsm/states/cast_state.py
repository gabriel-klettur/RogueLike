from roguelike_game.ecs.fsm.state import State
from roguelike_game.ecs.fsm.fsm import FiniteStateMachine
from roguelike_game.ecs.fsm.states.spell.prepare_spell_state import PrepareSpellState
from roguelike_game.ecs.fsm.states.aggro_state import AggroState


class CastState(State):
    def __init__(self):
        self.spell_fsm = FiniteStateMachine(PrepareSpellState())
        # Contexto compartido para la sub-FSM de hechizo
        self.spell_fsm.context = {}
        self.direction: tuple[float,float] = (0, 0)
        self.spawn_pos: tuple[float,float] = (0, 0)

    def enter(self, entity):
        # Calcular dirección y posición de spawn de la fireball (usa contexto si fue proporcionado)
        ctx = self.spell_fsm.context
        if 'direction' in ctx and 'spawn_pos' in ctx:
            self.direction, self.spawn_pos = ctx['direction'], ctx['spawn_pos']
        else:
            world = entity.world
            pos_cmp = world.components['Position'][entity.id]
            player_pos = world.player_position
            if player_pos:
                dx = player_pos.x - pos_cmp.x
                dy = player_pos.y - pos_cmp.y
                length = (dx*dx + dy*dy) ** 0.5 or 1
                self.direction = (dx/length, dy/length)
            else:
                self.direction = (1, 0)
            spawn_x, spawn_y = pos_cmp.x, pos_cmp.y
            sprite_cmp = world.components['Sprite'].get(entity.id)
            if sprite_cmp:
                w, h = sprite_cmp.image.get_size()
                spawn_x += w/2; spawn_y += h/2
            self.spawn_pos = (spawn_x, spawn_y)
            # Almacenar dirección y posición en contexto de la sub-FSM para ReleaseSpellState y ResolveSpellState
            self.spell_fsm.context['direction'] = self.direction
            self.spell_fsm.context['spawn_pos'] = self.spawn_pos
        # Iniciar sub-FSM de hechizo
        self.spell_fsm.current_state.enter(entity)

    def execute(self, entity, dt):
        # Actualiza la sub-FSM (invoca execute en estados anidados)
        self.spell_fsm.update(entity, dt)
        # Cuando la sub-FSM vuelve a AggroState, procesar según tipo de entidad
        if isinstance(self.spell_fsm.current_state, AggroState):
            # Solo reconfigurar la FSM global si existe NPCState para esta entidad
            npcs = entity.world.components.get('NPCState', {})
            if entity.id in npcs:
                npcs[entity.id].fsm.change_state(AggroState(), entity)

    def exit(self, entity):
        pass