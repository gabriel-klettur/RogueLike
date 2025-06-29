# Path: src/roguelike_game/ecs/fsm/states/player/player_interact_state.py
from roguelike_game.ecs.fsm.state import State
from roguelike_game.ecs.fsm.states.idle_state import IdleState

class PlayerInteractState(State):
    """Estado de interacción del jugador (recoger objetos, usar puertas, etc.)."""
    def enter(self, entity):
        # Iniciar lógica o animación de interacción
        pass

    def execute(self, entity, dt):
        # Finalizar interacción tras lógica o input
        entity.world.components['NPCState'][entity.id].fsm.change_state(IdleState(), entity)

    def exit(self, entity):
        # Limpiar flags o animaciones de interacción
        pass