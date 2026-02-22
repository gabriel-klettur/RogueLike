import logging
from pathlib import Path
from typing import Optional

import pygame

from roguelike_engine.config.config_tiles import INVERSE_OVERLAY_MAP
import roguelike_engine.config.config_tiles as _cfg_tiles
from roguelike_editors.tiles.tiles_picker_panel.tile_picker_controller import TilePickerController
from roguelike_editors.tiles.tiles_picker_panel.tile_picker_events import TilePickerEventHandler
from roguelike_editors.tiles.tiles_picker_panel.tile_picker_state import TilePickerState

logger = logging.getLogger(__name__)


class _MapTilePickerEditorProxy:
    """
    Minimal state proxy expected by TilePicker (scroll + current_choice).

    This avoids coupling the Tiles Picker to MapEditorState.
    """

    def __init__(self):
        self.scroll_offset: int = 0
        self.current_choice: Optional[str] = None


class PaintTilesController:
    """
    Map toolbar tool that opens a floating Tile Picker to choose an overlay tile
    and then applies it to a zone after confirmation.
    """

    def __init__(self, *, editor_state, map_controller, toolbar_controller):
        self.editor = editor_state
        self.map_controller = map_controller
        self.toolbar_controller = toolbar_controller

        # Picker state and adapter
        self.picker_state = TilePickerState()
        self._proxy = _MapTilePickerEditorProxy()
        self.picker = TilePickerController(self, self._proxy, self.picker_state)
        # Enable blinking selection highlight in the picker UI for Map Editor usage
        try:
            setattr(self.picker, 'blink_selection', True)
        except Exception:
            pass
        self.events = TilePickerEventHandler(self.picker, self._proxy, self.picker_state)

    # ---------------------------
    # Public API used by toolbar/view
    # ---------------------------
    def toggle(self) -> bool:
        """Toggle picker open/close and reflect mode highlight."""
        if self.picker_state.open:
            self.close()
            return False
        self.open()
        return True

    def open(self) -> None:
        """Open picker and anchor to the right of the toolbar widget panel."""
        try:
            # Anchor near toolbar panel
            widget = self.toolbar_controller.view.widget
            panel = widget.panel
            px, py = panel.pos or (widget.x, widget.y)
            w, h = panel.size if hasattr(panel, 'size') else (widget.size, widget.size)
            margin = getattr(widget, 'padding', 8)
            self.picker_state.pos = (px + w + margin, py)
        except Exception:
            # Fallback to screen center if toolbar not ready
            try:
                sw, sh = pygame.display.get_surface().get_size() if pygame.display.get_surface() else (800, 600)
            except Exception:
                sw, sh = (800, 600)
            self.picker_state.pos = ((sw - 320) // 2, (sh - 240) // 2)
        self.picker.open()
        # Do not enable paint_tiles_mode yet; it will be enabled after tile selection

    def close(self) -> None:
        """Close picker and clear highlight flag."""
        self.picker._close()
        try:
            self.editor.paint_tiles_mode = False
        except Exception:
            pass

    def is_open(self) -> bool:
        return bool(self.picker_state.open)

    def render(self, screen) -> None:
        if self.picker_state.open:
            self.picker.view.render(screen)

    # ---------------------------
    # Event plumbing from MapEditorEventHandler
    # ---------------------------
    def handle_event(self, ev, camera=None, map_manager=None) -> bool:
        """Forward events to the picker and react to selections.

        Returns True if the event was consumed by the picker.
        """
        if not self.picker_state.open:
            return False

        # Intercept left/right-click inside picker region
        if ev.type == pygame.MOUSEBUTTONDOWN and ev.button in (1, 3):
            pos = ev.pos
            if self.picker.is_over(pos):
                before = self._proxy.current_choice
                # map param optional for tileset slicing; safe to pass None
                consumed = self.events.handle_click(pos, ev.button, None)
                after = self._proxy.current_choice
                if consumed and after and after != before and ev.button == 1:
                    self._on_tile_selected(after)
                return True

        # Delegate drag/scroll/keyboard to picker events
        if self.events.handle_event(ev, camera=None, map=None):
            return True
        return False

    # ---------------------------
    # Internals
    # ---------------------------
    def _on_tile_selected(self, choice_path: str) -> None:
        """Set tile_code from chosen asset and prompt confirmation for a zone."""
        code = self._choice_to_overlay_code(choice_path)
        if not code:
            logger.warning("[MapEditor] No overlay code found for choice: %s", choice_path)
            return
        # Store chosen overlay code in MapEditorState
        self.editor.tile_code = code
        # Next step: allow user to click a zone to confirm painting (handled in modes.py)
        # Ensure paint_tiles_mode is enabled so a map click sets pending zone + confirmation
        try:
            self.editor.paint_tiles_mode = True
        except Exception:
            pass
        # Keep picker open so user can change selection if desired

    @staticmethod
    def _choice_to_overlay_code(choice_path: str) -> Optional[str]:
        """Translate 'tiles/<name>.png' to an overlay code via INVERSE_OVERLAY_MAP.

        If no code exists for the asset, register a dynamic mapping at runtime so
        get_sprite_for_tile can resolve the sprite by overlay code during painting.
        """
        name = Path(choice_path).stem
        codes = INVERSE_OVERLAY_MAP.get(name, [])
        if codes:
            return codes[0]
        # Create a dynamic overlay code mapping at runtime
        base_code = f"dyn_{name}"
        code = base_code
        # Ensure uniqueness if a similar key exists
        i = 1
        while code in _cfg_tiles.OVERLAY_CODE_MAP:
            i += 1
            code = f"{base_code}_{i}"
        # Register in both maps so assets.get_sprite_for_tile can resolve it
        _cfg_tiles.OVERLAY_CODE_MAP[code] = name
        INVERSE_OVERLAY_MAP.setdefault(name, []).append(code)
        return code

