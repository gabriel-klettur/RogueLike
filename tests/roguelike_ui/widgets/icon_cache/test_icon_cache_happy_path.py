import pygame

import roguelike_ui.widgets.icon_cache as ic_mod


def test_get_icon_caches_result(monkeypatch):
    # Fake loader returns a surface
    def fake_load_image(path, size):
        return pygame.Surface(size, flags=pygame.SRCALPHA)

    monkeypatch.setattr(ic_mod, 'load_image', fake_load_image, raising=True)

    s1 = ic_mod.IconCache.get_icon('assets/icons/sword.png', (16, 16))
    assert isinstance(s1, pygame.Surface)
    # Second call should return exactly the cached object (same id)
    s2 = ic_mod.IconCache.get_icon('assets/icons/sword.png', (16, 16))
    assert s1 is s2
