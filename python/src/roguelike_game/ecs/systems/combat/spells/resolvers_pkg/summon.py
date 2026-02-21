from typing import Any, Dict
from roguelike_game.ecs.systems.combat.spells.resolvers_pkg.base import BaseSpellResolver
from roguelike_game.ecs.systems.combat.spells.resolvers_pkg.utils import get_entity_center, mouse_world
from roguelike_game.factories.registry import get_factory
from roguelike_engine.config.config_tiles import TILE_SIZE
from roguelike_game.ecs.components.abilities.summoned_unit_component import SummonedUnitComponent


class SummonResolver(BaseSpellResolver):
    def resolve(self, world, caster, spawn_meta: Dict[str, Any], cfg, camera):
        effect = {}
        try:
            effect = getattr(cfg, 'extra', {}).get('effect', {}) or {}
        except Exception:
            effect = {}
        template_id = str(effect.get('template_id', ''))
        duration = float(effect.get('duration', cfg.get('duration', 0.0) or 0.0))
        count = int(effect.get('count', 1) or 1)
        spread = float(effect.get('spread_radius', 0.0) or 0.0)
        if not template_id:
            return
        # Base spawn position: if caster is the player, use mouse world position; otherwise caster center
        try:
            player_eid = getattr(world, 'player_entity', None)
        except Exception:
            player_eid = None
        if player_eid is not None and caster == player_eid:
            cx, cy = mouse_world(camera)
        else:
            cx, cy = get_entity_center(world, caster)
        base_tx = int(round(cx / float(TILE_SIZE)))
        base_ty = int(round(cy / float(TILE_SIZE)))
        created = []
        for i in range(max(1, count)):
            ox = 0.0
            oy = 0.0
            if spread > 0.0:
                ang = (i / max(1, count)) * 6.283185307179586
                ox = spread * 0.5 * float(__import__('math').cos(ang))
                oy = spread * 0.5 * float(__import__('math').sin(ang))
            tx = int(round((cx + ox) / float(TILE_SIZE)))
            ty = int(round((cy + oy) / float(TILE_SIZE)))
            eid = get_factory("monster").create(world, tile_x=tx, tile_y=ty, monster_type=template_id, instance_id=None)
            if isinstance(eid, int):
                world.components.setdefault('SummonedUnitComponent', {})[eid] = SummonedUnitComponent(owner=caster, duration=duration)
                created.append(eid)
        return created
