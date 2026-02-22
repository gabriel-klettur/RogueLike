"""
Module: melee_combat_system.py
Contains the MeleeCombatSystem which resolves melee attack intents
and applies damage to targets based on attacker stats, weapon bonuses,
and target defense.
"""
from roguelike_game.ecs.components.combat.combat_stats import CombatStats
from roguelike_engine.utils.benchmark.benchmark import benchmark
from roguelike_game.ecs.components.combat.last_attacker import LastAttacker
from roguelike_game.ecs.utils.position_utils import compute_entity_center
from roguelike_game.ecs.components.core.identity import Faction
from roguelike_game.ecs.components.combat.dying_tag import DyingTag
from roguelike_game.ecs.utils.health_utils import is_neutral
import time
 
 
class MeleeCombatSystem:
    """
    Sistema que procesa eventos de WantsToMelee y aplica daño.
    
    Para cada intención de ataque:
      1. Recupera estadísticas de atacante y objetivo.
      2. Calcula daño = max(0, ataque + bonus_arma - defensa).
      3. Reduce los puntos de vida del objetivo.
      4. Elimina el componente WantsToMelee para limpiar el evento.      
    """
 
    def __init__(self, perf_log):
        self.perf_log = perf_log
    
    def update(self, world, camera=None):
        """
        Recorre todos los eventos WantsToMelee registrados en el mundo,
        aplica el daño calculado y luego purga cada evento para evitar
        procesarlo de nuevo en el siguiente ciclo.
        """
        # Iterar sobre una copia de los items para poder eliminar sobre la marcha
        for eid, intent in list(world.components['WantsToMelee'].items()):
            # Inmunidad para neutrales: saltar completamente el daño y limpiar el evento
            try:
                if is_neutral(world, intent.target):
                    del world.components['WantsToMelee'][eid]
                    continue
            except Exception:
                pass
            # Obtener estadísticas de atacante y defensor
            attacker_stats: CombatStats = world.components['CombatStats'][intent.attacker]
            defender_stats: CombatStats = world.components['CombatStats'][intent.target]
            # Friendly-fire filter: evitar daño entre monstruos de la misma facción
            try:
                monsters = world.components.get('MonsterArchetype', {})
                if (intent.attacker in monsters) and (intent.target in monsters):
                    # Bypass if attacker has AllowFriendlyFire enabled (provocation)
                    aff = world.components.get('AllowFriendlyFire', {}).get(intent.attacker)
                    allow_ff = bool(getattr(aff, 'enabled', False))
                    owner_idt = world.components.get('Identity', {}).get(intent.attacker)
                    target_idt = world.components.get('Identity', {}).get(intent.target)
                    if (not allow_ff) and owner_idt and target_idt and owner_idt.faction == target_idt.faction:
                        # Limpiar el evento y saltar daño
                        del world.components['WantsToMelee'][eid]
                        continue
            except Exception:
                pass
            
            # Bonus de arma si existe
            weapon_comp = world.components.get('MeleeWeapon', {}).get(intent.attacker)
            weapon_bonus = weapon_comp.damage if weapon_comp else 0
            
            # Cálculo de daño neto (no negativo)
            raw_damage = attacker_stats.power + weapon_bonus - defender_stats.defense
            damage = max(0, raw_damage)
            # One-shot si atacante es el jugador y godmode está activo
            is_player_attacker = intent.attacker in world.components.get('PlayerTagComponent', {})
            try:
                gm_attacker = bool(getattr(getattr(world, 'state', None), 'godmode', False)) and is_player_attacker
            except Exception:
                gm_attacker = False
            if gm_attacker:
                # Ajustar daño para reducir HP del objetivo a 0 en este impacto
                damage = max(damage, defender_stats.current_hp)
            
            # Inmortalidad del jugador en godmode
            is_player_target = intent.target in world.components.get('PlayerTagComponent', {})
            try:
                godmode = bool(getattr(getattr(world, 'state', None), 'godmode', False)) and is_player_target
            except Exception:
                godmode = False
            
            # Aplicar daño al objetivo (omitir si es jugador y godmode)
            if not godmode:
                defender_stats.current_hp -= damage
                # Registrar último atacante del objetivo
                world.components.setdefault('LastAttacker', {})[intent.target] = LastAttacker(intent.attacker, time.time())
            
            # NPC recibe daño de jugador -> publicar evento OnHit y posible OnDeath
            if intent.attacker in world.components.get('PlayerTagComponent', {}):
                target_eid = intent.target
                # Skip post-mortem effects if target is already dying
                if target_eid in world.components.get('DeathTimer', {}) or target_eid in world.components.get('DyingTag', {}):
                    del world.components['WantsToMelee'][eid]
                    continue
                # determinar dirección de daño usando centros de sprite si están disponibles
                pos_map = world.components.get('Position', {})
                spr_map = world.components.get('Sprite', {})
                scl_map = world.components.get('Scale', {})
                attacker_pos = pos_map.get(intent.attacker)
                defender_pos = pos_map.get(target_eid)
                try:
                    if attacker_pos and defender_pos:
                        aspr = spr_map.get(intent.attacker)
                        dspr = spr_map.get(target_eid)
                        ascl = scl_map.get(intent.attacker)
                        dscl = scl_map.get(target_eid)
                        if aspr:
                            ac = compute_entity_center(attacker_pos, aspr, ascl)
                            ax = float(ac.x)
                        else:
                            ax = float(attacker_pos.x)
                        if dspr:
                            dc = compute_entity_center(defender_pos, dspr, dscl)
                            dx_center = float(dc.x)
                        else:
                            dx_center = float(defender_pos.x)
                        from_left = ax < dx_center
                    else:
                        from_left = False
                except Exception:
                    from_left = bool(attacker_pos and defender_pos and (attacker_pos.x < defender_pos.x))
                qmap = world.components.setdefault('FSMEventQueue', {})
                q = qmap.setdefault(target_eid, [])
                q.append({"type": "OnHit", "from_left": from_left})
                if not godmode and defender_stats.current_hp <= 0:
                    # Encolar evento de muerte para la FSM del objetivo
                    q.append({"type": "OnDeath"})
                    # Marcar entidad como en proceso de muerte para evitar duplicados
                    try:
                        world.components.setdefault('DyingTag', {})[target_eid] = DyingTag()
                    except Exception:
                        pass
                    # Publicar evento de combo de tipo 'kill' para el atacante jugador
                    try:
                        combo_q_kill = world.components.setdefault('ComboEventQueue', [])
                        combo_q_kill.append({
                            'type': 'kill',
                            'attacker': intent.attacker,
                            'target': target_eid
                        })
                    except Exception:
                        pass
                # Publicar evento de COMBO para el atacante (jugador)
                if not godmode:
                    combo_q = world.components.setdefault('ComboEventQueue', [])
                    combo_q.append({
                        'attacker': intent.attacker,
                        'target': target_eid,
                        'damage': float(damage),
                        'source': 'melee'
                    })
                # Actualizar HUD de objetivo (centrado arriba)
                try:
                    hud = world.components.setdefault('TargetHUD', {})
                    hud['target_eid'] = int(target_eid)
                    hud['last_hit_time'] = float(time.time())
                    hud.setdefault('ttl_s', 3.0)
                except Exception:
                    pass
            # Jugador recibe daño de NPC/u otro -> publicar evento OnHit y posible OnDeath
            elif is_player_target:
                if not godmode:
                    target_eid = intent.target
                    # Skip post-mortem effects if target is already dying
                    if target_eid in world.components.get('DeathTimer', {}) or target_eid in world.components.get('DyingTag', {}):
                        del world.components['WantsToMelee'][eid]
                        continue
                    # determinar dirección de daño usando centros
                    pos_map = world.components.get('Position', {})
                    spr_map = world.components.get('Sprite', {})
                    scl_map = world.components.get('Scale', {})
                    attacker_pos = pos_map.get(intent.attacker)
                    defender_pos = pos_map.get(target_eid)
                    try:
                        if attacker_pos and defender_pos:
                            aspr = spr_map.get(intent.attacker)
                            dspr = spr_map.get(target_eid)
                            ascl = scl_map.get(intent.attacker)
                            dscl = scl_map.get(target_eid)
                            if aspr:
                                ac = compute_entity_center(attacker_pos, aspr, ascl)
                                ax = float(ac.x)
                            else:
                                ax = float(attacker_pos.x)
                            if dspr:
                                dc = compute_entity_center(defender_pos, dspr, dscl)
                                dx_center = float(dc.x)
                            else:
                                dx_center = float(defender_pos.x)
                            from_left = ax < dx_center
                        else:
                            from_left = False
                    except Exception:
                        from_left = bool(attacker_pos and defender_pos and (attacker_pos.x < defender_pos.x))
                    qmap = world.components.setdefault('FSMEventQueue', {})
                    q = qmap.setdefault(target_eid, [])
                    q.append({"type": "OnHit", "from_left": from_left})
                    if defender_stats.current_hp <= 0:
                        # Encolar evento de muerte para la FSM del objetivo (jugador)
                        q.append({"type": "OnDeath"})
                        # Marcar entidad como en proceso de muerte para evitar duplicados
                        try:
                            world.components.setdefault('DyingTag', {})[target_eid] = DyingTag()
                        except Exception:
                            pass
                    # Romper combo del jugador al recibir daño
                    combo_q = world.components.setdefault('ComboEventQueue', [])
                    combo_q.append({'type': 'break', 'entity': target_eid})
            
            # (Opcional) Aquí podrías disparar efectos secundarios,
            # p.ej. animaciones, sonidos o eventos de muerte si HP ≤ 0
            
            # Limpia el evento para no reprocesarlo
            del world.components['WantsToMelee'][eid]