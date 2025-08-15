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
        # Registrar inicio de ataque y duración por defecto en el contexto de la FSM
        fsm = entity.world.components['NPCState'][entity.id].fsm
        fsm.context['attack_start'] = time.time()

    def execute(self, entity, dt):
        # La transición a Idle ahora es gobernada por JSON ('after_attack') evaluada en FSMSystem
        pass

    def exit(self, entity):
        # No forzar 'idle'; el PlayerFacingSystem resolverá el estado adecuado
        pass