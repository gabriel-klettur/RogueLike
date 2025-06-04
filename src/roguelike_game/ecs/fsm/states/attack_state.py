from roguelike_game.ecs.fsm.state import State
from roguelike_game.ecs.fsm.states.death_state import DeathState
from roguelike_game.ecs.fsm.states.chase_state import ChaseState
from roguelike_game.ecs.components.ai.chase_target import ChaseTarget
from roguelike_game.ecs.components.transform.velocity import Velocity
from roguelike_game.ecs.components.combat.npc_attack_cooldown import NPCAttackCooldown
from roguelike_game.ecs.components.combat.health import Health
from roguelike_engine.config.config_tiles import TILE_SIZE
import time

class AttackState(State):
    """
    Estado Attack: lógica de combate cuerpo a cuerpo.
    """
    def enter(self, entity):
        # Iniciar animación de ataque y asignar target de persecución
        world = entity.world
        eid = entity.id
        world.components['ChaseTarget'][eid] = ChaseTarget(world.player_entity)
        # Resetear velocidad al entrar en AttackState
        world.components['Velocity'][eid] = Velocity(0, 0)

    def execute(self, entity, dt):
        world = entity.world
        eid = entity.id
        # Verificar muerte
        hp_cmp = world.components['Health'][eid]
        if hp_cmp.current_hp <= 0:
            world.components['NPCState'][eid].fsm.change_state(DeathState(), entity)
            return
        # Obtener posiciones del NPC y jugador
        pos = world.components['Position'][eid]
        player_pos = world.player_position
        if not player_pos:
            return
        dx = player_pos.x - pos.x
        dy = player_pos.y - pos.y
        dist_sq = dx*dx + dy*dy
        # Si dentro de rango melee: atacar y quedarse en AttackState
        mr_cmp = world.components['MeleeRange'][eid]
        if dist_sq <= (mr_cmp.range * TILE_SIZE) ** 2:
            # Daño periódico al jugador cada 1s en AttackState
            now = time.time()
            cd_map = world.components.setdefault('NPCAttackCooldown', {})
            cd = cd_map.get(eid)
            if cd is None:
                # establecer primera oportunidad tras 1s
                cd_map[eid] = NPCAttackCooldown(next_time=now + 1)
            elif now >= cd.next_time:
                # aplicar daño al jugador
                ph = world.components['Health'][world.player_entity]
                ph.current_hp = max(0, ph.current_hp - 10)
                # reset cooldown
                cd_map[eid] = NPCAttackCooldown(next_time=now + 1)
            return
        # Fuera de rango: cambiar a ChaseState
        world.components['NPCState'][eid].fsm.change_state(ChaseState(), entity)

    def exit(self, entity):
        # Limpiar animación de ataque y remover target de persecución
        world = entity.world
        world.components.get('ChaseTarget', {}).pop(entity.id, None)
        # Resetear velocidad al salir de AttackState
        world.components['Velocity'][entity.id] = Velocity(0, 0)