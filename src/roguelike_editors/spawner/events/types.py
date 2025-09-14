from __future__ import annotations

from dataclasses import dataclass
from typing import Any, Optional
import logging


@dataclass
class EditorCtx:
    controller: Any
    model: Any
    game: Any
    world: Any
    camera: Any
    split_tool: Optional[Any]
    split_adapter: Optional[Any]
    logger: logging.Logger
