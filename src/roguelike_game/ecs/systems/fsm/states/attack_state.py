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
from roguelike_game.ecs.components.combat.telegraph_arc import TelegraphArc
from roguelike_game.ecs.components.combat.windup_outline import WindupOutline

class AttackState(State):
    """
    Estado Attack: lógica de combate cuerpo a cuerpo.
    """
    def enter(self, entity):
        # Iniciar animación de ataque y asignar target de persecución
        world = entity.world
        eid = entity.id
        # Para el boss final, no asignar ChaseTarget durante AttackState (evita movimiento por sistemas de persecución)
        try:
            arche_map = world.components.get('MonsterArchetype', {})
            mt = arche_map.get(eid)
            mtype = (getattr(mt, 'type', None) or '').lower() if mt else None
            is_final_boss = bool(isinstance(mtype, str) and mtype.startswith('final_boss_barbol'))
        except Exception:
            is_final_boss = False
        if not is_final_boss:
            world.components['ChaseTarget'][eid] = ChaseTarget(world.player_entity)
        else:
            # Asegurar que no quede rastro de ChaseTarget del frame previo
            world.components.get('ChaseTarget', {}).pop(eid, None)
        # Resetear velocidad al entrar en AttackState
        world.components['Velocity'][eid] = Velocity(0, 0)
        # Registrar inicio de ataque y asegurar duración en contexto FSM
        try:
            fsm = world.components['NPCState'][eid].fsm
            fsm.context['attack_start'] = time.time()
            # Asegurar que no quede marcado como disparado de ciclos previos
            try:
                fsm.context.pop('attack_fired', None)
            except Exception:
                pass
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
        # Si es Final Boss Barbol y ya disparó, permitir salida cuando termine el lock, sin depender de rango
        try:
            mt = world.components.get('MonsterArchetype', {}).get(eid)
            mtype = (getattr(mt, 'type', None) or '').lower() if mt else None
            is_final_boss = bool(isinstance(mtype, str) and mtype.startswith('final_boss_barbol'))
        except Exception:
            is_final_boss = False
        if is_final_boss:
            try:
                fsm = world.components['NPCState'][eid].fsm
                now = time.time()
                fired = bool(fsm.context.get('attack_fired', False))
                lock_until = float(fsm.context.get('lock_move_until', 0.0) or 0.0)
                if fired and now >= lock_until:
                    try:
                        world.components.get('TelegraphArc', {}).pop(eid, None)
                    except Exception:
                        pass
                    try:
                        world.components.get('WindupOutline', {}).pop(eid, None)
                    except Exception:
                        pass
                    world.components.get('ChaseTarget', {}).pop(eid, None)
                    from roguelike_game.ecs.systems.fsm.states.monster.chase_state import ChaseState
                    world.components['NPCState'][eid].fsm.change_state(ChaseState(), entity)
                    return
            except Exception:
                pass
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
            # Aplicar retraso de "wind-up" antes de ejecutar el ataque
            # Se basa en un timestamp guardado al entrar en AttackState (attack_start)
            # y un valor configurable en el contexto FSM: 'attack_windup_s' (por defecto 1.0s)
            try:
                fsm = world.components['NPCState'][eid].fsm
                # Asegurar que attack_start exista; si no, inicializar ahora mismo
                start_t = float(fsm.context.get('attack_start') or now)
                fsm.context.setdefault('attack_start', start_t)
                windup_s = float(fsm.context.get('attack_windup_s', 1.0))
                # Movimiento bloqueado temporalmente tras el disparo
                lock_until = float(fsm.context.get('lock_move_until', 0.0) or 0.0)
            except Exception:
                start_t = now
                windup_s = 1.0
                lock_until = 0.0
            # Wind-up para TODOS los NPCs: inmovilizar y esperar antes de ejecutar el ataque
            if now - start_t < windup_s:
                # Inmovilizar durante el wind-up y evitar que sistemas de persecución reintroduzcan movimiento
                world.components['Velocity'][eid] = Velocity(0, 0)
                world.components.get('ChaseTarget', {}).pop(eid, None)
                # Registrar/actualizar outline amarillo del collider mientras dura el wind-up
                try:
                    world.components.setdefault('WindupOutline', {})[eid] = WindupOutline()
                except Exception:
                    pass
                # Telegraph para jefe final o para clases con flag use_attack_telegraph
                show_flag = False
                try:
                    show_flag = bool(world.components['NPCState'][eid].fsm.context.get('use_attack_telegraph', False))
                except Exception:
                    show_flag = False
                if is_final_boss or show_flag:
                    try:
                        arche_map = world.components.get('MonsterArchetype', {})
                        mt = arche_map.get(eid)
                        mtype = (getattr(mt, 'type', None) or '').lower() if mt else None
                    except Exception:
                        mtype = None
                    spell_id = 'hostile_slash'
                    if isinstance(mtype, str) and mtype.startswith('final_boss_barbol'):
                        spell_id = 'boss_barbol_slash'
                    elif mtype in ('barbol_oscuro', 'oscuro', 'dark'):
                        spell_id = 'hostile_slash_dark'
                    elif mtype in ('barbol_morado', 'morado', 'purple'):
                        spell_id = 'hostile_slash_purple'
                    elif mtype in ('barbol_boss', 'boss'):
                        spell_id = 'hostile_slash_red'
                    elif mtype in ('barbol_cyan', 'cyan'):
                        spell_id = 'hostile_slash_cyan'
                    elif mtype in ('barbol_gris', 'gris', 'gray', 'grey'):
                        spell_id = 'hostile_slash_gray'
                    elif mtype in ('barbol_gigante', 'gigante', 'giant'):
                        spell_id = 'hostile_slash_giant'
                    cfg = SPELLS.get(spell_id) or SPELLS.get('hostile_slash') or SPELLS.get('slash')
                    # Usar hit_radius/hit_arc_degrees si existen, si no, radius/arc_range_degrees
                    try:
                        import math
                        hit_radius = float(cfg.get('hit_radius', 0.0)) if cfg else 0.0
                        if hit_radius <= 0.0:
                            hit_radius = float(cfg.get('radius', 0.0)) if cfg else 0.0
                        arc_deg = float(cfg.get('hit_arc_degrees', 0.0)) if cfg else 0.0
                        if arc_deg <= 0.0:
                            arc_deg = float(cfg.get('arc_range_degrees', 0.0)) if cfg else 0.0
                        arc_rad = math.radians(arc_deg)
                        # Dirección normalizada hacia el jugador
                        mag = (dx*dx + dy*dy) ** 0.5
                        ndx, ndy = (dx / mag, dy / mag) if mag > 1e-6 else (1.0, 0.0)
                        # Color desde cfg.color (si no, fallback). alpha semitransparente
                        col = cfg.get('telegraph_color', None) if cfg else None
                        if not (isinstance(col, (list, tuple)) and len(col) >= 3):
                            col = cfg.get('color', None) if cfg else None
                        if not (isinstance(col, (list, tuple)) and len(col) >= 3):
                            col = [255, 230, 150]
                        a = int(cfg.get('telegraph_alpha', 90)) if cfg else 90
                        a = max(0, min(255, a))
                        rgba = (int(col[0]), int(col[1]), int(col[2]), a)
                        # Offset visual coherente con el slash
                        offset = float(cfg.get('offset', 0.0)) if cfg else 0.0
                        # Progreso radial 0..1 del wind-up
                        try:
                            prog = max(0.0, min(1.0, (now - start_t) / max(1e-6, windup_s)))
                        except Exception:
                            prog = 0.0
                        arc_map = world.components.setdefault('TelegraphArc', {})
                        arc_map[eid] = TelegraphArc(radius=hit_radius, arc_angle=arc_rad, direction=(ndx, ndy), color=rgba, offset=offset, progress=prog)
                    except Exception:
                        pass
                return
            # Determinar cooldown del slash desde config (fallback 1.0s)
            try:
                # Selección de spell por clase de monstruo
                arche_map = world.components.get('MonsterArchetype', {})
                mtype = None
                try:
                    mt = arche_map.get(eid)
                    mtype = (getattr(mt, 'type', None) or '').lower()
                except Exception:
                    mtype = None
                spell_id = 'hostile_slash'
                # Final Boss Barbol: usar slash gigante dedicado
                if isinstance(mtype, str) and mtype.startswith('final_boss_barbol'):
                    spell_id = 'boss_barbol_slash'
                elif mtype in ('barbol_oscuro', 'oscuro', 'dark'):
                    spell_id = 'hostile_slash_dark'
                elif mtype in ('barbol_morado', 'morado', 'purple'):
                    spell_id = 'hostile_slash_purple'
                elif mtype in ('barbol_boss', 'boss'):
                    spell_id = 'hostile_slash_red'
                elif mtype in ('barbol_cyan', 'cyan'):
                    spell_id = 'hostile_slash_cyan'
                elif mtype in ('barbol_gris', 'gris', 'gray', 'grey'):
                    spell_id = 'hostile_slash_gray'
                elif mtype in ('barbol_gigante', 'gigante', 'giant'):
                    spell_id = 'hostile_slash_giant'
                # Preferir spell_id, fallback a hostile_slash, luego 'slash'
                cfg = SPELLS.get(spell_id) or SPELLS.get('hostile_slash') or SPELLS.get('slash')
                cd_secs = float(cfg.get('cooldown_duration', 1.0)) if cfg else 1.0
            except Exception:
                cd_secs = 1.0
            if (cd is None) or (now >= cd.next_time):
                # Preparar spawn_meta para que el slash apunte al jugador y no rote con el mouse
                try:
                    # Limpiar telegraph al ejecutar el ataque
                    try:
                        world.components.get('TelegraphArc', {}).pop(eid, None)
                    except Exception:
                        pass
                    try:
                        world.components.get('WindupOutline', {}).pop(eid, None)
                    except Exception:
                        pass
                    spawn_meta = {
                        'target_eid': int(world.player_entity),
                        'rotate_with_owner': False,
                    }
                    resolver = SPELL_RESOLVERS.get('slash')
                    if resolver is not None:
                        resolver.resolve(world, eid, spawn_meta, cfg, None)
                    # Tras disparar, si es jefe final, bloquear movimiento por la duración del ataque
                    try:
                        if is_final_boss:
                            dur = 0.5
                            try:
                                dur = float(world.components['NPCState'][eid].fsm.context.get('attack_duration', 0.5))
                            except Exception:
                                dur = 0.5
                            world.components['NPCState'][eid].fsm.context['lock_move_until'] = float(now + max(0.0, dur))
                            world.components['NPCState'][eid].fsm.context['attack_fired'] = True
                            world.components['Velocity'][eid] = Velocity(0, 0)
                    except Exception:
                        pass
                except Exception:
                    pass
                # reset cooldown según el hechizo
                cd_map[eid] = NPCAttackCooldown(next_time=now + cd_secs)
                # Reiniciar el timestamp de inicio de ataque para exigir wind-up en el siguiente golpe
                try:
                    fsm = world.components['NPCState'][eid].fsm
                    fsm.context['attack_start'] = float(now)
                except Exception:
                    pass
            return
        # Fuera de rango
        # Para el jefe final: no interrumpir el wind-up ni el golpe; terminar el ataque y luego permitir chase
        try:
            arche_map = world.components.get('MonsterArchetype', {})
            mt = arche_map.get(eid)
            mtype = (getattr(mt, 'type', None) or '').lower() if mt else None
            is_final_boss = bool(isinstance(mtype, str) and mtype.startswith('final_boss_barbol'))
        except Exception:
            is_final_boss = False
        if is_final_boss:
            now = time.time()
            try:
                fsm = world.components['NPCState'][eid].fsm
                start_t = float(fsm.context.get('attack_start') or now)
                windup_s = float(fsm.context.get('attack_windup_s', 1.0))
                lock_until = float(fsm.context.get('lock_move_until', 0.0) or 0.0)
            except Exception:
                start_t = now
                windup_s = 1.0
                lock_until = 0.0
            # 1) Si sigue en wind-up: mostrar telegraph y no salir del estado
            if now - start_t < windup_s:
                try:
                    import math
                    # Seleccionar spell id como en la ruta normal
                    spell_id = 'boss_barbol_slash'
                    cfg = SPELLS.get(spell_id) or SPELLS.get('hostile_slash') or SPELLS.get('slash')
                    hit_radius = float(cfg.get('hit_radius', 0.0)) if cfg else 0.0
                    if hit_radius <= 0.0:
                        hit_radius = float(cfg.get('radius', 0.0)) if cfg else 0.0
                    arc_deg = float(cfg.get('hit_arc_degrees', 0.0)) if cfg else 0.0
                    if arc_deg <= 0.0:
                        arc_deg = float(cfg.get('arc_range_degrees', 0.0)) if cfg else 0.0
                    arc_rad = math.radians(arc_deg)
                    mag = (dx*dx + dy*dy) ** 0.5
                    ndx, ndy = (dx / mag, dy / mag) if mag > 1e-6 else (1.0, 0.0)
                    col = cfg.get('telegraph_color', None) if cfg else None
                    if not (isinstance(col, (list, tuple)) and len(col) >= 3):
                        col = cfg.get('color', None) if cfg else None
                    if not (isinstance(col, (list, tuple)) and len(col) >= 3):
                        col = [255, 230, 150]
                    a = int(cfg.get('telegraph_alpha', 90)) if cfg else 90
                    a = max(0, min(255, a))
                    rgba = (int(col[0]), int(col[1]), int(col[2]), a)
                    offset = float(cfg.get('offset', 0.0)) if cfg else 0.0
                    try:
                        prog = max(0.0, min(1.0, (now - start_t) / max(1e-6, windup_s)))
                    except Exception:
                        prog = 0.0
                    arc_map = world.components.setdefault('TelegraphArc', {})
                    arc_map[eid] = TelegraphArc(radius=hit_radius, arc_angle=arc_rad, direction=(ndx, ndy), color=rgba, offset=offset, progress=prog)
                except Exception:
                    pass
                # Mantener inmóvil
                world.components['Velocity'][eid] = Velocity(0, 0)
                world.components.get('ChaseTarget', {}).pop(eid, None)
                return
            # 2) Si terminó wind-up: disparar aunque esté fuera de rango, y bloquear hasta terminar
            try:
                # Preparar spell cfg y cooldown
                cfg = SPELLS.get('boss_barbol_slash') or SPELLS.get('hostile_slash') or SPELLS.get('slash')
                cd_secs = float(cfg.get('cooldown_duration', 1.0)) if cfg else 1.0
                # Limpiar telegraph si existe
                try:
                    world.components.get('TelegraphArc', {}).pop(eid, None)
                except Exception:
                    pass
                try:
                    world.components.get('WindupOutline', {}).pop(eid, None)
                except Exception:
                    pass
                spawn_meta = {
                    'target_eid': int(world.player_entity),
                    'rotate_with_owner': False,
                }
                resolver = SPELL_RESOLVERS.get('slash')
                if resolver is not None:
                    resolver.resolve(world, eid, spawn_meta, cfg, None)
                # Establecer lock de movimiento por duración del ataque
                try:
                    dur = float(world.components['NPCState'][eid].fsm.context.get('attack_duration', 0.5))
                except Exception:
                    dur = 0.5
                world.components['NPCState'][eid].fsm.context['lock_move_until'] = float(now + max(0.0, dur))
                world.components['NPCState'][eid].fsm.context['attack_fired'] = True
                world.components['Velocity'][eid] = Velocity(0, 0)
                cd_map = world.components.setdefault('NPCAttackCooldown', {})
                cd_map[eid] = NPCAttackCooldown(next_time=now + cd_secs)
                # Reiniciar attack_start para siguientes ciclos
                try:
                    world.components['NPCState'][eid].fsm.context['attack_start'] = float(now)
                except Exception:
                    pass
            except Exception:
                pass
            # 3) Si sigue bloqueado tras disparar, permanecer en AttackState
            try:
                if now < float(world.components['NPCState'][eid].fsm.context.get('lock_move_until', 0.0) or 0.0):
                    world.components['Velocity'][eid] = Velocity(0, 0)
                    world.components.get('ChaseTarget', {}).pop(eid, None)
                    return
            except Exception:
                pass
            # 4) Terminó todo el ciclo: ahora sí permitir cambio a Chase
            try:
                world.components.get('TelegraphArc', {}).pop(eid, None)
            except Exception:
                pass
            from roguelike_game.ecs.systems.fsm.states.monster.chase_state import ChaseState
            world.components['NPCState'][eid].fsm.change_state(ChaseState(), entity)
            return

        # Comportamiento por defecto para el resto de NPCs: limpiar y cambiar a Chase
        try:
            world.components.get('TelegraphArc', {}).pop(eid, None)
        except Exception:
            pass
        try:
            world.components.get('WindupOutline', {}).pop(eid, None)
        except Exception:
            pass
        from roguelike_game.ecs.systems.fsm.states.monster.chase_state import ChaseState
        world.components['NPCState'][eid].fsm.change_state(ChaseState(), entity)

    def exit(self, entity):
        # Limpiar animación de ataque y remover target de persecución
        world = entity.world
        world.components.get('ChaseTarget', {}).pop(entity.id, None)
        # Resetear velocidad al salir de AttackState
        try:
            world.components.get('TelegraphArc', {}).pop(entity.id, None)
        except Exception:
            pass
        try:
            world.components.get('WindupOutline', {}).pop(entity.id, None)
        except Exception:
            pass