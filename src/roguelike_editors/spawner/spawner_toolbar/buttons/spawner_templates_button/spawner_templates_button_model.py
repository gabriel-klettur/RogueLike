from __future__ import annotations

from dataclasses import dataclass
from ...spawner_toolbar_model import TOOL_SPAWNER_TEMPLATES, ICON_PATHS


@dataclass
class SpawnerTemplatesButtonModel:
    tool_id: str = TOOL_SPAWNER_TEMPLATES
    icon_path: str = ICON_PATHS.get(TOOL_SPAWNER_TEMPLATES, "")


__all__ = ["SpawnerTemplatesButtonModel"]
