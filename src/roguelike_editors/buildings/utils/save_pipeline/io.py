"""File system helpers for the split save pipeline."""
from __future__ import annotations

import json
import logging
import os
from typing import List

from .models import ExistingData, SavePaths

logger = logging.getLogger(__name__)


def ensure_directories(paths: SavePaths) -> None:
    """Create target directories if they do not exist."""

    for target in (paths.templates_path, paths.instances_path):
        directory = os.path.dirname(target)
        if directory:
            os.makedirs(directory, exist_ok=True)


def _read_json_list(path: str) -> List[dict]:
    if not os.path.exists(path):
        return []
    try:
        with open(path, "r", encoding="utf-8-sig") as handler:
            data = json.load(handler) or []
    except UnicodeError:
        with open(path, "r", encoding="utf-8") as handler:
            data = json.load(handler) or []
    except FileNotFoundError:
        return []
    except Exception as exc:  # pragma: no cover - defensive
        logger.error("[Buildings][Save] Error reading %s: %s", path, exc)
        return []
    return data if isinstance(data, list) else []


def load_existing(paths: SavePaths) -> ExistingData:
    """Load existing split files to preserve IDs before saving."""

    templates = _read_json_list(paths.templates_path)
    instances = _read_json_list(paths.instances_path)
    return ExistingData(templates=templates, instances=instances)


def write_templates(path: str, templates: List[dict]) -> None:
    with open(path, "w", encoding="utf-8") as handler:
        json.dump(templates, handler, indent=4)


def write_instances(path: str, instances: List[dict]) -> None:
    with open(path, "w", encoding="utf-8") as handler:
        json.dump(instances, handler, indent=4)
