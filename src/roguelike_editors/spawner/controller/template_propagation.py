from __future__ import annotations

"""Propagación de cambios de templates a entidades vivas del ECS."""
from typing import Any
from roguelike_engine.config.map_config import global_map_settings
from roguelike_editors.spawner.services.persistence import find_instance_in_json


def on_template_saved(controller: Any, updated_template: dict) -> None:
    """Propaga cambios de un template (e.g. trigger/policy/waves) a entidades del ECS."""
    try:
        world = getattr(getattr(controller, 'game', None), 'ecs', None)
        world = getattr(world, 'ecs_world', None)
        if not world:
            return
        t_id = str(updated_template.get('id')) if isinstance(updated_template, dict) else None
        if not t_id:
            return
        comps = getattr(world, 'components', {})
        if 'SpawnerConfig' not in comps:
            return
        for eid in world.get_entities_with('SpawnerConfig', 'SpawnerState'):
            try:
                cfg = comps['SpawnerConfig'][eid]
            except Exception:
                continue
            try:
                if str(getattr(cfg, 'template_id', '')) != t_id:
                    continue
            except Exception:
                continue
            # Tile local para el lookup en JSON
            try:
                zone = getattr(cfg, 'zone', 'lobby')
                off_x, off_y = global_map_settings.zone_offsets.get(zone, (0, 0))
                gx, gy = getattr(cfg, 'anchor_tile', (0, 0))
                local_tile = (int(gx - off_x), int(gy - off_y))
            except Exception:
                zone = getattr(cfg, 'zone', 'lobby')
                local_tile = (0, 0)
            # Overrides de instancia
            try:
                _, _, overrides = find_instance_in_json(t_id, zone, local_tile)
            except Exception:
                overrides = None
            # Construcción del config fusionado
            trigger = dict(updated_template.get('trigger', {})) if isinstance(updated_template, dict) else {}
            policy = dict(updated_template.get('policy', {})) if isinstance(updated_template, dict) else {}
            waves = list(updated_template.get('waves', [])) if isinstance(updated_template, dict) else []
            spawner_type = (
                updated_template.get('spawner_type', getattr(cfg, 'spawner_type', 'invisible'))
                if isinstance(updated_template, dict)
                else getattr(cfg, 'spawner_type', 'invisible')
            )
            if isinstance(overrides, dict):
                for k, v in overrides.items():
                    try:
                        if k.startswith('trigger.'):
                            trigger[k.split('.', 1)[1]] = v
                        elif k.startswith('policy.'):
                            policy[k.split('.', 1)[1]] = v
                        elif k == 'spawner_type':
                            spawner_type = v
                    except Exception:
                        continue
            # Recalcular cooldown
            try:
                from roguelike_engine.config import config as _cfg
                fps = getattr(_cfg, 'FPS', 60)
                cooldown_s = float(policy.get('cooldown_s', 10.0))
                cooldown_frames = int(round(cooldown_s * fps))
            except Exception:
                cooldown_frames = getattr(cfg, 'cooldown_frames', 0)
            # Aplicar in-place
            try:
                cfg.trigger = trigger
                cfg.policy = policy
                if isinstance(waves, list):
                    cfg.waves = waves
                cfg.spawner_type = spawner_type
                cfg.cooldown_frames = cooldown_frames
            except Exception:
                pass
    except Exception:
        pass
