from roguelike_game.ecs.systems.fsm.state import State
from roguelike_game.ecs.systems.fsm.states.idle_state import IdleState
from roguelike_game.ecs.systems.fsm.anim_bridge import set_mapped_anim

class MoveState(State):
    def enter(self, entity):
        # Establecer animación base de movimiento si aplica (jugador será dirigido por PlayerFacingSystem)
        set_mapped_anim(entity, 'MoveState', direction=None)

    def execute(self, entity, dt):
        # Si no hay input de movimiento, volver a IdleState
        inp = entity.world.components.get('InputComponent', {}).get(entity.id)
        if not inp or (inp.move_x == 0 and inp.move_y == 0):
            entity.world.components['NPCState'][entity.id].fsm.change_state(IdleState(), entity)

    def exit(self, entity):
        # No forzar 'idle'; PlayerFacingSystem decidirá
        pass