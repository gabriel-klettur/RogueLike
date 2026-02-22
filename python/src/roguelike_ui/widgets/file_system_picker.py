"""
FileSystemPicker: Browse directories and select PNG assets.
Placed in roguelike_ui.widgets for reuse.
"""
import pygame
from typing import Optional
from pathlib import Path
from roguelike_ui.panel import PanelSurface
from roguelike_ui.widgets.picker_panel import PickerPanel, PickerPanelState
from roguelike_engine.utils.loader import load_image
from roguelike_editors.tiles.tiles_editor_config import ARROW_UP_ICON, FOLDER_ICON


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
        # cache for file thumbnails to avoid reloading each frame
        self.thumb_cache = {}
        # Reusable PickerPanel (no background, no dragging, overlays drawn here)
        self.picker = PickerPanel(
            cell_size=(self.thumb_size, self.thumb_size),
            margin=self.pad,
            padding=self.pad,
            draw_panel_bg=False,
            allow_dragging=False,
            draw_overlays=False,
        )
        self.grid_state = PickerPanelState(rect=pygame.Rect(0, 0, 0, 0), visible=True)
        self._last_draw_position = (0, 0)
        # Hover overlay surface (reused)
        self.hover_overlay = pygame.Surface((self.thumb_size, self.thumb_size), pygame.SRCALPHA)
        self.hover_overlay.fill((255, 230, 0, 100))
        if pygame.display.get_surface():
            self.hover_overlay = self.hover_overlay.convert_alpha()
        # Configure picker callbacks
        self.picker.set_item_count(lambda: len(self.model.entries))
        self.picker.set_draw_item(self._draw_entry)
        # External hooks for parent to react to open/select
        self.on_open = None  # type: Optional[callable]
        self.on_select = None  # type: Optional[callable]
        # Wire picker events
        self.picker.on_select = self._on_picker_select
        self.picker.on_open = self._on_picker_open

    def _draw_entry(self, surf: pygame.Surface, rect: pygame.Rect, idx: int, sel: bool, hov: bool) -> None:
        """Draw a single entry (icon/thumbnail, hover overlay, folder name)."""
        if idx < 0 or idx >= len(self.model.entries):
            return
        name, path, is_dir = self.model.entries[idx]
        # Record entry rect in global coordinates for interaction
        gx = self._last_draw_position[0] + rect.x
        gy = self._last_draw_position[1] + rect.y
        self.entry_rects.append((pygame.Rect(gx, gy, rect.width, rect.height), (name, path, is_dir), idx))
        # Draw icon or thumbnail
        if is_dir:
            if name == "..":
                icon = load_image(ARROW_UP_ICON, (self.thumb_size, self.thumb_size))
            else:
                icon = load_image(FOLDER_ICON, (self.thumb_size, self.thumb_size))
            surf.blit(icon, rect)
        else:
            thumb = self.thumb_cache.get(path)
            if not thumb:
                try:
                    thumb = pygame.image.load(str(path)).convert_alpha()
                    thumb = pygame.transform.scale(thumb, (self.thumb_size, self.thumb_size))
                except Exception:
                    thumb = pygame.Surface((self.thumb_size, self.thumb_size))
                    thumb.fill((100, 100, 100))
                self.thumb_cache[path] = thumb
            surf.blit(thumb, rect)
        # Hover overlay (preserve previous look & feel)
        mx, my = pygame.mouse.get_pos()
        lx, ly = mx - self._last_draw_position[0], my - self._last_draw_position[1]
        if rect.collidepoint((lx, ly)):
            surf.blit(self.hover_overlay, (rect.x, rect.y))
            pygame.draw.rect(surf, (255, 230, 0), rect, 2)
        # Centered folder name (ellipsized)
        if is_dir and name != "..":
            text = name
            max_w = rect.width - 4
            if self.font.size(text)[0] > max_w:
                while self.font.size(text + "...")[0] > max_w and len(text) > 0:
                    text = text[:-1]
                text = text + "..."
            label = self.font.render(text, True, (0, 0, 0))
            surf.blit(label, label.get_rect(center=rect.center))

    def _on_picker_select(self, idx: int) -> None:
        """Sync selection with model and notify parent."""
        if 0 <= idx < len(self.model.entries):
            name, path, is_dir = self.model.entries[idx]
            # Select folders/files uniformly for keyboard nav
            self.model.selected = path
            self.grid_state.selected_index = idx
            if self.on_select:
                try:
                    self.on_select(idx)
                except Exception:
                    pass

    def _on_picker_open(self, idx: int) -> None:
        """Open item: navigate folders or emit file open callback."""
        if 0 <= idx < len(self.model.entries):
            name, path, is_dir = self.model.entries[idx]
            if is_dir:
                # Navigate and clear selection/hover
                self.model.navigate(idx)
                self.grid_state.hovered_index = None
                self.grid_state.selected_index = None
            else:
                if self.on_open:
                    try:
                        self.on_open(path)
                    except Exception:
                        pass

    def draw(self, surface: pygame.Surface, position: tuple) -> tuple:
        """Draw panel at position, return hovered entry (name, path, is_dir)."""
        # Reset per-frame state
        self.entry_rects = []
        self.model.load_entries()
        count = len(self.model.entries)
        # Compute target grid dimensions to force a fixed number of columns
        cols = max(1, self.cols)
        rows_total = (count + cols - 1) // cols
        # Match legacy ScrollableGrid geometry (includes outer padding on both sides)
        w = cols * (self.thumb_size + self.pad) + self.pad
        grid_h_total = rows_total * (self.thumb_size + self.pad) + self.pad
        # Limit visible height to 3 rows and rely on PickerPanel scrollbar for overflow
        visible_rows = max(1, min(3, rows_total))
        grid_h_visible = visible_rows * (self.thumb_size + self.pad) + self.pad
        height = grid_h_visible + self.pad + 20  # extra space for path label
        # Init or resize panel
        if self.panel is None:
            self.panel = PanelSurface(w, height)
        else:
            self.panel.resize(w, height)
        # Prepare picker state and render grid
        self._last_draw_position = position
        self.grid_state.rect = pygame.Rect(0, 0, w, grid_h_visible)
        # Sync selected index from model before rendering (for keyboard navigation)
        if self.model.selected is not None:
            try:
                sel_idx = next((i for i, (_, p, _) in enumerate(self.model.entries) if p == self.model.selected), None)
                self.grid_state.selected_index = sel_idx
            except Exception:
                self.grid_state.selected_index = None
        self.grid_state.scroll_y = self.model.scroll_offset
        self.picker.render(self.panel.surface, self.grid_state)
        # Determine hovered entry using computed item rects
        hovered = None
        mx, my = pygame.mouse.get_pos()
        lx, ly = mx - position[0], my - position[1]
        hovered_idx = None
        for idx, rect in enumerate(self.grid_state.item_rects):
            if 0 <= idx < count and rect.collidepoint((lx, ly)):
                hovered = self.model.entries[idx]
                hovered_idx = idx
                break
        # Mirror hovered index into state for unified behavior
        self.grid_state.hovered_index = hovered_idx
        # Draw path label inside panel (full path for hovered entry or current directory)
        path_str = str(hovered[1]) if hovered else str(self.model.current_dir)
        big_font = pygame.font.SysFont(None, 20)
        label = big_font.render(path_str, True, (255, 230, 0))
        # Position label just below the visible grid area
        label_rect = label.get_rect(topleft=(self.pad, self.grid_state.rect.h + self.pad))
        self.panel.surface.blit(label, label_rect)
        # Blit panel
        surface.blit(self.panel.surface, position)
        return hovered

    def handle_event(self, event: pygame.event.Event, position: tuple) -> None:
        """Forward events to PickerPanel, mapping to local coordinates.
        Also sync scroll offset back to model.
        """
        # Temporarily translate event coordinates into local space for PickerPanel
        translated = event
        # For mouse events that carry a position, create a shallow copy with adjusted pos
        if hasattr(event, 'pos'):
            try:
                ex, ey = event.pos
                lx = ex - position[0]
                ly = ey - position[1]
                translated = pygame.event.Event(event.type, {**event.__dict__, 'pos': (lx, ly)})
            except Exception:
                translated = event
        # Initialize selection for keyboard navigation: consume the first arrow/return key
        if event.type == pygame.KEYDOWN:
            if self.grid_state.selected_index is None and len(self.model.entries) > 0:
                if event.key in (pygame.K_LEFT, pygame.K_RIGHT, pygame.K_UP, pygame.K_DOWN, pygame.K_RETURN):
                    self._on_picker_select(0)
                    # Consume this first key event to avoid immediate movement to index 1
                    self.model.scroll_offset = self.grid_state.scroll_y
                    return
        # Dispatch to picker (special-case wheel to use absolute rect for hit-test)
        if event.type == pygame.MOUSEWHEEL:
            old_rect = self.grid_state.rect
            try:
                self.grid_state.rect = self.grid_state.rect.move(position[0], position[1])
                self.picker.handle_event(event, self.grid_state)
            finally:
                # restore local rect
                self.grid_state.rect = old_rect
        else:
            self.picker.handle_event(translated, self.grid_state)
        # Sync scroll back
        self.model.scroll_offset = self.grid_state.scroll_y
