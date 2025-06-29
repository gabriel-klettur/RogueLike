# Path: src/roguelike_game/ecs/fsm/states/player/move_state.py
from roguelike_game.ecs.fsm.state import State
from roguelike_game.ecs.fsm.states.idle_state import IdleState

class MoveState(State):
    def enter(self, entity):
        # Cambiar animación a 'walk'
        animator = entity.world.components.get('Animator', {}).get(entity.id)
        if animator:
            animator.current_state = 'walk'

    def execute(self, entity, dt):
        # Si no hay input de movimiento, volver a IdleState
        inp = entity.world.components.get('InputComponent', {}).get(entity.id)
        if not inp or (inp.move_x == 0 and inp.move_y == 0):
            entity.world.components['NPCState'][entity.id].fsm.change_state(IdleState(), entity)

    def exit(self, entity):
        # Restaurar animación a 'idle'
        animator = entity.world.components.get('Animator', {}).get(entity.id)
        if animator:
            animator.current_state = 'idle'