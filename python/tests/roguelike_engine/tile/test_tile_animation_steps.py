import pygame

from roguelike_engine.utils.loader import load_sprite_sheet


def test_animation_frames_order_and_size(monkeypatch):
    # Build a 4x1 sheet of 6x6 sprites with distinct colors per column
    w, h, cols = 6, 6, 4
    total_w, total_h = w * cols, h

    colors = [(255, 0, 0, 255), (0, 255, 0, 255), (0, 0, 255, 255), (255, 255, 0, 255)]

    def _fake_load_image(path):
        surf = pygame.Surface((total_w, total_h), pygame.SRCALPHA)
        for i, c in enumerate(colors):
            rect = pygame.Rect(i * w, 0, w, h)
            surf.fill(c, rect)
        return surf

    monkeypatch.setattr("roguelike_engine.utils.loader.load_image", _fake_load_image)

    frames = load_sprite_sheet("sheet.png", (w, h), row=0, columns=cols, start_col=0)
    assert len(frames) == cols
    # Spot-check top-left pixel color per frame to verify order was preserved
    for i, fr in enumerate(frames):
        assert fr.get_at((0, 0)) == colors[i]
