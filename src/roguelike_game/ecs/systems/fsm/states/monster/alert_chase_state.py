# Path: src/roguelike_game/ecs/fsm/states/monster/alert_chase_state.py
import math
import time
from roguelike_game.ecs.systems.fsm.state import State
from roguelike_game.ecs.systems.fsm.states.monster.patrol_state import PatrolState
from roguelike_game.ecs.systems.fsm.states.death_state import DeathState
from roguelike_game.ecs.components.transform.velocity import Velocity
from roguelike_engine.config.config_tiles import TILE_SIZE

class AlertChaseState(State):
    """
    Estado de persecución de 5 segundos tras recibir daño de largo alcance.
    """
    def enter(self, entity):
        # Inicializar temporizador de persecución extendida
        self.start_time = time.time()

    def execute(self, entity, dt):
        world = entity.world
        eid = entity.id
        # Verificar muerte
        hp_cmp = world.components['Health'][eid]
        if hp_cmp.current_hp <= 0:
            world.components['NPCState'][eid].fsm.change_state(DeathState(), entity)
            return
        # Si expiró tiempo de persecución, volver a patrulla
        if time.time() - self.start_time >= 5.0:
            world.components['NPCState'][eid].fsm.change_state(PatrolState(), entity)
            return
        # Lógica de chase (sin comprobar rango)
        pos = world.components['Position'][eid]
        player_pos = world.player_position
        if not player_pos:
            return
        dx = player_pos.x - pos.x
        dy = player_pos.y - pos.y
        dist_sq = dx*dx + dy*dy
        # Actualizar animación de chase según dirección
        anim = world.components['Animator'][eid]
        if abs(dx) > abs(dy):
            direction = 'left' if dx < 0 else 'right'
        else:
            direction = 'down' if dy > 0 else 'up'
        anim.current_state = f"chase_{direction}"
        # Mover con velocidad aumentada (50%) en chase
        speed_cmp = world.components['MovementSpeed'][eid]
        chase_speed = speed_cmp.speed * 1.5
        step = chase_speed * dt if dt else chase_speed
        if dist_sq > step*step:
            dist = math.sqrt(dist_sq)
            vx = dx/dist * step
            vy = dy/dist * step
            world.components['Velocity'][eid] = Velocity(vx, vy)
        else:
            world.components['Velocity'][eid] = Velocity(0, 0)

    def exit(self, entity):
        # Al salir de AlertChase, detener movimiento y limpiar animación
        eid = entity.id
        world = entity.world
        world.components['Velocity'][eid] = Velocity(0, 0)
        anim = world.components['Animator'][eid]
        if anim.current_state.startswith('chase_'):
            anim.current_state = anim.current_state[len('chase_'):]