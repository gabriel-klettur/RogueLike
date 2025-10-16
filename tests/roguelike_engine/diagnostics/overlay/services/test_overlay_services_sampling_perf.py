import time
from typing import Dict, List

import pygame
import pytest

from roguelike_engine.diagnostics.overlay.model import DiagnosticsOverlayModel
from roguelike_engine.diagnostics.overlay.view import DiagnosticsOverlayView
from roguelike_engine.diagnostics.overlay.services.lines_builder import build_lines


@pytest.fixture(autouse=True)
def _init_pygame():
    pygame.font.init()
    yield
    pygame.font.quit()


def test_build_lines_scales_reasonably_with_many_keys():
    # Build a larger perf_log to simulate load
    perf: Dict[str, List[float]] = {}
    for i in range(1, 300):
        perf[f"{i}.System {i}"] = [0.001 * (i % 7 + 1)] * 60
    model = DiagnosticsOverlayModel(perf_log=perf)
    view = DiagnosticsOverlayView()

    t0 = time.perf_counter()
    lines, label_w, value_w, levels, colors = build_lines(model, view)
    dt = time.perf_counter() - t0

    # Sanity: some lines produced and reasonable time budget
    assert len(lines) > 0
    assert label_w > 0 and value_w > 0
    assert dt < 2.0  # generous budget to avoid flakes on CI
