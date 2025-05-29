from roguelike_game.ecs.fsm.state import State
from roguelike_game.ecs.fsm.states.chase_state import ChaseState
from roguelike_game.ecs.fsm.states.flee_state import FleeState
from roguelike_game.ecs.fsm.states.attack_state import AttackState
from roguelike_game.ecs.fsm.states.death_state import DeathState
from roguelike_engine.config.config_tiles import TILE_SIZE

class AggroState(State):
    """
    Estado Aggro: persecución activa del jugador con evaluación de ataque y huida.
    """
    def enter(self, entity):
        # Inicializar animaciones de agresión si es necesario
        pass

    def execute(self, entity, dt):
        world = entity.world
        # Verificar muerte
        hp_cmp = world.components['Health'][entity]
        if hp_cmp.current_hp <= 0:
            world.components['NPCState'][entity].fsm.change_state(DeathState(), entity)
            return
        # La persecución se maneja directamente en ChaseState
        # Verificar salud para cambio a huida
        health_cmp = world.components['Health'][entity]
        if health_cmp.current_hp <= health_cmp.max_hp * 0.3:
            world.components['NPCState'][entity].fsm.change_state(FleeState(), entity)
            return
        # Verificar distancia de ataque
        pos = world.components['Position'][entity]
        player_pos = world.player_position
        if player_pos:
            dx = player_pos.x - pos.x
            dy = player_pos.y - pos.y
            dist_sq = dx*dx + dy*dy
            mr_cmp = world.components['MeleeRange'][entity]
            if dist_sq <= (mr_cmp.range * TILE_SIZE) ** 2:
                world.components['NPCState'][entity].fsm.change_state(AttackState(), entity)
                return
        # Si no ataca ni huye, continuar persiguiendo
        ChaseState().execute(entity, dt)

    def exit(self, entity):
        # Limpiar animaciones de agresión
        pass
