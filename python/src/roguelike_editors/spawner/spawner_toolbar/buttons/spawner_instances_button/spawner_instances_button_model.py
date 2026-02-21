from __future__ import annotations

from dataclasses import dataclass
from ...spawner_toolbar_model import TOOL_SPAWNER_INSTANCES, ICON_PATHS


@dataclass
class SpawnerInstancesButtonModel:
    tool_id: str = TOOL_SPAWNER_INSTANCES
    icon_path: str = ICON_PATHS.get(TOOL_SPAWNER_INSTANCES, "")


__all__ = ["SpawnerInstancesButtonModel"]
