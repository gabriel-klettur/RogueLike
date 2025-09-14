"""
Module: melee_combat_system.py
Contains the MeleeCombatSystem which resolves melee attack intents
and applies damage to targets based on attacker stats, weapon bonuses,
and target defense.
"""
from roguelike_game.ecs.components.combat.combat_stats import CombatStats
from roguelike_engine.utils.benchmark import benchmark
from roguelike_game.ecs.components.combat.last_attacker import LastAttacker
from roguelike_game.ecs.utils.position_utils import compute_entity_center
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
            # Obtener estadísticas de atacante y defensor
            attacker_stats: CombatStats = world.components['CombatStats'][intent.attacker]
            defender_stats: CombatStats = world.components['CombatStats'][intent.target]
            
            # Bonus de arma si existe
            weapon_comp = world.components['MeleeWeapon'].get(intent.attacker)
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
                    q.append({"type": "OnDeath"})
                    # Evento de kill para combo basado en muertes
                    combo_q = world.components.setdefault('ComboEventQueue', [])
                    combo_q.append({'type': 'kill', 'entity': intent.attacker, 'target': target_eid})
                    world.components.setdefault('ComboKillCounted', set()).add(target_eid)
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
                        q.append({"type": "OnDeath"})
                    # Romper combo del jugador al recibir daño
                    combo_q = world.components.setdefault('ComboEventQueue', [])
                    combo_q.append({'type': 'break', 'entity': target_eid})
            
            # (Opcional) Aquí podrías disparar efectos secundarios,
            # p.ej. animaciones, sonidos o eventos de muerte si HP ≤ 0
            
            # Limpia el evento para no reprocesarlo
            del world.components['WantsToMelee'][eid]