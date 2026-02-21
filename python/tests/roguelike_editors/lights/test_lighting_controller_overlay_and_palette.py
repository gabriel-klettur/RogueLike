import types
import sys
import pygame
import pytest

import roguelike_editors.lighting.lighting_controller as ctrl_mod


class DummyLM:
    def __init__(self):
        self.enabled = True
        self._tile_occ = False
        self._shadows = False
        self._removed = []

    def set_enabled(self, v: bool):
        self.enabled = bool(v)

    def tile_occlusion_enabled(self):
        return self._tile_occ

    def shadow_polygons_enabled(self):
        return self._shadows

    def remove_by_id(self, lid: str):
        self._removed.append(lid)

    # Additional API used elsewhere, no-op
    def set_quality(self, *a, **k):
        pass


class DummyDN:
    def __init__(self):
        self.enabled = True


@pytest.fixture()
def fake_lighting_modules(monkeypatch):
    lm = DummyLM()
    # Create fake modules in sys.modules
    lighting_pkg = types.ModuleType("roguelike_engine.rendering.lighting")
    lighting_pkg.get_global_lighting = lambda: lm
    daynight_mod = types.ModuleType("roguelike_engine.rendering.lighting.daynight")
    daynight_mod.get_global_daynight = lambda: DummyDN()
    monkeypatch.setitem(sys.modules, "roguelike_engine.rendering.lighting", lighting_pkg)
    monkeypatch.setitem(sys.modules, "roguelike_engine.rendering.lighting.daynight", daynight_mod)
    return lm


def test_overlay_and_labels_toggles_and_palette_cycle(fake_lighting_modules, monkeypatch):
    c = ctrl_mod.LightingEditorController(font=None)
    st = c.model
    st.visible = True
    # Provide dummy button rects
    st._btn_overlay = pygame.Rect(10, 10, 100, 20)
    st._btn_labels = pygame.Rect(10, 40, 100, 20)
    st._btn_palette_prev = pygame.Rect(10, 70, 100, 20)
    st._btn_palette_next = pygame.Rect(10, 100, 100, 20)

    # Start flags
    st.overlay_visible = True
    st.overlay_labels = True

    # Click overlay to toggle off
    c._on_click((15, 15))
    assert st.overlay_visible is False
    # Click overlay to toggle on
    c._on_click((15, 15))
    assert st.overlay_visible is True

    # Click labels to toggle off
    c._on_click((15, 45))
    assert st.overlay_labels is False
    # Toggle on
    c._on_click((15, 45))
    assert st.overlay_labels is True

    # Palette cycle: set hovered preset id and cycle colors
    st._hovered_preset_id = "torch"
    # First next sets a default color from palette
    c._on_click((15, 105))
    assert st.overlay_palette.get("torch") is not None
    first = st.overlay_palette["torch"]
    # Next moves to a different color
    c._on_click((15, 105))
    second = st.overlay_palette["torch"]
    assert second != first
    # Prev goes back
    c._on_click((15, 75))
    assert st.overlay_palette["torch"] == first
