"""
Sistema que permite a NPCs castear hechizos automáticamente en intervalos fijos.
Crea WantsToCastSpell para que SpellCastingSystem lo procese.
"""
from __future__ import annotations

import time
import random
import logging

from roguelike_game.ecs.components.ai.wants_to_cast import WantsToCastSpell
from roguelike_engine.config.config_tiles import TILE_SIZE
from roguelike_game.ecs.utils.position_utils import compute_entity_center
from roguelike_game.ecs.components.combat.cast_outline import CastOutline
from roguelike_game.ecs.components.status.stun_component import StunComponent

logger = logging.getLogger(__name__)


class AutoCastSystem:
    def __init__(self, perf_log=None):
        self.perf_log = perf_log

    def update(self, world, camera=None):
        comps = world.components
        auto_map = comps.get('AutoCastComponent', {})
        if not auto_map:
            return
        player_eid = getattr(world, 'player_entity', None)
        if player_eid is None:
            return
        now = time.time()
        # Iterar sobre entidades con componente de autocast
        for eid, ac in list(auto_map.items()):
            try:
                if not getattr(ac, 'enabled', True):
                    continue
                wants = comps.setdefault('WantsToCastSpell', {})
                # No iniciar otros casts si el NPC está ejecutando un slash (wind-up o swing activo)
                try:
                    # Señal visual del wind-up
                    if eid in comps.get('TelegraphArc', {}):
                        continue
                    # Slash activo: emisor de partículas presente en el caster durante la vida del golpe
                    if eid in comps.get('SlashEmitterComponent', {}):
                        continue
                    # Alternativa: usar contexto del NPCState para ventana de wind-up
                    npc_state = comps.get('NPCState', {}).get(eid)
                    if npc_state is not None:
                        fsm = getattr(npc_state, 'fsm', None)
                        if fsm is not None:
                            now_ts = time.time()
                            ctx = getattr(fsm, 'context', {}) or {}
                            start_t = float(ctx.get('attack_start') or 0.0)
                            windup_s = float(ctx.get('attack_windup_s', 0.0) or 0.0)
                            if windup_s > 0.0 and start_t > 0.0 and (now_ts - start_t) < windup_s:
                                continue
                except Exception:
                    pass

                # Si estamos canalizando, mantener outline, frenar y castear al terminar
                chan = getattr(ac, 'active_channel', None)
                if isinstance(chan, dict):
                    # Congelar movimiento durante canalizado
                    try:
                        vel = comps.get('Velocity', {}).get(eid)
                        if vel is not None:
                            vel.vx = 0.0
                            vel.vy = 0.0
                    except Exception:
                        pass
                    # ¿Finalizó el canalizado?
                    st = float(chan.get('start_ts', now) or now)
                    dur = float(chan.get('duration', 0.0) or 0.0)
                    if now >= st + dur:
                        # Remover outline visual
                        comps.get('CastOutline', {}).pop(eid, None)
                        # Resolver target
                        target = str(chan.get('target', 'player'))
                        spawn_pos = None
                        if target == 'player' and player_eid is not None:
                            # Calcular centro del player
                            pos_map = comps.get('Position', {})
                            spr_map = comps.get('Sprite', {})
                            scl_map = comps.get('Scale', {})
                            dpos = pos_map.get(player_eid)
                            if dpos is not None:
                                try:
                                    dspr = spr_map.get(player_eid)
                                    dscl = scl_map.get(player_eid)
                                    if dspr:
                                        dc = compute_entity_center(dpos, dspr, dscl)
                                        spawn_pos = (float(dc.x), float(dc.y))
                                    else:
                                        spawn_pos = (float(dpos.x), float(dpos.y))
                                except Exception:
                                    spawn_pos = (float(dpos.x), float(dpos.y))
                        # Crear intención de cast si no hay una ya pendiente para este eid
                        if eid not in wants:
                            # Combinar meta de la entrada con spawn_pos y target
                            entry = chan.get('entry') or {}
                            meta = dict(entry.get('meta') or {})
                            # Flatten nested meta (builder may wrap under 'meta')
                            try:
                                inner = meta.get('meta')
                                if isinstance(inner, dict):
                                    meta.update(inner)
                                    meta.pop('meta', None)
                            except Exception:
                                pass
                            # Explicitar target para que el resolver lo pueda honrar en ausencia de spawn_pos
                            meta.setdefault('target', target)
                            if spawn_pos:
                                meta['spawn_pos'] = spawn_pos
                            wants[eid] = WantsToCastSpell(caster=eid, spell=str(chan.get('spell')), meta=meta)
                        # Programar siguiente periodo aleatorio (si corresponde) para la entrada usada
                        try:
                            entry = chan.get('entry')
                            if isinstance(entry, dict):
                                entry['last_cast_ts'] = now
                                mn = entry.get('min_period_s')
                                mx = entry.get('max_period_s')
                                if isinstance(mn, (int, float)) and isinstance(mx, (int, float)) and mx >= mn:
                                    entry['next_ready_ts'] = now + random.uniform(float(mn), float(mx))
                                else:
                                    per = float(entry.get('period_s', 2.0) or 2.0)
                                    entry['next_ready_ts'] = now + per
                        except Exception:
                            pass
                        # Limpiar canalizado activo
                        ac.active_channel = None
                    # Durante canalizado no iniciar otros
                    continue

                # Modo multi-entrada: entries
                entries = getattr(ac, 'entries', None)
                if isinstance(entries, list) and entries:
                    # Elegir la primera entrada lista (orden estable)
                    for entry in entries:
                        try:
                            # Calcular next_ready_ts si no existe
                            nrt = entry.get('next_ready_ts')
                            if nrt is None:
                                # Inicial: permitir initial_delay_s (entry o entry.meta) para escalonar arranques
                                meta_obj = entry.get('meta') or {}
                                init_delay = entry.get('initial_delay_s')
                                if not isinstance(init_delay, (int, float)):
                                    init_delay = meta_obj.get('initial_delay_s')
                                if isinstance(init_delay, (int, float)) and init_delay >= 0.0:
                                    entry['next_ready_ts'] = now + float(init_delay)
                                    continue  # no arrancar en el mismo frame
                                # Si no hay initial_delay_s, usar rango aleatorio si hay min/max; si no, period_s
                                mn = entry.get('min_period_s')
                                mx = entry.get('max_period_s')
                                if isinstance(mn, (int, float)) and isinstance(mx, (int, float)) and mx >= mn:
                                    entry['next_ready_ts'] = now + random.uniform(float(mn), float(mx))
                                else:
                                    per = float(entry.get('period_s', 2.0) or 2.0)
                                    entry['next_ready_ts'] = now + per
                                continue  # no arrancar en el mismo frame de inicialización
                            # Trigger por pérdida de vida: si se cruza un múltiplo de on_hp_loss_step, forzar disponibilidad inmediata
                            try:
                                meta_obj = entry.get('meta') or {}
                                step_val = entry.get('on_hp_loss_step')
                                if not isinstance(step_val, (int, float)):
                                    step_val = meta_obj.get('on_hp_loss_step')
                                if isinstance(step_val, (int, float)) and step_val > 0:
                                    h = comps.get('Health', {}).get(eid)
                                    if h is not None:
                                        max_hp = int(getattr(h, 'max_hp', 0))
                                        cur_hp = int(getattr(h, 'current_hp', 0))
                                        lost = max(0, max_hp - cur_hp)
                                        bucket = int(lost // int(step_val))
                                        prev_bucket = int(entry.get('_hp_loss_bucket', -1))
                                        if bucket > prev_bucket:
                                            entry['_hp_loss_bucket'] = bucket
                                            entry['next_ready_ts'] = now  # permitir cast inmediato
                                            nrt = now
                            except Exception:
                                pass
                            if now < float(nrt):
                                continue
                            # Gateo por aggro
                            # Verificar vida del jugador
                            ph = comps.get('Health', {}).get(player_eid)
                            player_dead = (ph is None) or (ph.current_hp <= 0)
                            has_death_timer = player_eid in comps.get('DeathTimer', {})
                            if player_dead or has_death_timer:
                                continue
                            # Gateo por distancia por-entrada (min/max en px o en tiles)
                            try:
                                pos_map = comps.get('Position', {})
                                spr_map = comps.get('Sprite', {})
                                scl_map = comps.get('Scale', {})
                                apos = pos_map.get(eid)
                                dpos = pos_map.get(player_eid)
                                if apos is not None and dpos is not None:
                                    try:
                                        aspr = spr_map.get(eid)
                                        ascl = scl_map.get(eid)
                                        if aspr:
                                            acxcy = compute_entity_center(apos, aspr, ascl)
                                            ax, ay = float(acxcy.x), float(acxcy.y)
                                        else:
                                            ax, ay = float(apos.x), float(apos.y)
                                        dspr = spr_map.get(player_eid)
                                        dscl = scl_map.get(player_eid)
                                        if dspr:
                                            dcxcy = compute_entity_center(dpos, dspr, dscl)
                                            px, py = float(dcxcy.x), float(dcxcy.y)
                                        else:
                                            px, py = float(dpos.x), float(dpos.y)
                                    except Exception:
                                        ax, ay = float(apos.x), float(apos.y)
                                        px, py = float(dpos.x), float(dpos.y)
                                    dx = px - ax
                                    dy = py - ay
                                    dist_sq = dx*dx + dy*dy
                                    # Umbrales en píxeles (permitir en entry o en entry.meta)
                                    meta_obj = entry.get('meta') or {}
                                    min_px = entry.get('min_distance')
                                    if not isinstance(min_px, (int, float)):
                                        min_px = meta_obj.get('min_distance')
                                    max_px = entry.get('max_distance')
                                    if not isinstance(max_px, (int, float)):
                                        max_px = meta_obj.get('max_distance')
                                    min_v = float(min_px) if isinstance(min_px, (int, float)) else 0.0
                                    max_candidates = []
                                    if isinstance(max_px, (int, float)):
                                        max_candidates.append(float(max_px))
                                    # Umbrales en tiles (entry o entry.meta)
                                    mn_tiles = entry.get('min_distance_tiles')
                                    if not isinstance(mn_tiles, (int, float)):
                                        mn_tiles = meta_obj.get('min_distance_tiles')
                                    mx_tiles = entry.get('max_distance_tiles')
                                    if not isinstance(mx_tiles, (int, float)):
                                        mx_tiles = meta_obj.get('max_distance_tiles')
                                    if isinstance(mn_tiles, (int, float)):
                                        try:
                                            min_v = max(min_v, float(mn_tiles) * float(TILE_SIZE))
                                        except Exception:
                                            min_v = max(min_v, float(mn_tiles))
                                    if isinstance(mx_tiles, (int, float)):
                                        try:
                                            max_candidates.append(float(mx_tiles) * float(TILE_SIZE))
                                        except Exception:
                                            max_candidates.append(float(mx_tiles))
                                    max_v = 0.0
                                    for c in max_candidates:
                                        if isinstance(c, (int, float)) and c > 0:
                                            max_v = c if (max_v <= 0 or c < max_v) else max_v
                                    if min_v > 0.0 and dist_sq < (min_v * min_v):
                                        continue
                                    if max_v > 0.0 and dist_sq > (max_v * max_v):
                                        continue
                            except Exception:
                                pass
                            # Iniciar canalizado o castear inmediato si channel_s <= 0
                            spell = str(entry.get('spell'))
                            chan_s = float(entry.get('channel_s', 0.0) or 0.0)
                            color_from = tuple(entry.get('wire_from') or (0, 128, 255))
                            color_to = tuple(entry.get('wire_to') or (0, 255, 0))
                            target = str(entry.get('target', 'player'))
                            if chan_s > 1e-6:
                                ac.active_channel = {
                                    'spell': spell,
                                    'start_ts': now,
                                    'duration': chan_s,
                                    'wire_from': color_from,
                                    'wire_to': color_to,
                                    'target': target,
                                    'entry': entry,
                                }
                                # Adjuntar CastOutline para renderizar el wire azul->verde
                                comps.setdefault('CastOutline', {})[eid] = CastOutline.create(duration=chan_s, color_from=color_from, color_to=color_to, start_time=now)
                                # Aplicar stun al caster para inmovilizar durante el canalizado
                                try:
                                    comps.setdefault('StunComponent', {})[eid] = StunComponent.create(chan_s)
                                except Exception:
                                    pass
                                break
                            else:
                                # Cast inmediato sin canalizado
                                if eid in wants:
                                    break
                                # Resolver spawn_pos si target es player
                                spawn_pos = None
                                if target == 'player' and player_eid is not None:
                                    pos_map = comps.get('Position', {})
                                    spr_map = comps.get('Sprite', {})
                                    scl_map = comps.get('Scale', {})
                                    dpos = pos_map.get(player_eid)
                                    if dpos is not None:
                                        try:
                                            dspr = spr_map.get(player_eid)
                                            dscl = scl_map.get(player_eid)
                                            if dspr:
                                                dc = compute_entity_center(dpos, dspr, dscl)
                                                spawn_pos = (float(dc.x), float(dc.y))
                                            else:
                                                spawn_pos = (float(dpos.x), float(dpos.y))
                                        except Exception:
                                            spawn_pos = (float(dpos.x), float(dpos.y))
                                meta = dict(entry.get('meta') or {})
                                # Flatten nested meta (builder may wrap under 'meta')
                                try:
                                    inner = meta.get('meta')
                                    if isinstance(inner, dict):
                                        meta.update(inner)
                                        meta.pop('meta', None)
                                except Exception:
                                    pass
                                # Explicitar el mismo target evaluado para esta entrada
                                meta.setdefault('target', target)
                                if spawn_pos:
                                    meta['spawn_pos'] = spawn_pos
                                wants[eid] = WantsToCastSpell(caster=eid, spell=spell, meta=meta)
                                # Programar siguiente disponibilidad
                                mn = entry.get('min_period_s')
                                mx = entry.get('max_period_s')
                                if isinstance(mn, (int, float)) and isinstance(mx, (int, float)) and mx >= mn:
                                    entry['next_ready_ts'] = now + random.uniform(float(mn), float(mx))
                                else:
                                    per = float(entry.get('period_s', 2.0) or 2.0)
                                    entry['next_ready_ts'] = now + per
                                break
                        except Exception:
                            continue
                    # Siguiente entidad
                    continue

                # Legado: un solo autocast fijo
                last_ts = float(getattr(ac, 'last_cast_ts', 0.0) or 0.0)
                period = max(0.0, float(getattr(ac, 'period_s', 2.0) or 2.0))
                if now - last_ts < period:
                    continue
                spell = getattr(ac, 'spell', None) or 'fireball'
                # Evitar duplicar intención si ya existe para este eid
                if eid in wants:
                    continue
                # Gateo por aggro: sólo autocastear si el jugador está dentro del radio de aggro del NPC
                try:
                    # Verificar vida del jugador
                    ph = comps.get('Health', {}).get(player_eid)
                    player_dead = (ph is None) or (ph.current_hp <= 0)
                    has_death_timer = player_eid in comps.get('DeathTimer', {})
                    if player_dead or has_death_timer:
                        continue
                    # Calcular distancia por centros si hay Sprite/Scale; fallback a Position
                    pos_map = comps.get('Position', {})
                    spr_map = comps.get('Sprite', {})
                    scl_map = comps.get('Scale', {})
                    apos = pos_map.get(eid)
                    dpos = pos_map.get(player_eid)
                    if not apos or not dpos:
                        continue
                    try:
                        aspr = spr_map.get(eid)
                        ascl = scl_map.get(eid)
                        if aspr:
                            acxcy = compute_entity_center(apos, aspr, ascl)
                            ax, ay = float(acxcy.x), float(acxcy.y)
                        else:
                            ax, ay = float(apos.x), float(apos.y)
                        dspr = spr_map.get(player_eid)
                        dscl = scl_map.get(player_eid)
                        if dspr:
                            dcxcy = compute_entity_center(dpos, dspr, dscl)
                            px, py = float(dcxcy.x), float(dcxcy.y)
                        else:
                            px, py = float(dpos.x), float(dpos.y)
                    except Exception:
                        ax, ay = float(apos.x), float(apos.y)
                        px, py = float(dpos.x), float(dpos.y)
                    dx = px - ax
                    dy = py - ay
                    dist_sq = dx*dx + dy*dy
                    aggro_cmp = comps.get('AggroRange', {}).get(eid)
                    if aggro_cmp is not None:
                        radius_px = float(getattr(aggro_cmp, 'radius', 0)) * float(TILE_SIZE)
                        if radius_px > 0 and dist_sq > radius_px * radius_px:
                            # Fuera de área de aggro: no crear intención de cast
                            continue
                except Exception:
                    # En caso de error al calcular, no bloquear la lógica previa
                    pass
                wants[eid] = WantsToCastSpell(caster=eid, spell=spell, meta=getattr(ac, 'meta', None))
                ac.last_cast_ts = now
            except Exception:
                logger.exception("[AutoCastSystem] Error processing eid=%s", eid)
                continue
