from roguelike_game.ecs.systems.fsm.state import State
from roguelike_game.ecs.systems.fsm.states.idle_state import IdleState
import time
from roguelike_game.ecs.systems.fsm.anim_bridge import set_mapped_anim

class PlayerAttackState(State):
    def enter(self, entity):
        # Detener movimiento
        vel = entity.world.components.get('Velocity', {}).get(entity.id)
        if vel:
            vel.vx = vel.vy = 0
        # Iniciar animación de ataque vía mapa de animaciones (sin dirección específica)
        set_mapped_anim(entity, 'PlayerAttackState', direction=None, reset_frame=True)
        # Registrar inicio de ataque
        self.start_time = time.time()

    def execute(self, entity, dt):
        # Esperar duración del ataque
        if time.time() - self.start_time >= 0.3:
            entity.world.components['NPCState'][entity.id].fsm.change_state(IdleState(), entity)

    def exit(self, entity):
        # No forzar 'idle'; el PlayerFacingSystem resolverá el estado adecuado
        pass