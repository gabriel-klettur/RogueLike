from roguelike_game.ecs.systems.fsm.state import State
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
        # Ignorar Aggro para jugador sin componente MeleeRange
        if entity.id == world.player_entity:
            return
        # Verificar muerte
        hp_cmp = world.components['Health'][entity]
        if hp_cmp.current_hp <= 0:
            # Import local para evitar importación circular con UnconsciousState
            from roguelike_game.ecs.systems.fsm.states.unconscious_state import UnconsciousState
            world.components['NPCState'][entity].fsm.change_state(UnconsciousState(), entity)
            return
        # Si el jugador está inconsciente (HP<=0) o ya tiene DeathTimer, no agredir
        try:
            player_id = world.player_entity
            ph = world.components.get('Health', {}).get(player_id)
            player_dead = (ph is None) or (ph.current_hp <= 0)
            has_death_timer = player_id in world.components.get('DeathTimer', {})
            if player_dead or has_death_timer:
                from roguelike_game.ecs.systems.fsm.states.monster.patrol_state import PatrolState
                world.components['NPCState'][entity].fsm.change_state(PatrolState(), entity)
                return
        except Exception:
            pass
        # La persecución se maneja directamente en ChaseState
        # Verificar salud para cambio a huida
        health_cmp = world.components['Health'][entity]
        if health_cmp.current_hp <= health_cmp.max_hp * 0.3:
            from roguelike_game.ecs.systems.fsm.states.monster.flee_state import FleeState
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
                from roguelike_game.ecs.systems.fsm.states.attack_state import AttackState
                world.components['NPCState'][entity].fsm.change_state(AttackState(), entity)
                return
        # Si no ataca ni huye, continuar persiguiendo                
        from roguelike_game.ecs.systems.fsm.states.monster.chase_state import ChaseState
        world.components['NPCState'][entity].fsm.change_state(ChaseState(), entity)
        return

    def exit(self, entity):
        # Limpiar animaciones de agresión
        pass