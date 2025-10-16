import io
import builtins
import json
import os
import tempfile
from typing import Dict, List

import pygame
import pytest

from roguelike_engine.diagnostics.overlay.model import DiagnosticsOverlayModel
from roguelike_engine.diagnostics.overlay.view import DiagnosticsOverlayView
from roguelike_engine.diagnostics.overlay.services.lines_builder import build_lines
from roguelike_engine.diagnostics.overlay.services.persistence import save_overlay_state, load_overlay_state, get_state_file_path


@pytest.fixture(autouse=True)
def _init_pygame():
    pygame.font.init()
    yield
    pygame.font.quit()


def test_build_lines_handles_none_inputs_and_duplicates():
    perf_log: Dict[str, List[float]] = {
        "1.Update": [0.002] * 60,
        "2.Render": [0.004] * 60,
    }
    model = DiagnosticsOverlayModel(perf_log=perf_log)
    view = DiagnosticsOverlayView()

    # Force initially_collapsed to trigger auto-collapse logic safely
    model.initially_collapsed = True
    lines, label_w, value_w, levels, colors = build_lines(model, view, state=None, camera=None, map_manager=None, entities=None, extra_lines=["FPS:", "FrameTime:"])

    # No duplicates should be added for provided extra lines matching built-in labels
    labels = [l.strip() for (l, _r) in lines]
    assert labels.count("FPS:") <= 1
    assert labels.count("FrameTime:") <= 1
    # Collapsed groups should be set on first build
    assert isinstance(model.collapsed_groups, set)


def test_save_overlay_state_swallows_io_errors(monkeypatch):
    # Simulate open() raising for write path
    def _boom(*args, **kwargs):
        raise OSError("disk full")

    with tempfile.TemporaryDirectory() as tmp:
        monkeypatch.setattr("builtins.open", _boom)
        # Should not raise
        save_overlay_state(["1"], base_path=tmp)


def test_load_overlay_state_invalid_json_returns_empty():
    with tempfile.TemporaryDirectory() as tmp:
        fp = get_state_file_path(base_path=tmp)
        os.makedirs(os.path.dirname(fp), exist_ok=True)
        with open(fp, "w", encoding="utf-8") as f:
            f.write("{ invalid json }")
        assert load_overlay_state(base_path=tmp) == []
