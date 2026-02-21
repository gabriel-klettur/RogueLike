import types
import sys
import pygame
import pytest

import roguelike_editors.lighting.lighting_controller as ctrl_mod


class DummyLM:
    def __init__(self):
        self.removed = []

    def remove_by_id(self, lid: str):
        self.removed.append(lid)

    def set_enabled(self, v: bool):
        pass

    def should_render(self):
        return True


class DummyDN:
    def __init__(self):
        self.enabled = True


@pytest.fixture()
def fake_light_env(monkeypatch):
    lm = DummyLM()
    lighting_pkg = types.ModuleType("roguelike_engine.rendering.lighting")
    lighting_pkg.get_global_lighting = lambda: lm
    daynight_mod = types.ModuleType("roguelike_engine.rendering.lighting.daynight")
    daynight_mod.get_global_daynight = lambda: DummyDN()
    monkeypatch.setitem(sys.modules, "roguelike_engine.rendering.lighting", lighting_pkg)
    monkeypatch.setitem(sys.modules, "roguelike_engine.rendering.lighting.daynight", daynight_mod)
    return lm


def test_delete_selected_button_clears_and_removes(fake_light_env, monkeypatch):
    c = ctrl_mod.LightingEditorController(font=None)
    st = c.model
    st.visible = True
    st._btn_delete_selected = pygame.Rect(10, 10, 120, 22)
    # Seed selection
    st.selected_light_ids = {3, 7, 9}
    st.selected_light_id = 9
    # Make delete_instances return len(ids)
    monkeypatch.setattr(ctrl_mod, "delete_instances", lambda ids: len(ids), raising=True)

    c._on_click((15, 15))
    # Selection cleared
    assert st.selected_light_id is None and not st.selected_light_ids
    # Removed from LM
    lm = fake_light_env
    assert set(lm.removed) == {"persist:3", "persist:7", "persist:9"}
