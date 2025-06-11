from roguelike_game.ecs.fsm.state import State
from roguelike_game.ecs.fsm.states.idle_state import IdleState
import time

class PlayerAttackState(State):
    def enter(self, entity):
        # Detener movimiento
        vel = entity.world.components.get('Velocity', {}).get(entity.id)
        if vel:
            vel.vx = vel.vy = 0
        # Iniciar animación de ataque
        animator = entity.world.components.get('Animator', {}).get(entity.id)
        if animator:
            animator.current_state = 'attack'
        # Registrar inicio de ataque
        self.start_time = time.time()

    def execute(self, entity, dt):
        # Esperar duración del ataque
        if time.time() - self.start_time >= 0.3:
            entity.world.components['NPCState'][entity.id].fsm.change_state(IdleState(), entity)

    def exit(self, entity):
        # Restaurar animación a 'idle'
        animator = entity.world.components.get('Animator', {}).get(entity.id)
        if animator:
            animator.current_state = 'idle'