from __future__ import annotations

import logging
from dataclasses import dataclass, field
from typing import Any, Callable, Optional, TypeVar

import pygame

from ..types import EditorCtx

T = TypeVar("T")


def _ensure_mouse_position(event: pygame.event.Event) -> tuple[int, int]:
    """Return the integer mouse position from the pygame event."""
    pos = getattr(event, "pos", (0, 0))
    if not isinstance(pos, (tuple, list)) or len(pos) != 2:
        return 0, 0
    return int(pos[0]), int(pos[1])


@dataclass
class LeftClickContext:
    """Aggregates shared collaborators and helpers for LMB handling."""

    handler: Any
    editor_ctx: EditorCtx
    event: pygame.event.Event
    logger: logging.Logger = field(default_factory=lambda: logging.getLogger(__name__))
    mx: int = field(init=False)
    my: int = field(init=False)
    world: Any = field(init=False)
    camera: Any = field(init=False)
    controller: Any = field(init=False)
    model: Any = field(init=False)
    instance_properties: Any = field(init=False, default=None)
    view: Any = field(init=False, default=None)

    def __post_init__(self) -> None:
        self.mx, self.my = _ensure_mouse_position(self.event)
        self.world = getattr(self.editor_ctx, "world", None)
        self.camera = getattr(self.editor_ctx, "camera", None)
        self.controller = getattr(self.handler, "controller", None)
        self.model = getattr(self.handler, "model", None)
        self.instance_properties = getattr(self.controller, "instance_properties", None)
        self.view = getattr(self.controller, "view", None)

    # ---------------------------------------------------------------------
    # Generic helpers
    # ---------------------------------------------------------------------
    def guard(self, description: str, func: Callable[[], T], *, default: Optional[T] = None) -> Optional[T]:
        """Execute *func* swallowing exceptions and logging contextual debug."""
        try:
            return func()
        except Exception:  # noqa: BLE001 - defensive guard around editor tooling
            self.logger.debug("[SpawnerEditor] %s failed", description, exc_info=True)
            return default

    def set_attr(self, target: Any, attr: str, value: Any, description: str) -> bool:
        if target is None:
            return False
        return bool(
            self.guard(
                f"setattr:{description}",
                lambda: (setattr(target, attr, value), True)[1],
                default=False,
            )
        )

    def get_attr(self, target: Any, attr: str, default: Any = None, *, description: str = "getattr") -> Any:
        if target is None:
            return default
        return self.guard(description, lambda: getattr(target, attr), default=default)

    def log_debug(self, message: str, *args: object) -> None:
        self.guard("logger.debug", lambda: self.logger.debug(message, *args), default=None)

    # ------------------------------------------------------------------
    # World / controller helpers
    # ------------------------------------------------------------------
    def world_state_set(self, attr: str, value: Any) -> None:
        state = getattr(self.world, "state", None)
        self.set_attr(state, attr, value, f"world.state.{attr}")

    def refresh_instances_from_disk(self) -> None:
        panel = getattr(self.controller, "spawner_instances", None)
        self.guard(
            "refresh instances panel",
            lambda: panel.refresh_from_disk() if panel is not None else None,
        )

    # ------------------------------------------------------------------
    # Visual building helpers
    # ------------------------------------------------------------------
    @property
    def visuals(self) -> Any:
        return getattr(self.instance_properties, "visuals", None)

    @property
    def visuals_model(self) -> Any:
        visuals = self.visuals
        return getattr(visuals, "model", None)

    def get_selected_building_id(self) -> Optional[int]:
        model = self.visuals_model
        value = self.get_attr(model, "selected_building_id", default=None, description="selected_building_id")
        return int(value) if value is not None else None

    def set_selected_building_id(self, value: Optional[int]) -> None:
        model = self.visuals_model
        if value is None:
            self.set_attr(model, "selected_building_id", None, "clear selected_building_id")
        else:
            self.set_attr(model, "selected_building_id", int(value), "set selected_building_id")

    def pick_building_under_cursor(self) -> Any:
        visuals = self.visuals
        if visuals is None:
            return None
        return self.guard(
            "pick visual building under cursor",
            lambda: visuals.pick_visual_building_under_cursor(self.mx, self.my),
        )

    def is_same_instance(self, building: Any) -> bool:
        if building is None:
            return False
        sel_inst = self.guard(
            "selected_instance",
            lambda: getattr(getattr(self.instance_properties, "model", None), "selected_instance", None),
        )
        if not isinstance(sel_inst, dict):
            return True
        sid = sel_inst.get("id")
        if sid is None:
            return True
        ob_sid = self.guard(
            "building spawner instance id",
            lambda: getattr(building, "spawner_instance_id", getattr(building, "spawn_id", "")),
        )
        return str(ob_sid) == str(sid)

    def is_building_hidden(self, building: Any) -> bool:
        if building is None:
            return False
        return bool(self.guard("building.editor_hidden", lambda: getattr(building, "editor_hidden", False), default=False))

    def clear_building_selection(self) -> None:
        self.set_selected_building_id(None)


StepFn = Callable[[LeftClickContext], bool]
