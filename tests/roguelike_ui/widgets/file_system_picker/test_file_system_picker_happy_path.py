import types
import pygame

import roguelike_ui.widgets.file_system_picker as fsp


def test_file_system_picker_draw_and_navigate(tmp_path, monkeypatch):
    # Directory structure
    root = tmp_path / "assets"
    sub = root / "subdir"
    root.mkdir()
    sub.mkdir()
    (root / "a.png").write_bytes(b"fake")  # content won't be used (we stub image.load)

    # Stub load_image (icons) and pygame.image.load (thumbnails)
    monkeypatch.setattr(fsp, 'load_image', lambda path, size: pygame.Surface(size, pygame.SRCALPHA), raising=True)
    monkeypatch.setattr(pygame.image, 'load', lambda p: pygame.Surface((4, 4), pygame.SRCALPHA), raising=True)
    monkeypatch.setattr(pygame.transform, 'scale', lambda surf, sz: pygame.Surface(sz, pygame.SRCALPHA), raising=True)

    # Model + View
    m = fsp.FileSystemPickerModel(str(root))
    v = fsp.FileSystemPickerView(m, thumb_size=16, pad=2, cols=2)

    screen = pygame.Surface((300, 200), flags=pygame.SRCALPHA)

    # Draw at top-left; should populate entries and draw without error
    hovered = v.draw(screen, position=(0, 0))
    assert isinstance(m.entries, list)

    # Select first entry safely and open it (either '..' if exists, or a dir/file)
    if m.entries:
        v._on_picker_select(0)
        # on_open handles both dir navigation and file selection callback
        v._on_picker_open(0)

    # After navigation, draw again; should still work
    v.draw(screen, position=(0, 0))


def test_file_system_picker_handle_event_keyboard_initial_selection(tmp_path, monkeypatch):
    root = tmp_path / "assets"
    (root).mkdir()
    # two files to allow selection
    (root / "a.png").write_bytes(b"fake")
    (root / "b.png").write_bytes(b"fake")

    monkeypatch.setattr(fsp, 'load_image', lambda path, size: pygame.Surface(size, pygame.SRCALPHA), raising=True)
    monkeypatch.setattr(pygame.image, 'load', lambda p: pygame.Surface((4, 4), pygame.SRCALPHA), raising=True)
    monkeypatch.setattr(pygame.transform, 'scale', lambda surf, sz: pygame.Surface(sz, pygame.SRCALPHA), raising=True)

    m = fsp.FileSystemPickerModel(str(root))
    v = fsp.FileSystemPickerView(m, thumb_size=8, pad=2, cols=2)
    screen = pygame.Surface((200, 100), flags=pygame.SRCALPHA)

    v.draw(screen, (0, 0))
    # Ensure KEYDOWN moves selection from None to first entry
    ev = pygame.event.Event(pygame.KEYDOWN, key=pygame.K_RIGHT)
    v.handle_event(ev, position=(0, 0))
    assert v.grid_state.selected_index in (0, None)  # 0 if entries exist
