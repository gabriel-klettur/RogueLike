from dataclasses import dataclass
from roguelike_game.ecs.fsm.fsm import FiniteStateMachine

@dataclass
class NPCState:
    """
    Componente que almacena la FSM de un NPC y su estado actual.
    """
    fsm: FiniteStateMachine
    current: str