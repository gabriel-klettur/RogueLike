import types
import sys
from types import SimpleNamespace
import pygame
import pytest

import roguelike_editors.lighting.lighting_controller as ctrl_mod


class DummyLight:
    def __init__(self, lid: str):
        self.id = lid
        self.x = 0.0
        self.y = 0.0


class DummyLM:
    def __init__(self):
        self._lights = [DummyLight("persist:5")]
        self.enabled = True

    def set_enabled(self, v: bool):
        self.enabled = bool(v)

    def should_render(self):
        return True


class DummyGame:
    def __init__(self):
        self.camera = SimpleNamespace(zoom=1.0, offset_x=0.0, offset_y=0.0)


@pytest.fixture()
def fake_env(monkeypatch):
    # Map settings with origin zone
    gms = SimpleNamespace(zone_offsets={"z0": (0, 0)})
    monkeypatch.setattr(ctrl_mod, "global_map_settings", gms, raising=True)

    # Fake lighting modules
    lm = DummyLM()
    lighting_pkg = types.ModuleType("roguelike_engine.rendering.lighting")
    lighting_pkg.get_global_lighting = lambda: lm
    monkeypatch.setitem(sys.modules, "roguelike_engine.rendering.lighting", lighting_pkg)

    # Presets: torch radius=20
    monkeypatch.setattr(ctrl_mod, "_load_presets", lambda: {"torch": {"radius": 20}}, raising=True)
    # Instances: one at screen/world (100,200)
    monkeypatch.setattr(
        ctrl_mod,
        "load_light_instances",
        lambda: [{"id": 5, "preset_id": "torch", "zone": "z0", "rel_x": 100, "rel_y": 200}],
        raising=True,
    )
    return lm


def test_select_then_drag_and_release_updates_service_and_moves_live_light(fake_env, monkeypatch):
    c = ctrl_mod.LightingEditorController(font=None)
    c.model.visible = True
    c.game = DummyGame()
    # Click on the circle to select and start dragging (single selection)
    down = SimpleNamespace(type=pygame.MOUSEBUTTONDOWN, button=1, pos=(100, 200))
    c.handle_event(down)
    assert c.model.selected_light_id == 5
    assert c.model._dragging_inst is True

    # Drag motion moves preview and live light
    move = SimpleNamespace(type=pygame.MOUSEMOTION, pos=(110, 210))
    c.handle_event(move)
    lm = fake_env
    light = lm._lights[0]
    assert (light.x, light.y) == (110.0, 210.0)

    # Mouse up persists the new position via service
    calls = []
    monkeypatch.setattr(ctrl_mod, "update_instance_position", lambda *a, **k: calls.append((a, k)), raising=True)
    up = SimpleNamespace(type=pygame.MOUSEBUTTONUP, pos=(110, 210))
    c.handle_event(up)
    assert calls, "update_instance_position was not called"
    args, kwargs = calls[0]
    assert int(args[0]) == 5 and float(args[1]) == 110.0 and float(args[2]) == 210.0
    assert c.model._dragging_inst is False
