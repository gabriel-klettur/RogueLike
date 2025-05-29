from roguelike_game.ecs.fsm.state import State
from roguelike_game.ecs.components.transform.position import Position
from roguelike_game.ecs.components.transform.movement_speed import MovementSpeed
from roguelike_game.ecs.components.ai.aggro_range import AggroRange
from roguelike_engine.config.config_tiles import TILE_SIZE

class FleeState(State):
    """
    Estado Flee: huye del jugador cuando la salud es baja.
    """
    def enter(self, entity):
        # Opcional: iniciar animación de huida
        pass

    def execute(self, entity, dt):
        world = entity.world
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
        # Volver a PatrolState si fuera de rango
        dist_sq = dx*dx + dy*dy
        rng_cmp = world.components['AggroRange'][entity]
        if dist_sq > (rng_cmp.radius * TILE_SIZE) ** 2:
            from roguelike_game.ecs.fsm.states.patrol_state import PatrolState
            world.components['NPCState'][entity].fsm.change_state(PatrolState(), entity)

    def exit(self, entity):
        # Limpieza al salir de huida
        pass