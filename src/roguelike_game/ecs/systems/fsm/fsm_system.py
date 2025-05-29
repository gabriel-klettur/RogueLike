"""
Sistema ECS para actualizar la FSM de NPCs.
"""


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
    """
    Recorre entidades con NPCState y ejecuta la FSM.
    """
    def update(self, world, camera=None):
        # Iterar sobre copia para evitar modificación concurrente al remover entidades
        for eid in list(world.get_entities_with('NPCState')):
            npc_state = world.components['NPCState'][eid]
            entity = _EntityProxy(world, eid)
            npc_state.fsm.update(entity, 0)