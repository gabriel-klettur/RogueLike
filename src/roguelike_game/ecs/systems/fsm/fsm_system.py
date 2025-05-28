"""
Sistema ECS para actualizar la FSM de NPCs.
"""
from roguelike_game.ecs.components.fsm.npc_state import NPCState

class FSMSystem:
    """
    Recorre entidades con NPCState y ejecuta la FSM.
    """
    def update(self, world, camera=None):
        for eid in world.get_entities_with('NPCState'):
            npc_state = world.components['NPCState'][eid]
            npc_state.fsm.update(eid, 0)