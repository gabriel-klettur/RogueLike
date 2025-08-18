"""
SpawnerPlacementSystem: loads spawner templates and instances from JSON and creates ECS entities
with SpawnerConfig + SpawnerState components.
"""
from __future__ import annotations

import json
import ast
import os
from typing import Any, Dict, List

from roguelike_engine.config import config
from roguelike_game.ecs.components.spawner.spawner_config import SpawnerConfig
from roguelike_game.ecs.components.spawner.spawner_state import SpawnerState
from roguelike_engine.config.map_config import global_map_settings


class SpawnerPlacementSystem:
    def __init__(self, perf_log=None):
        self.perf_log = perf_log
        self._loaded = False
        self._templates: Dict[str, Dict[str, Any]] = {}

    def _load_templates(self) -> Dict[str, Dict[str, Any]]:
        base = config.DATA_DIR
        path = os.path.join(base, "spawners", "spawners.json")
        try:
            with open(path, "r", encoding="utf-8") as f:
                data = json.load(f)
            # normalize list into dict by id
            if isinstance(data, list):
                return {t["id"]: t for t in data}
            return data or {}
        except FileNotFoundError:
            return {}

    def _load_instances(self) -> List[Dict[str, Any]]:
        base = config.DATA_DIR
        path = os.path.join(base, "spawners", "instances.json")
        try:
            with open(path, "r", encoding="utf-8") as f:
                data = json.load(f)
            return data if isinstance(data, list) else []
        except FileNotFoundError:
            return []

    def _resolve_config(self, tpl: Dict[str, Any], inst: Dict[str, Any]) -> SpawnerConfig:
        # template base
        trigger = dict(tpl.get("trigger", {}))
        policy = dict(tpl.get("policy", {}))
        # waves can be list or, if saved incorrectly, a string with JSON/Python-like content
        raw_waves = tpl.get("waves", [])
        waves: List[Dict[str, Any]] = []
        try:
            if isinstance(raw_waves, str):
                s = raw_waves.strip()
                parsed = None
                try:
                    parsed = json.loads(s)
                except Exception:
                    try:
                        parsed = ast.literal_eval(s)
                    except Exception:
                        parsed = None
                if isinstance(parsed, list):
                    waves = parsed
            elif isinstance(raw_waves, list):
                waves = raw_waves
        except Exception:
            waves = []
        # keep only dict entries
        waves = [w for w in waves if isinstance(w, dict)]
        spawner_type = tpl.get("spawner_type", "invisible")

        # apply overrides in dot-notation
        for key, value in inst.get("overrides", {}).items():
            if key.startswith("trigger."):
                trigger[key.split(".", 1)[1]] = value
            elif key.startswith("policy."):
                policy[key.split(".", 1)[1]] = value
            elif key == "spawner_type":
                spawner_type = value

        # derive cooldown in frames (60 FPS default)
        fps = getattr(config, "FPS", 60)
        cooldown_s = float(policy.get("cooldown_s", 10.0))
        cooldown_frames = int(round(cooldown_s * fps))

        # Convert zone-local tile -> global tile using zone offsets
        zone = inst.get("zone", "lobby")
        local_tx, local_ty = tuple(inst.get("tile", (0, 0)))
        off_x, off_y = global_map_settings.zone_offsets.get(zone, (0, 0))
        anchor_tile = (off_x + int(local_tx), off_y + int(local_ty))

        return SpawnerConfig(
            template_id=tpl.get("id", "unknown"),
            zone=zone,
            anchor_tile=anchor_tile,
            spawner_type=spawner_type,
            trigger=trigger,
            policy=policy,
            waves=waves,
            cooldown_frames=cooldown_frames,
        )

    def update(self, world, camera=None):
        # Only run once per map load
        if self._loaded:
            return
        self._loaded = True

        self._templates = self._load_templates()
        instances = self._load_instances()
        if not instances or not self._templates:
            return

        comps = world.components
        for inst in instances:
            tpl_id = inst.get("template_id")
            if not tpl_id or tpl_id not in self._templates:
                continue
            cfg = self._resolve_config(self._templates[tpl_id], inst)
            eid = world.create_entity()
            comps['SpawnerConfig'][eid] = cfg
            comps['SpawnerState'][eid] = SpawnerState()
