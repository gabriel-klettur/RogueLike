import json
import tempfile
from typing import Dict, List

import pygame
import pytest

from roguelike_engine.diagnostics.overlay.model import DiagnosticsOverlayModel
from roguelike_engine.diagnostics.overlay.view import DiagnosticsOverlayView
from roguelike_engine.diagnostics.overlay.services.lines_builder import build_lines


@pytest.fixture(autouse=True)
def _init_pygame():
    # Initialize only the font subsystem (no window needed)
    pygame.font.init()
    yield
    pygame.font.quit()


def _perf_log_example(n: int = 5) -> Dict[str, List[float]]:
    # Keys include numeric dotted ids to exercise grouping
    perf: Dict[str, List[float]] = {}
    for i in range(1, n + 1):
        perf[f"{i}.Task {i}"] = [0.001 * i] * 60  # seconds per sample
    return perf


def test_build_lines_contains_fps_and_frame_time():
    perf_log = _perf_log_example(3)
    model = DiagnosticsOverlayModel(perf_log=perf_log)
    view = DiagnosticsOverlayView()

    class _Clock:
        def get_fps(self) -> float:
            return 50.0

    class _State:
        clock = _Clock()

    lines, label_w, value_w, levels, colors = build_lines(model, view, state=_State())

    labels = [l for (l, _r) in lines]
    assert any(l.strip().startswith("FPS:") for l in labels)
    assert any(l.strip().startswith("FrameTime:") for l in labels)
    assert label_w > 0 and value_w > 0
    assert len(levels) == len(lines)
    assert len(colors) == len(lines)
