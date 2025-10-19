import types

import pygame

import roguelike_editors.buildings.buildings_picker.building_picker_view as bpv


class DummyEditorState:
    def __init__(self):
        self.entries = []
        self.history = []


def test_picker_back_icon_cached_by_size():
    ed = DummyEditorState()
    view = bpv.PickerView(ed)
    # same size must return cached surface object
    a = view._get_back_icon((64, 64))
    b = view._get_back_icon((64, 64))
    assert a is b
    # different size must produce a different cached object
    c = view._get_back_icon((32, 32))
    assert c is not a


def test_picker_drag_preview_cached(monkeypatch):
    ed = DummyEditorState()
    view = bpv.PickerView(ed)

    calls = {"count": 0}

    def fake_load_image(path, scale=None):
        calls["count"] += 1
        # return an opaque surface
        surf = pygame.Surface((10, 5), pygame.SRCALPHA)
        surf.fill((255, 255, 255, 255))
        return surf

    monkeypatch.setattr(bpv, "load_image", fake_load_image)

    # First call loads and caches
    s1 = view._get_drag_preview("assets/fake/a.png")
    # Second call reuses cache (no extra load)
    s2 = view._get_drag_preview("assets/fake/a.png")

    assert s1 is s2
    assert calls["count"] == 1


def test_picker_label_cache():
    ed = DummyEditorState()
    view = bpv.PickerView(ed)

    s1 = view._get_label("My Folder")
    s2 = view._get_label("My Folder")
    assert s1 is s2

    s3 = view._get_label("Other")
    assert s3 is not s1
