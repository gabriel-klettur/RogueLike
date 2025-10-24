"""
Sistema que permite a NPCs castear hechizos automáticamente en intervalos fijos.
Crea WantsToCastSpell para que SpellCastingSystem lo procese.
"""
from __future__ import annotations

import time
import logging

from roguelike_game.ecs.components.ai.wants_to_cast import WantsToCastSpell
from roguelike_engine.config.config_tiles import TILE_SIZE
from roguelike_game.ecs.utils.position_utils import compute_entity_center

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
                # Respetar periodo en segundos
                last_ts = float(getattr(ac, 'last_cast_ts', 0.0) or 0.0)
                period = max(0.0, float(getattr(ac, 'period_s', 2.0) or 2.0))
                if now - last_ts < period:
                    continue
                spell = getattr(ac, 'spell', None) or 'fireball'
                # Evitar duplicar intención si ya existe para este eid
                wants = comps.setdefault('WantsToCastSpell', {})
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
