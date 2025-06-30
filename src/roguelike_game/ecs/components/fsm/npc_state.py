# Path: src/roguelike_game/ecs/components/fsm/npc_state.py
from dataclasses import dataclass
from roguelike_game.ecs.systems.fsm.fsm import FiniteStateMachine

@dataclass
class NPCState:
    """
    Componente que almacena la FSM de un NPC y su estado actual.
    """
    fsm: FiniteStateMachine
    current: str