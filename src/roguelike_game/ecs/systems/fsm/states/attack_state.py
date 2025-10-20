from roguelike_game.ecs.systems.fsm.state import State
from roguelike_game.ecs.components.ai.chase_target import ChaseTarget
from roguelike_game.ecs.components.transform.velocity import Velocity
from roguelike_game.ecs.components.combat.npc_attack_cooldown import NPCAttackCooldown
from roguelike_game.ecs.components.combat.health import Health
from roguelike_engine.config.config_tiles import TILE_SIZE
import time
from roguelike_game.ecs.systems.fsm.anim_bridge import set_mapped_anim, primary_direction_from_vector
from roguelike_game.ecs.utils.position_utils import compute_entity_center
from roguelike_game.config.spells_config import SPELLS
from roguelike_game.ecs.systems.combat.spells.resolvers import SPELL_RESOLVERS

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
        # Registrar inicio de ataque y asegurar duración en contexto FSM
        try:
            fsm = world.components['NPCState'][eid].fsm
            fsm.context['attack_start'] = time.time()
            try:
                dur = float(fsm.context.get('attack_duration', 0))
            except Exception:
                dur = 0.0
            if dur <= 0.0:
                # Intentar derivar de MeleeWeapon.cooldown; si no, fallback seguro
                try:
                    mw = world.components.get('MeleeWeapon', {}).get(eid)
                    if mw and hasattr(mw, 'cooldown') and float(mw.cooldown) > 0:
                        fsm.context['attack_duration'] = float(mw.cooldown)
                    else:
                        fsm.context['attack_duration'] = 0.5
                except Exception:
                    fsm.context['attack_duration'] = 0.5
        except Exception:
            # Si algo falla con el contexto, no bloquear la entrada al estado
            pass
        # Establecer animación de ataque según dirección hacia el jugador (usando centros)
        comps = world.components
        pos_map = comps.get('Position', {})
        spr_map = comps.get('Sprite', {})
        scl_map = comps.get('Scale', {})
        pos = pos_map.get(eid)
        spr = spr_map.get(eid)
        scl = scl_map.get(eid)
        player_id = getattr(world, 'player_entity', None)
        ppos = pos_map.get(player_id) if player_id is not None else None
        pspr = spr_map.get(player_id) if player_id is not None else None
        pscl = scl_map.get(player_id) if player_id is not None else None
        direction = None
        try:
            if pos and ppos:
                if spr:
                    c1 = compute_entity_center(pos, spr, scl)
                    x1, y1 = float(c1.x), float(c1.y)
                else:
                    x1, y1 = float(pos.x), float(pos.y)
                if pspr:
                    c2 = compute_entity_center(ppos, pspr, pscl)
                    x2, y2 = float(c2.x), float(c2.y)
                else:
                    x2, y2 = float(ppos.x), float(ppos.y)
                dx = x2 - x1
                dy = y2 - y1
                direction = primary_direction_from_vector(dx, dy)
        except Exception:
            direction = None
        set_mapped_anim(entity, 'AttackState', direction, reset_frame=True)

    def execute(self, entity, dt):
        world = entity.world
        eid = entity.id
        # Verificar muerte del propio NPC
        hp_cmp = world.components['Health'][eid]
        if hp_cmp.current_hp <= 0:
            # Import local para evitar importación circular con UnconsciousState
            from roguelike_game.ecs.systems.fsm.states.unconscious_state import UnconsciousState
            world.components['NPCState'][eid].fsm.change_state(UnconsciousState(), entity)
            return
        # Si el jugador está inconsciente (HP<=0) o ya tiene DeathTimer, dejar de atacar
        try:
            player_id = world.player_entity
            ph = world.components.get('Health', {}).get(player_id)
            player_dead = (ph is None) or (ph.current_hp <= 0)
            has_death_timer = player_id in world.components.get('DeathTimer', {})
            if player_dead or has_death_timer:
                from roguelike_game.ecs.systems.fsm.states.monster.patrol_state import PatrolState
                world.components['NPCState'][eid].fsm.change_state(PatrolState(), entity)
                return
        except Exception:
            # Si algo falla, por seguridad no atacar
            from roguelike_game.ecs.systems.fsm.states.monster.patrol_state import PatrolState
            world.components['NPCState'][eid].fsm.change_state(PatrolState(), entity)
            return
        # Obtener centros del NPC y jugador para cálculos de distancia y origen
        comps = world.components
        pos_map = comps.get('Position', {})
        spr_map = comps.get('Sprite', {})
        scl_map = comps.get('Scale', {})
        pos = pos_map.get(eid)
        spr = spr_map.get(eid)
        scl = scl_map.get(eid)
        player_id = getattr(world, 'player_entity', None)
        ppos = pos_map.get(player_id) if player_id is not None else None
        pspr = spr_map.get(player_id) if player_id is not None else None
        pscl = scl_map.get(player_id) if player_id is not None else None
        if not pos or not ppos:
            return
        try:
            if spr:
                c1 = compute_entity_center(pos, spr, scl)
                x1, y1 = float(c1.x), float(c1.y)
            else:
                x1, y1 = float(pos.x), float(pos.y)
            if pspr:
                c2 = compute_entity_center(ppos, pspr, pscl)
                x2, y2 = float(c2.x), float(c2.y)
            else:
                x2, y2 = float(ppos.x), float(ppos.y)
        except Exception:
            x1, y1 = float(pos.x), float(pos.y)
            x2, y2 = float(ppos.x), float(ppos.y)
        dx = x2 - x1
        dy = y2 - y1
        dist_sq = dx*dx + dy*dy
        # Si dentro de rango melee: atacar y quedarse en AttackState
        mr_cmp = world.components['MeleeRange'][eid]
        if dist_sq <= (mr_cmp.range * TILE_SIZE) ** 2:
            # Disparar slash del NPC con cooldown del hechizo
            now = time.time()
            cd_map = world.components.setdefault('NPCAttackCooldown', {})
            cd = cd_map.get(eid)
            # Determinar cooldown del slash desde config (fallback 1.0s)
            try:
                cfg = SPELLS.get('slash')
                cd_secs = float(cfg.get('cooldown_duration', 1.0)) if cfg else 1.0
            except Exception:
                cd_secs = 1.0
            if (cd is None) or (now >= cd.next_time):
                # Preparar spawn_meta para que el slash apunte al jugador y no rote con el mouse
                try:
                    spawn_meta = {
                        'target_eid': int(world.player_entity),
                        'rotate_with_owner': False,
                    }
                    resolver = SPELL_RESOLVERS.get('slash')
                    if resolver is not None:
                        resolver.resolve(world, eid, spawn_meta, cfg, None)
                except Exception:
                    pass
                # reset cooldown según el hechizo
                cd_map[eid] = NPCAttackCooldown(next_time=now + cd_secs)
            return
        # Fuera de rango: cambiar a ChaseState
        from roguelike_game.ecs.systems.fsm.states.monster.chase_state import ChaseState
        world.components['NPCState'][eid].fsm.change_state(ChaseState(), entity)

    def exit(self, entity):
        # Limpiar animación de ataque y remover target de persecución
        world = entity.world
        world.components.get('ChaseTarget', {}).pop(entity.id, None)
        # Resetear velocidad al salir de AttackState
        world.components['Velocity'][entity.id] = Velocity(0, 0)