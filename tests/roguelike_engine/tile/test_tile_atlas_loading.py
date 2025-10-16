import pygame

from roguelike_engine.utils.loader import load_sprite_sheet


def test_load_sprite_sheet_slices_expected_number_of_frames(monkeypatch):
    # Create a synthetic sheet 3 columns x 1 row of 8x8 sprites = 24x8 total
    sprite_w, sprite_h = 8, 8
    columns = 3
    total_w, total_h = sprite_w * columns, sprite_h

    def _fake_load_image(path):
        surf = pygame.Surface((total_w, total_h), pygame.SRCALPHA)
        # Paint distinct columns to differentiate frames
        colors = [(255,0,0,255), (0,255,0,255), (0,0,255,255)]
        for i, c in enumerate(colors):
            rect = pygame.Rect(i * sprite_w, 0, sprite_w, sprite_h)
            surf.fill(c, rect)
        return surf

    monkeypatch.setattr("roguelike_engine.utils.loader.load_image", _fake_load_image)

    frames = load_sprite_sheet("any.png", (sprite_w, sprite_h), row=0, columns=columns, start_col=0)
    assert len(frames) == columns
    # Each frame must be the requested size
    for fr in frames:
        assert fr.get_size() == (sprite_w, sprite_h)
