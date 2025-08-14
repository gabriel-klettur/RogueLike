"""
Sistema ECS para actualizar la FSM de NPCs.
"""
from roguelike_engine.utils.benchmark import benchmark
import roguelike_engine.config.config as config
from roguelike_editors.fsm.services.fsm_registry import get_state_class
from roguelike_game.ecs.systems.fsm.states.attack_state import AttackState
from roguelike_game.ecs.systems.fsm.states.damage_state import DamageState
from roguelike_game.ecs.systems.fsm.states.death_state import DeathState

# Wrapper para pasar entidad con acceso a world e id como key en componentes
class _EntityProxy:
    def __init__(self, world, entity_id):
        self.world = world
        self.id = entity_id
    def __hash__(self):
        return hash(self.id)
    def __eq__(self, other):
        if isinstance(other, _EntityProxy):
            return self.id == other.id
        return other == self.id
    def __repr__(self):
        return f"<EntityProxy {self.id}>"

class FSMSystem:
    def __init__(self, perf_log):
        self.perf_log = perf_log

    @benchmark(lambda self: self.perf_log, "4.2.1.FSMSystem.update")
    def update(self, world, camera=None):
        # Iterar sobre copia para evitar modificación concurrente al remover entidades
        for eid in list(world.get_entities_with('NPCState')):
            npc_state = world.components['NPCState'][eid]
            entity = _EntityProxy(world, eid)
            # Consumir eventos de FSM antes de update() por entidad
            self._process_events(world, eid, npc_state, entity)
            npc_state.fsm.update(entity, 0)

    def _process_events(self, world, eid, npc_state, entity):
        queue_map = world.components.setdefault('FSMEventQueue', {})
        events = queue_map.get(eid)
        if not events:
            return
        fsm = npc_state.fsm
        # Consumir en FIFO
        while events:
            ev = events.pop(0)
            etype = ev.get('type')
            if etype == 'OnHit':
                from_left = bool(ev.get('from_left', False))
                # Política de siguiente estado tras Damage
                current = fsm.current_state
                if isinstance(current, AttackState):
                    next_state = AttackState()
                else:
                    cls_name = fsm.context.get('damage_next_class', 'PatrolState')
                    cls = get_state_class(cls_name)
                    next_state = cls() if cls is not None else AttackState()
                fsm.change_state(DamageState(next_state, from_left), entity)
            elif etype == 'OnDeath':
                fsm.change_state(DeathState(), entity)