import logging
from typing import Any

from roguelike_game.factories.player.loader import (
    load_and_scale_sprites,
    extract_initial_frame,
    build_animator_map,
)
from roguelike_game.factories.monster.config import reload_monster_defs
from roguelike_game.factories.monster import cache as monster_cache

logger = logging.getLogger(__name__)


def update_player_assets(ecs_world: Any, ent_id: str) -> None:
    """Reload player assets and update Sprite/Animator components for all player entities of this class."""
    try:
        player_tags = ecs_world.components.get('PlayerTagComponent', {})
        sprites_comp = ecs_world.components.get('Sprite', {})
        animators = ecs_world.components.get('Animator', {})
        sprites_dict = load_and_scale_sprites(ent_id)
        initial_frame = extract_initial_frame(sprites_dict)
        anim_map = build_animator_map(sprites_dict)
        for eid, tag in player_tags.items():
            if tag.class_name == ent_id:
                if initial_frame and eid in sprites_comp:
                    img = initial_frame.copy() if hasattr(initial_frame, 'copy') else initial_frame
                    sprites_comp[eid].image = img
                if eid in animators:
                    animators[eid].animations = anim_map
        logger.debug(f"[ecs_update_service] Player ECS updated for class {ent_id}")
    except Exception as e:
        logger.error(f"[ecs_update_service][ERROR] Failed to update player ECS for {ent_id}: {e}")


def update_monster_assets(ecs_world: Any, ent_id: str) -> None:
    """Reload monster defs and caches, then update Sprite/Animator for matching entities."""
    try:
        reload_monster_defs()
        monster_cache._loaded_variants.discard(ent_id)
        monster_cache._SPRITE_SURFACES.pop(ent_id, None)
        monster_cache._DEATH_SURFACES.pop(ent_id, None)
        monster_cache.load_caches_for([ent_id])
        logger.debug(f"[ecs_update_service] Monster defs reloaded and cache reset for {ent_id}")
    except Exception as e:
        logger.warning(f"[ecs_update_service][WARN] Problem reloading monster caches for {ent_id}: {e}")

    # Update existing entities regardless of cache reload success
    try:
        idents = ecs_world.components.get('Identity', {})
        sprites = ecs_world.components.get('Sprite', {})
        animators = ecs_world.components.get('Animator', {})
        base_map = monster_cache._SPRITE_SURFACES.get(ent_id, {})
        for eid, identity in idents.items():
            if identity.name.lower() == ent_id:
                # Set a reasonable default image (down frame)
                down_surf = base_map.get('down')
                if down_surf and eid in sprites:
                    raw = down_surf.copy() if hasattr(down_surf, 'copy') else down_surf
                    sprites[eid].image = raw
                # Replace animations with single frames per state
                if eid in animators:
                    new_anims = {state: [surf.copy() if hasattr(surf, 'copy') else surf]
                                 for state, surf in base_map.items()}
                    animators[eid].animations = new_anims
        logger.debug(f"[ecs_update_service] Monster ECS updated for type {ent_id}")
    except Exception as e:
        logger.error(f"[ecs_update_service][ERROR] Failed to update monster ECS for {ent_id}: {e}")


def update_player_stats(ecs_world: Any, ent_id: str, key: str, value: Any) -> None:
    """Propagate player stat changes to ECS components."""
    try:
        player_tags = ecs_world.components.get('PlayerTagComponent', {})
        health_comps = ecs_world.components.get('Health', {})
        combat_comps = ecs_world.components.get('CombatStats', {})
        speed_comps = ecs_world.components.get('MovementSpeed', {})
        npc_states = ecs_world.components.get('NPCState', {})
        for eid, tag in player_tags.items():
            if tag.class_name == ent_id:
                if key == 'max_strength':
                    hc = health_comps.get(eid)
                    cc = combat_comps.get(eid)
                    if hc:
                        hc.max_hp = value
                        hc.current_hp = value
                    if cc:
                        cc.max_hp = value
                        cc.current_hp = value
                elif key == 'basic_attack':
                    cc = combat_comps.get(eid)
                    if cc:
                        cc.power = value
                elif key == 'basic_armor':
                    cc = combat_comps.get(eid)
                    if cc:
                        cc.defense = value
                elif key == 'basic_speed':
                    sc = speed_comps.get(eid)
                    if sc:
                        sc.speed = value
                elif key == 'attack_duration':
                    ns = npc_states.get(eid)
                    try:
                        if ns and hasattr(ns, 'fsm') and hasattr(ns.fsm, 'context'):
                            ns.fsm.context['attack_duration'] = float(value) if value is not None else None
                    except Exception:
                        pass
        logger.debug(f"[ecs_update_service] Player ECS stats updated for class {ent_id}")
    except Exception as e:
        logger.error(f"[ecs_update_service][ERROR] Failed to update player ECS stats for {ent_id}: {e}")


def update_monster_stats(ecs_world: Any, ent_id: str, key: str, value: Any) -> None:
    """Propagate monster stat changes to ECS components."""
    try:
        idents = ecs_world.components.get('Identity', {})
        health_comps = ecs_world.components.get('Health', {})
        combat_comps = ecs_world.components.get('CombatStats', {})
        speed_comps = ecs_world.components.get('MovementSpeed', {})
        npc_states = ecs_world.components.get('NPCState', {})
        for eid, identity in idents.items():
            if identity.name.lower() == ent_id:
                if key == 'hp':
                    hc = health_comps.get(eid)
                    cc = combat_comps.get(eid)
                    if hc:
                        hc.max_hp = value
                        hc.current_hp = value
                    if cc:
                        cc.max_hp = value
                        cc.current_hp = value
                elif key == 'power':
                    cc = combat_comps.get(eid)
                    if cc:
                        cc.power = value
                elif key == 'defense':
                    cc = combat_comps.get(eid)
                    if cc:
                        cc.defense = value
                elif key == 'speed':
                    sc = speed_comps.get(eid)
                    if sc:
                        sc.speed = float(value)
                elif key == 'damage_duration':
                    ns = npc_states.get(eid)
                    try:
                        if ns and hasattr(ns, 'fsm') and hasattr(ns.fsm, 'context'):
                            ns.fsm.context['attack_duration'] = float(value) if value is not None else None
                    except Exception:
                        pass
        logger.debug(f"[ecs_update_service] Monster ECS stats updated for type {ent_id}")
    except Exception as e:
        logger.error(f"[ecs_update_service][ERROR] Failed to update monster ECS stats for {ent_id}: {e}")
