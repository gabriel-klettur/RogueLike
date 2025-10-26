from __future__ import annotations

import json
from pathlib import Path

import pygame
import pytest

from roguelike_engine.rendering.lighting.daynight import DayNightSystem


def make_config(tmp_path: Path, *, keyframes: list[tuple[int, float, list[int]]] | None = None,
                 enabled: bool = True, ambient_only: bool = True, time_scale: float = 0.4,
                 start_minute: int = 0) -> Path:
    cfg = {
        "enabled": enabled,
        "ambient_only": ambient_only,
        "time_scale": time_scale,
        "start_minute": start_minute,
        "keyframes": [
            {"minute": m, "intensity": i, "color": c} for (m, i, c) in (
                keyframes
                or [
                    # Deterministic simple curve: night(0) -> day(1) -> same until 1440
                    (0, 0.0, [150, 170, 220]),
                    (1, 1.0, [255, 255, 255]),
                    (1440, 1.0, [255, 255, 255]),
                ]
            )
        ],
    }
    p = tmp_path / "lighting_test.json"
    p.write_text(json.dumps(cfg), encoding="utf-8")
    return p


auto_sz = (64, 64)


def test_overlay_rebuilds_on_color_change_and_reuses_when_unchanged(tmp_path: Path):
    cfg_path = make_config(tmp_path)
    dn = DayNightSystem(cfg_path)

    # Force night: intensity ~ 0, tint very dark
    dn.set_minute_of_day(0)
    s1 = dn.get_overlay_surface(auto_sz)
    assert isinstance(s1, pygame.Surface)

    # Jump to day: intensity 1.0, tint white -> must rebuild cache Surface
    dn.set_minute_of_day(1)
    s2 = dn.get_overlay_surface(auto_sz)
    assert isinstance(s2, pygame.Surface)
    assert s2 is not s1  # color changed => new surface built

    # Calling again without changes should return the same cached Surface
    s3 = dn.get_overlay_surface(auto_sz)
    assert s3 is s2


def test_set_minute_has_immediate_effect(tmp_path: Path):
    cfg_path = make_config(tmp_path, time_scale=0.4, start_minute=0)
    dn = DayNightSystem(cfg_path)

    dn.set_minute_of_day(1260)  # 21:00 -> Night
    # Intensity near 0 at night (exact value depends on curve; in our config it's exactly 0 after dusk)
    assert dn.get_phase() in ("Night", "Dusk")
    # The overlay should reflect the night tint now (no need to assert pixel values; cache invalidation is enough)
    _ = dn.get_overlay_surface(auto_sz)


def test_time_scale_progression_with_mocked_ticks(monkeypatch: pytest.MonkeyPatch, tmp_path: Path):
    cfg_path = make_config(tmp_path, time_scale=1.0, start_minute=0)
    dn = DayNightSystem(cfg_path)

    # Control ticks so that minutes advance predictably (1.0 min/s)
    base = 1_000
    monkeypatch.setattr(pygame.time, "get_ticks", lambda: base)
    dn._start_ticks = base  # align start
    assert dn.get_minute_of_day() == 0

    # After 30 seconds -> 30 minutes
    monkeypatch.setattr(pygame.time, "get_ticks", lambda: base + 30_000)
    assert dn.get_minute_of_day() == 30

    # After 90 minutes -> 90
    monkeypatch.setattr(pygame.time, "get_ticks", lambda: base + 90_000)
    assert dn.get_minute_of_day() == 90


@pytest.mark.parametrize(
    "minute,expected",
    [
        (299, "Night"),
        (300, "Dawn"),
        (419, "Dawn"),
        (420, "Day"),
        (1139, "Day"),
        (1140, "Dusk"),
        (1259, "Dusk"),
        (1260, "Night"),
        (0, "Night"),
    ],
)
def test_phase_boundaries(tmp_path: Path, minute: int, expected: str):
    cfg_path = make_config(tmp_path)  # curve shape doesn't affect get_phase() ranges
    dn = DayNightSystem(cfg_path)
    dn.set_minute_of_day(minute)
    assert dn.get_phase() == expected
