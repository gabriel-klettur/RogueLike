from roguelike_game.ecs.fsm.state import State
from roguelike_engine.config.config_tiles import TILE_SIZE
from roguelike_game.ecs.fsm.states.death_state import DeathState
from roguelike_game.ecs.components.transform.velocity import Velocity
import time

class FleeState(State):
    """
    Estado Flee: huye del jugador cuando la salud es baja.
    """
    def enter(self, entity):
        # Iniciar temporizador de huida y resetear velocidad
        self.start_time = time.time()
        eid = entity.id
        world = entity.world
        world.components['Velocity'][eid] = Velocity(0, 0)

    def execute(self, entity, dt):
        world = entity.world
        eid = entity.id
        # Resetear velocidad cada tick
        world.components['Velocity'][eid] = Velocity(0, 0)
        # Volver a AggroState tras 5 segundos de huida
        if time.time() - self.start_time >= 5.0:
            # Importar localmente para evitar dependencia circular
            from roguelike_game.ecs.fsm.states.aggro_state import AggroState
            world.components['NPCState'][entity].fsm.change_state(AggroState(), entity)
            return
        # Verificar muerte
        hp_cmp = world.components['Health'][eid]
        if hp_cmp.current_hp <= 0:
            world.components['NPCState'][entity].fsm.change_state(DeathState(), entity)
            return
        pos = world.components['Position'][eid]
        player_pos = world.player_position
        if not player_pos:
            return
        dx = pos.x - player_pos.x
        dy = pos.y - player_pos.y
        # Normalizar vector de huida
        mag = (dx*dx + dy*dy) ** 0.5
        speed_cmp = world.components['MovementSpeed'][eid]
        step = speed_cmp.speed * dt if dt else speed_cmp.speed
        if mag != 0:
            vx = dx/mag * step
            vy = dy/mag * step
            world.components['Velocity'][eid] = Velocity(vx, vy)

    def exit(self, entity):
        # Resetear velocidad al salir de FleeState
        eid = entity.id
        world = entity.world
        world.components['Velocity'][eid] = Velocity(0, 0)