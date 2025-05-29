import time
from roguelike_game.ecs.fsm.state import State
from roguelike_game.ecs.components.transform.position import Position
from roguelike_game.ecs.components.ai.aggro_range import AggroRange
from roguelike_game.ecs.fsm.states.death_state import DeathState
from roguelike_engine.config.config_tiles import TILE_SIZE

class IdleState(State):
    """
    Estado Idle: NPC en reposo, patrulla pasiva y espera detectar jugador.
    """
    def enter(self, entity):
        # Registrar timestamp de inicio (opcional)
        self.start_time = time.time()

    def execute(self, entity, dt):
        # Verificar muerte
        world = entity.world
        hp_cmp = world.components['Health'][entity]
        if hp_cmp.current_hp <= 0:
            world.components['NPCState'][entity].fsm.change_state(DeathState(), entity)
            return
        # Obtener mundo y posiciones
        pos = world.components['Position'][entity]
        player_pos = world.player_position
        if not player_pos:
            return
        dx = pos.x - player_pos.x
        dy = pos.y - player_pos.y
        dist_sq = dx*dx + dy*dy
        rng_cmp = world.components['AggroRange'][entity]
        if dist_sq <= (rng_cmp.radius * TILE_SIZE)**2:
            # Cambiar a AggroState de forma local
            from roguelike_game.ecs.fsm.states.aggro_state import AggroState
            npc_state = world.components['NPCState'][entity]
            npc_state.fsm.change_state(AggroState(), entity)

    def exit(self, entity):
        # Limpiar animaciones o flags de Idle (opcional)
        pass