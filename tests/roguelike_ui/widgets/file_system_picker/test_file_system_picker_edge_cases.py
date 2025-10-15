import pygame

import roguelike_ui.widgets.file_system_picker as fsp


def test_file_system_picker_handles_corrupt_png_and_parent_entry(tmp_path, monkeypatch):
    root = tmp_path / "assets"
    sub = root / "subdir"
    root.mkdir()
    sub.mkdir()
    # Non-png file should be ignored in listing
    (root / "note.txt").write_text("hi", encoding="utf-8")
    # Corrupt png path
    bad_png = root / "bad.png"
    bad_png.write_bytes(b"not a real png")

    # Stub icon loader and force pygame.image.load to raise to hit fallback thumbnail path
    monkeypatch.setattr(fsp, 'load_image', lambda path, size: pygame.Surface(size, pygame.SRCALPHA), raising=True)
    def boom_load(path):
        raise pygame.error("corrupt")
    monkeypatch.setattr(pygame.image, 'load', boom_load, raising=True)
    monkeypatch.setattr(pygame.transform, 'scale', lambda surf, sz: pygame.Surface(sz, pygame.SRCALPHA), raising=True)

    model = fsp.FileSystemPickerModel(str(root))
    view = fsp.FileSystemPickerView(model, thumb_size=12, pad=2, cols=2)
    screen = pygame.Surface((200, 120), flags=pygame.SRCALPHA)

    # First draw at root: no parent '..' entry expected
    hovered = view.draw(screen, position=(0, 0))
    names = [name for (name, _, _) in model.entries]
    assert ".." not in names
    # Ensure corrupt png didn't crash drawing (an entry should exist for bad.png)
    assert "bad.png" in names

    # Navigate into subdir (entries are [(name, path, is_dir), ...])
    model.current_dir = sub
    model.load_entries()
    view.draw(screen, position=(0, 0))
    names2 = [name for (name, _, _) in model.entries]
    # Now parent entry should be present
    assert ".." in names2
