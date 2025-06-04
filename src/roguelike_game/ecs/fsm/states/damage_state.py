import time
import logging
from roguelike_game.ecs.fsm.state import State
from roguelike_game.ecs.components.transform.velocity import Velocity
from roguelike_game.ecs.components.rendering.flash_component import FlashComponent

class DamageState(State):
    """
    Estado de impacto: muestra sprite de daño y pausa breve, luego transita al siguiente estado.
    """
    def __init__(self, next_state, from_left: bool):
        self.next_state = next_state
        self.from_left = from_left

    def enter(self, entity):
        self.start_time = time.time()
        eid = entity.id
        world = entity.world
        # Detener movimiento
        world.components['Velocity'][eid] = Velocity(0, 0)
        print(f"[DamageState] Entity {eid} stopped for damage; from_left={self.from_left}")
        # Aplicar flash blanco
        damage_cfg = world.components['DamageConfig'][eid]
        world.components.setdefault('FlashComponent', {})[eid] = FlashComponent((255,255,255), damage_cfg.duration)
        # Mostrar sprite de daño
        anim = world.components.get('Animator', {}).get(eid)
        if anim:
            state = 'damage_left' if self.from_left else 'damage_right'
            if state in anim.animations:
                anim.current_state = state
        else:
            logging.warning(f"[DamageState] No Animator for eid {eid}, skipping animation")

    def execute(self, entity, dt):
        # Tras 2 segundos, transitar a next_state
        elapsed = time.time() - self.start_time
        damage_cfg = entity.world.components['DamageConfig'][entity.id]
        if elapsed >= damage_cfg.duration:
            print(f"[DamageState] Entity {entity.id} switching to next state after {elapsed:.2f}s")
            entity.world.components['NPCState'][entity.id].fsm.change_state(self.next_state, entity)

    def exit(self, entity):
        print(f"[DamageState] Entity {entity.id} exiting damage state")
        # No acciones adicionales al salir
        pass
