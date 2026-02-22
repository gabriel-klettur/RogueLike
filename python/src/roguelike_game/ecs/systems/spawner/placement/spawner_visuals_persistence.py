from __future__ import annotations

import json
import os
import logging

from roguelike_engine.config import config

logger = logging.getLogger(__name__)


def persist_spawner_instance_visuals(inst_id: str | None, visuals: dict, ensure_visible_in_game: bool = True) -> None:
    if not inst_id:
        return
    base = config.DATA_DIR
    path = os.path.join(base, "spawners", "spawners_instances.json")
    try:
        with open(path, 'r', encoding='utf-8-sig') as f:
            data = json.load(f)
        if not isinstance(data, list):
            return
    except FileNotFoundError:
        return
    except Exception:
        return
    changed = False
    for i, e in enumerate(data):
        try:
            if str(e.get('id')) == str(inst_id):
                if e.get('visuals') != visuals:
                    e['visuals'] = visuals
                    changed = True
                if ensure_visible_in_game:
                    ov = dict(e.get('overrides') or {})
                    if not bool(ov.get('visible_in_game', False)):
                        ov['visible_in_game'] = True
                        e['overrides'] = ov
                        changed = True
                break
        except Exception:
            continue
    if changed:
        try:
            with open(path, 'w', encoding='utf-8') as f:
                json.dump(data, f, ensure_ascii=False, indent=4)
        except Exception:
            pass
