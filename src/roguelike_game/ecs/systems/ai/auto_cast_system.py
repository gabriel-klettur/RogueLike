"""
Sistema que permite a NPCs castear hechizos automáticamente en intervalos fijos.
Crea WantsToCastSpell para que SpellCastingSystem lo procese.
"""
from __future__ import annotations

import time
import logging

from roguelike_game.ecs.components.ai.wants_to_cast import WantsToCastSpell

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
                wants[eid] = WantsToCastSpell(caster=eid, spell=spell)
                ac.last_cast_ts = now
            except Exception:
                logger.exception("[AutoCastSystem] Error processing eid=%s", eid)
                continue
