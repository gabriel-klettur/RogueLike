import math
import time
import logging
from roguelike_game.config.spells_config import SPELLS, SPELLS_VERSION
from roguelike_game.ecs.systems.combat.spells.spells_apply import apply_aura_cfg, log_aura_state

from roguelike_game.ecs.components.abilities.aura_component import AuraComponent
from roguelike_game.ecs.components.transform.position import Position
from roguelike_game.ecs.components.combat.health import Health

logger = logging.getLogger(__name__)

class AuraSystem:
    """
    Sistema que procesa auras activas: curación y expiración.
    """
    def __init__(self, perf_log):
        self.perf_log = perf_log

    def update(self, world, camera=None):
        now = time.time()
        to_remove = []
        for caster, aura in list(world.components.get('AuraComponent', {}).items()):
            # Refrescar parámetros del aura si la configuración fue recargada
            if getattr(aura, 'last_refresh_version', -1) != SPELLS_VERSION:
                spell_key = getattr(aura, 'spell_key', '')
                cfg = SPELLS.get(spell_key) if spell_key else None
                if cfg:
                    try:
                        apply_aura_cfg(aura, cfg)
                    except Exception:
                        pass
                    aura.last_refresh_version = SPELLS_VERSION
                    log_aura_state("[AuraSystem] refreshed aura", caster, aura, spell_key=spell_key, version=SPELLS_VERSION)
            # Expiración del aura
            if now >= aura.start_time + aura.duration:
                to_remove.append(caster)
                continue

            # Aplicar curación por segundo
            last = getattr(aura, 'last_apply_time', aura.start_time)
            dt = now - last
            heal_rate = aura.buff.get('heal_per_second', 0)
            if heal_rate > 0 and dt > 0:
                pos_cmp = world.components['Position'][caster]
                cx, cy = pos_cmp.x, pos_cmp.y
                for eid in world.get_entities_with('Position', 'Health'):
                    tpos = world.components['Position'][eid]
                    if (tpos.x - cx)**2 + (tpos.y - cy)**2 <= aura.radius**2:
                        hp = world.components['Health'][eid]
                        hp.current_hp = min(hp.max_hp, hp.current_hp + heal_rate * dt)
                aura.last_apply_time = now
        # Eliminar auras expiradas
        for caster in to_remove:
            world.components['AuraComponent'].pop(caster, None)