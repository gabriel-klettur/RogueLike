from roguelike_game.ecs.fsm.state import State
from roguelike_engine.config.config_tiles import TILE_SIZE
from roguelike_game.ecs.fsm.states.death_state import DeathState
import time

class FleeState(State):
    """
    Estado Flee: huye del jugador cuando la salud es baja.
    """
    def enter(self, entity):
        # Iniciar temporizador de huida
        self.start_time = time.time()
        # Opcional: iniciar animación de huida
        pass

    def execute(self, entity, dt):
        world = entity.world
        # Volver a AggroState tras 5 segundos de huida
        if time.time() - self.start_time >= 5.0:
            # Importar localmente para evitar dependencia circular
            from roguelike_game.ecs.fsm.states.aggro_state import AggroState
            world.components['NPCState'][entity].fsm.change_state(AggroState(), entity)
            return
        # Verificar muerte
        hp_cmp = world.components['Health'][entity]
        if hp_cmp.current_hp <= 0:
            world.components['NPCState'][entity].fsm.change_state(DeathState(), entity)
            return
        pos = world.components['Position'][entity]
        player_pos = world.player_position
        if not player_pos:
            return
        dx = pos.x - player_pos.x
        dy = pos.y - player_pos.y
        # Normalizar vector de huida
        mag = (dx*dx + dy*dy) ** 0.5
        speed_cmp = world.components['MovementSpeed'][entity]
        step = speed_cmp.speed * dt if dt else speed_cmp.speed
        if mag != 0:
            pos.x += dx/mag * step
            pos.y += dy/mag * step
        # La huida continúa hasta agotar el temporizador; no cambiar a PatrolState aquí

    def exit(self, entity):
        # Limpieza al salir de huida
        pass