import pygame

import roguelike_ui.widgets.icon_cache as ic_mod


def test_get_icon_returns_none_on_loader_failure(monkeypatch):
    def fail_loader(path, size):
        raise RuntimeError("boom")

    monkeypatch.setattr(ic_mod, 'load_image', fail_loader, raising=True)

    img = ic_mod.IconCache.get_icon('assets/icons/missing.png', (32, 32))
    assert img is None
