import time
import logging
from roguelike_game.ecs.systems.fsm.state import State
from roguelike_game.ecs.components.transform.velocity import Velocity
from roguelike_game.ecs.systems.fsm.anim_bridge import set_mapped_anim

import logging
logger = logging.getLogger(__name__)

class DamageState(State):
    """
    Estado de impacto: muestra sprite de daño y pausa breve, luego transita al siguiente estado.
    """
    def __init__(self, next_state, from_left: bool):
        self.next_state = next_state
        self.from_left = from_left
        self.prev_anim_state = None

    def enter(self, entity):
        self.start_time = time.time()
        eid = entity.id
        world = entity.world
        # Detener movimiento
        world.components['Velocity'][eid] = Velocity(0, 0)
        logger.debug(f"[DamageState] Entity {eid} stopped for damage; from_left={self.from_left}")
        # Mostrar sprite de daño
        anim = world.components.get('Animator', {}).get(eid)

        if anim:
            # store previous animation state
            self.prev_anim_state = anim.current_state
            direction = 'left' if self.from_left else 'right'
            set_mapped_anim(entity, 'DamageState', direction, reset_frame=True)
        else:
            logging.warning(f"[DamageState] No Animator for eid {eid}, skipping animation")

    def execute(self, entity, dt):
        # Tras 2 segundos, transitar a next_state
        elapsed = time.time() - self.start_time
        damage_cfg = entity.world.components['DamageConfig'][entity.id]
        if elapsed >= damage_cfg.duration:
            logger.debug(f"[DamageState] Entity {entity.id} switching to next state after {elapsed:.2f}s")
            fsm = entity.world.components['NPCState'][entity.id].fsm
            # Intentar transición al siguiente estado definido por el sistema de combate
            fsm.change_state(self.next_state, entity)
            # Si fue bloqueada por el guard (seguimos en DamageState), aplicar fallback a Patrol si está permitido
            try:
                if isinstance(fsm.current_state, DamageState):
                    allowed = (getattr(fsm, 'context', {}) or {}).get('allowed_state_classes')
                    if allowed and 'PatrolState' in allowed:
                        from roguelike_game.ecs.systems.fsm.states.monster.patrol_state import PatrolState
                        logger.debug(f"[DamageState] Fallback to PatrolState for entity {entity.id}")
                        fsm.change_state(PatrolState(), entity)
            except Exception as ex:
                logger.debug(f"[DamageState] Fallback check failed for entity {entity.id}: {ex}")

    def exit(self, entity):
        eid = entity.id
        world = entity.world
        logger.debug(f"[DamageState] Entity {eid} exiting damage state")
        # Clear any remaining flash
        world.components.get('FlashComponent', {}).pop(eid, None)
        # Restore animation state before damage
        anim = world.components.get('Animator', {}).get(eid)
        prev = getattr(self, 'prev_anim_state', None)
        if anim and prev and prev in anim.animations:
            anim.current_state = prev
            anim.frame_idx = 0