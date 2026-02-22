from roguelike_game.config.spells_config import SPELLS_VERSION
from roguelike_game.ecs.systems.combat.spells.spells_apply import apply_aura_cfg, log_aura_state

from .base import BaseSpellResolver


class AuraResolver(BaseSpellResolver):
    def resolve(self, world, caster, spawn_meta, cfg, camera):
        # Aplica un aura al caster
        from roguelike_game.ecs.components.abilities.aura_component import AuraComponent
        radius = cfg.get('radius', 100)
        buff = cfg.get('buff', {})
        duration = cfg.get('duration', 5.0)
        spell_key = spawn_meta.get('spell') if isinstance(spawn_meta, dict) else None
        comp = AuraComponent(radius, buff, duration, spell_key=spell_key or '', last_refresh_version=SPELLS_VERSION)
        # Hacer que Aura use los parámetros comunes de partículas aplanados desde vfx.particles.*
        try:
            apply_aura_cfg(comp, cfg)
        except Exception:
            pass
        # Debug logging unificado
        log_aura_state("[AuraResolver]", caster, comp, spell_key=spell_key, version=SPELLS_VERSION)
        world.components.setdefault('AuraComponent', {})[caster] = comp
