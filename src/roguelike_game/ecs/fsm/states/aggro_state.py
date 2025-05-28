import math
from roguelike_game.ecs.fsm.state import State
from roguelike_game.ecs.systems.ai.aggro_system import AggroSystem
from roguelike_game.ecs.fsm.states.chase_state import ChaseState
from roguelike_game.ecs.components.transform.position import Position
from roguelike_game.ecs.components.transform.movement_speed import MovementSpeed
from roguelike_game.ecs.components.combat.health import Health
from roguelike_game.ecs.components.combat.melee_range import MeleeRange
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
        # Actualizar target de persecución
        AggroSystem().track_target(world, entity)
        # Verificar salud para cambio a huida
        health_cmp = world.components['Health'][entity]
        if health_cmp.current_hp <= health_cmp.max_hp * 0.3:
            from roguelike_game.ecs.fsm.states.flee_state import FleeState
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
                from roguelike_game.ecs.fsm.states.attack_state import AttackState
                world.components['NPCState'][entity].fsm.change_state(AttackState(), entity)
                return
        # Si no ataca ni huye, continuar persiguiendo
        ChaseState().execute(entity, dt)

    def exit(self, entity):
        # Limpiar animaciones de agresión
        pass
