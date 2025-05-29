from roguelike_game.ecs.fsm.state import State
from roguelike_game.ecs.fsm.states.death_state import DeathState
from roguelike_game.ecs.systems.combat.combat_system import CombatSystem
from roguelike_engine.config.config_tiles import TILE_SIZE

class AttackState(State):
    """
    Estado Attack: lógica de combate cuerpo a cuerpo.
    """
    def enter(self, entity):
        # Opcional: iniciar animación de ataque
        pass

    def execute(self, entity, dt):
        world = entity.world
        # Verificar muerte
        hp_cmp = world.components['Health'][entity]
        if hp_cmp.current_hp <= 0:
            world.components['NPCState'][entity].fsm.change_state(DeathState(), entity)
            return
        chase_cmp = world.components['ChaseTarget'].get(entity)
        if not chase_cmp:
            return
        target_id = chase_cmp.target
        # Ejecutar ataque
        CombatSystem().perform_melee(world, entity, target_id)
        # Tras ataque, comprobar si sigue en rango
        pos = world.components['Position'][entity]
        player_pos = world.player_position
        if player_pos:
            dx = player_pos.x - pos.x
            dy = player_pos.y - pos.y
            dist_sq = dx*dx + dy*dy
            mr_cmp = world.components['MeleeRange'][entity]
            if dist_sq <= (mr_cmp.range * TILE_SIZE) ** 2:
                # continuar atacando
                return
        # Fuera de rango: volver a AggroState
        from roguelike_game.ecs.fsm.states.aggro_state import AggroState
        world.components['NPCState'][entity].fsm.change_state(AggroState(), entity)

    def exit(self, entity):
        # Opcional: limpiar animación de ataque
        pass