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
        world = entity.world
        # Verificar salud y muerte
        hp_cmp = world.components.get('Health', {}).get(entity.id)
        if hp_cmp and hp_cmp.current_hp <= 0:
            npc_state = world.components.get('NPCState', {}).get(entity.id)
            if npc_state:
                npc_state.fsm.change_state(DeathState(), entity)
            return
        # Verificar aggro solo para NPCs con AggroRange
        rng_cmp = world.components.get('AggroRange', {}).get(entity.id)
        if not rng_cmp:
            return
        # Obtener posición y posición del jugador
        pos = world.components.get('Position', {}).get(entity.id)
        player_pos = getattr(world, 'player_position', None)
        if not pos or not player_pos:
            return
        # Calcular distancia al jugador
        dx = pos.x - player_pos.x
        dy = pos.y - player_pos.y
        if dx*dx + dy*dy <= (rng_cmp.radius * TILE_SIZE)**2:
            from roguelike_game.ecs.fsm.states.aggro_state import AggroState
            npc_state = world.components.get('NPCState', {}).get(entity.id)
            if npc_state:
                npc_state.fsm.change_state(AggroState(), entity)

    def exit(self, entity):
        # Limpiar animaciones o flags de Idle (opcional)
        pass