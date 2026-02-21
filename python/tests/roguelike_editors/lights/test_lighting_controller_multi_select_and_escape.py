import types
import sys
from types import SimpleNamespace
import pygame
import pytest

import roguelike_editors.lighting.lighting_controller as ctrl_mod


class DummyLM:
    def __init__(self):
        self.enabled = True

    def set_enabled(self, v: bool):
        self.enabled = bool(v)

    def should_render(self):
        return True


@pytest.fixture()
def fake_env(monkeypatch):
    # Fake lighting module
    lm = DummyLM()
    lighting_pkg = types.ModuleType("roguelike_engine.rendering.lighting")
    lighting_pkg.get_global_lighting = lambda: lm
    monkeypatch.setitem(sys.modules, "roguelike_engine.rendering.lighting", lighting_pkg)

    # Map settings
    monkeypatch.setattr(ctrl_mod, "global_map_settings", SimpleNamespace(zone_offsets={"z0": (0, 0)}), raising=True)
    # Preset radius large to make hit test easy
    monkeypatch.setattr(ctrl_mod, "_load_presets", lambda: {"torch": {"radius": 50}}, raising=True)
    # Two instances
    monkeypatch.setattr(
        ctrl_mod,
        "load_light_instances",
        lambda: [
            {"id": 1, "preset_id": "torch", "zone": "z0", "rel_x": 100, "rel_y": 200},
            {"id": 2, "preset_id": "torch", "zone": "z0", "rel_x": 160, "rel_y": 200},
        ],
        raising=True,
    )
    return lm


def test_ctrl_multi_select_and_escape_clears(fake_env):
    c = ctrl_mod.LightingEditorController(font=None)
    c.model.visible = True
    c.game = SimpleNamespace(camera=SimpleNamespace(zoom=1.0, offset_x=0.0, offset_y=0.0))

    # Click first light
    down1 = SimpleNamespace(type=pygame.MOUSEBUTTONDOWN, button=1, pos=(100, 200), mod=0)
    c.handle_event(down1)
    assert c.model.selected_light_ids == {1}

    # CTRL+Click second light to multi-select
    down2 = SimpleNamespace(type=pygame.MOUSEBUTTONDOWN, button=1, pos=(160, 200), mod=pygame.KMOD_CTRL)
    c.handle_event(down2)
    assert c.model.selected_light_ids == {1, 2}

    # Press ESC clears selection and stops dragging
    esc = SimpleNamespace(type=pygame.KEYDOWN, key=pygame.K_ESCAPE)
    c.handle_event(esc)
    assert c.model.selected_light_id is None and not c.model.selected_light_ids
    assert c.model._dragging_inst is False
