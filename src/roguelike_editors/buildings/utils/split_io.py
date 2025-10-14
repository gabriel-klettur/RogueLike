from __future__ import annotations
import json
import os
import logging
from typing import Any, Dict, List, Tuple
from roguelike_engine.config.config import (
    BUILDINGS_TEMPLATES_PATH,
    BUILDINGS_INSTANCES_PATH,
)

logger = logging.getLogger(__name__)


def read_templates() -> list[dict]:
    try:
        with open(BUILDINGS_TEMPLATES_PATH, "r", encoding="utf-8-sig") as tf:
            templates_raw = json.load(tf) or []
        return templates_raw if isinstance(templates_raw, list) else []
    except FileNotFoundError:
        logger.warning("[Buildings] Templates file not found: %s", BUILDINGS_TEMPLATES_PATH)
        return []
    except Exception as e:
        logger.error("[Buildings] Error reading templates: %s", e)
        return []


def read_instances() -> list[dict]:
    try:
        with open(BUILDINGS_INSTANCES_PATH, "r", encoding="utf-8-sig") as inf:
            instances_raw = json.load(inf) or []
        return instances_raw if isinstance(instances_raw, list) else []
    except FileNotFoundError:
        return []
    except Exception as e:
        logger.error("[Buildings] Error reading instances: %s", e)
        return []
