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


def test_max_lines_truncates_and_reports_hidden_count():
    perf_log: Dict[str, List[float]] = {"1.A": [0.001] * 60}
    model = DiagnosticsOverlayModel(perf_log=perf_log)
    # Disable paging to make max_lines apply inside build_lines
    model.paging_enabled = False
    model.max_lines = 5
    view = DiagnosticsOverlayView()

    # Provide many extra lines to exceed limit
    extra = [f"Extra {i}" for i in range(20)]
    lines, label_w, value_w, levels, colors = build_lines(model, view, extra_lines=extra)

    # Expect last line to be an ellipsis with hidden count
    assert len(lines) == model.max_lines
    last_left, last_right = lines[-1]
    assert last_left == "..."
    # Hidden count must be a positive integer in right field
    assert "líneas ocultas" in last_right
