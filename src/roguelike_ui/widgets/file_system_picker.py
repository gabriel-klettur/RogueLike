"""
FileSystemPicker: Browse directories and select PNG assets.
Placed in roguelike_ui.widgets for reuse.
"""
import pygame
from pathlib import Path
from roguelike_ui.panel import PanelSurface
from roguelike_ui.widgets.grid import ScrollableGrid


class FileSystemPickerModel:
    """
    Model for file system picker.
    root_dir: Path where browsing starts.
    current_dir: Path currently browsing.
    entries: List of tuples (name, Path, is_dir).
    scroll_offset: vertical scroll.
    selected: selected entry name or None.
    """
    def __init__(self, root_dir: str):
        self.root_dir = Path(root_dir)
        self.current_dir = Path(root_dir)
        self.entries = []  # list of (name, Path, is_dir)
        self.scroll_offset = 0
        self.selected = None

    def load_entries(self):
        """Populate entries with directories and PNG files."""
        items = []
        # parent navigation
        if self.current_dir != self.root_dir:
            items.append(("..", self.current_dir.parent, True))
        # directories
        for entry in sorted(self.current_dir.iterdir()):
            if entry.is_dir():
                items.append((entry.name, entry, True))
        # PNG files
        for entry in sorted(self.current_dir.glob("*.png")):
            items.append((entry.name, entry, False))
        self.entries = items

    def navigate(self, index: int):
        """Navigate into directory or select file based on index."""
        name, path, is_dir = self.entries[index]
        if is_dir:
            # change directory
            self.current_dir = path
            self.scroll_offset = 0
            self.load_entries()
        else:
            self.selected = path
            return path


class FileSystemPickerView:
    """
    View for file system picker. Draws a grid of entries with scroll support.
    """
    def __init__(self, model: FileSystemPickerModel, thumb_size: int = 64, pad: int = 5, cols: int = 5):
        self.model = model
        self.thumb_size = thumb_size
        self.pad = pad
        self.cols = cols
        self.panel = None
        # font for labels
        self.font = pygame.font.SysFont(None, 14)
        self.entry_rects = []

    def draw(self, surface: pygame.Surface, position: tuple) -> tuple:
        """Draw panel at position, return hovered entry (name,path,is_dir)."""
        self.entry_rects = []
        self.model.load_entries()
        count = len(self.model.entries)
        grid = ScrollableGrid(self.thumb_size, self.pad, count, self.model.scroll_offset, cols=self.cols)
        cols, rows, w, grid_h = grid.compute()
        height = grid_h + self.pad + 20  # extra space for labels
        # init or resize panel
        if self.panel is None:
            self.panel = PanelSurface(w, height)
        else:
            self.panel.resize(w, height)
        # draw entries
        hovered = None
        def draw_fn(surf, rect, entry, idx):
            # record entry rect in global coordinates
            self.entry_rects.append((pygame.Rect(position[0] + rect.x, position[1] + rect.y, rect.width, rect.height), entry, idx))
            name, path, is_dir = entry
            # placeholder icon
            color = (80, 80, 150) if is_dir else (100, 100, 100)
            pygame.draw.rect(surf, color, rect)
            # label below
            label = self.font.render(name, True, (255,255,255))
            surf.blit(label, (rect.x, rect.y + rect.height + 2))
        hovered = grid.draw_items(self.panel.surface, self.model.entries, position, draw_fn)
        # blit panel
        surface.blit(self.panel.surface, position)
        return hovered
