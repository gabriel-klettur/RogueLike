from __future__ import annotations

"""Utilities to stagger the activation of persistent lights."""

from dataclasses import dataclass, field
from typing import Iterable, List

import pygame

from .light_types import Light


@dataclass
class StaggerScheduler:
    """Enable persistent lights one at a time to avoid hitches."""

    interval_ms: int = 3000
    _targets: List[Light] = field(default_factory=list, init=False)
    _cursor: int = field(default=0, init=False)
    _next_tick: int = field(default=0, init=False)

    def __post_init__(self) -> None:
        self._next_tick = pygame.time.get_ticks()

    def reset(self) -> None:
        self._targets.clear()
        self._cursor = 0
        self._next_tick = pygame.time.get_ticks()

    def configure(self, interval_ms: int) -> None:
        self.interval_ms = max(0, int(interval_ms))

    def populate(self, lights: Iterable[Light], order_desc: bool) -> None:
        items: List[tuple[int, Light]] = []
        for light in lights:
            try:
                identifier = getattr(light, "id", None)
                if isinstance(identifier, str) and identifier.startswith("persist:"):
                    number = int(identifier.split(":", 1)[1])
                    items.append((number, light))
            except Exception:
                continue
        items.sort(key=lambda item: item[0], reverse=order_desc)
        self._targets = [light for _, light in items]
        self._cursor = 0
        self._next_tick = pygame.time.get_ticks()

    def disable_targets(self) -> None:
        for light in self._targets:
            try:
                light.enabled = False
            except Exception:
                continue
        self.reset()

    def tick(self) -> None:
        if not self._targets:
            return
        now = pygame.time.get_ticks()
        interval = self.interval_ms
        while self._cursor < len(self._targets) and now >= self._next_tick:
            light = self._targets[self._cursor]
            try:
                if not getattr(light, "enabled", False):
                    light.enabled = True
            except Exception:
                pass
            finally:
                self._cursor += 1
                self._next_tick = now if interval == 0 else now + interval

    @property
    def done(self) -> bool:
        return self._cursor >= len(self._targets)

    def needs_population(self) -> bool:
        return not self._targets
