from __future__ import annotations

from dataclasses import dataclass, field
from typing import Optional, List, Dict, Any, Callable
import json
import os
import pygame
import logging
from pathlib import Path

# Reuse Buildings picker UI and events
from roguelike_editors.buildings.buildings_picker.building_picker_controller import (
    BuildingPickerController,
)
from roguelike_editors.buildings.buildings_picker.building_picker_events import (
    BuildingPickerEventHandler,
)
from roguelike_editors.buildings.buildings_picker.building_picker_view import (
    PickerView,
)
from roguelike_engine.config.config import BUILDINGS_TEMPLATES_PATH, ASSETS_DIR


@dataclass
class VisualsPickerState:
    # Navigation state (compatible with PickerView expectations)
    current_dir: str = "assets/buildings"
    history: List[str] = field(default_factory=list)
    entries: List[Any] = field(default_factory=list)  # List[DirEntry]
    selected_entry: Any | None = None

    # UI/internals that PickerView sets/reads
    picker_panel_rect: Optional[pygame.Rect] = None
    picker_internal_margin: int = 8
    picker_cell_w: int = 64
    picker_cell_h: int = 64
    picker_padding: int = 8
    picker_footer_h: int = 0
    picker_visible_rows: int = 3
    picker_max_columns: Optional[int] = 12
    picker_rows_needed: int = 0
    picker_needs_scroll: bool = False
    picker_scrollbar_w: int = 10
    picker_scroll_row: int = 0
    picker_scroll_dragging: bool = False
    picker_scroll_drag_offset: int = 0
    picker_scroll_track_rect: Optional[pygame.Rect] = None
    picker_scroll_thumb_rect: Optional[pygame.Rect] = None

    # Panel drag with RMB (optional)
    picker_dragging_panel: bool = False
    picker_drag_offset: tuple[int, int] = (0, 0)
    picker_manual_pos: Optional[tuple[int, int]] = None

    # Drag (not used for visuals selection, but referenced by PickerView)
    dragging_building: bool = False

    # Visibility
    picker_active: bool = False


class _DummyPlacer:
    def place_building_at_path(self, *args, **kwargs):
        # No-op for visuals selection
        return


class VisualsPicker:
    """Orchestrates the Buildings Picker for use as a Spawner Visuals selector.

    When a file is selected (LMB) the picker resolves the building template_id
    by matching the asset path in buildings_templates.json and invokes the
    provided callback with that template id.
    """

    def __init__(
        self,
        on_template_selected: Callable[[int], None],
        *,
        base_dir: str = "assets/buildings",
        dim_background: bool = False,
    ) -> None:
        # Resolve base directory robustly using engine config ASSETS_DIR
        try:
            assets_root = Path(ASSETS_DIR)
        except Exception:
            assets_root = Path("assets")
        # Normalize provided base_dir to a Path
        try:
            bd = Path(base_dir)
        except Exception:
            bd = Path("assets/buildings")
        # If path is relative or starts with 'assets/', resolve against assets_root
        base_abs_path: Path
        try:
            if not bd.is_absolute():
                # Strip leading 'assets' if present to avoid assets/assets
                parts = list(bd.parts)
                if parts and parts[0].lower() == 'assets':
                    bd = Path(*parts[1:]) if len(parts) > 1 else Path('.')
                base_abs_path = assets_root / bd
            else:
                base_abs_path = bd
        except Exception:
            base_abs_path = assets_root / "buildings"
        # Fallbacks
        if not base_abs_path.is_dir():
            # Prefer 'assets/buildings'
            cand = assets_root / "buildings"
            base_abs_path = cand if cand.is_dir() else assets_root
        self.state = VisualsPickerState(current_dir=str(base_abs_path))
        self.state.picker_active = True
        # UI+events reuse from Buildings picker
        self._picker_view = PickerView(self.state)
        self._picker_ctrl = BuildingPickerController(self.state, _DummyPlacer())
        self._picker_events = BuildingPickerEventHandler(self.state, self._picker_ctrl, buildings=[])
        # Selection callback
        self._on_template_selected = on_template_selected
        # Dim background flag (overlay)
        self._dim_background = bool(dim_background)
        # Build mapping asset_path -> template_id
        self._assets_to_tpl: Dict[str, int] = {}
        # Fallback mapping by filename (only when unique)
        self._basename_to_tpl: Dict[str, int] = {}
        self._build_assets_index()
        # Track last selected path to detect changes on clicks
        self._last_selected_path: Optional[str] = None
        # Logger
        self._log = logging.getLogger(__name__)
        try:
            if not self._log.handlers:
                _h = logging.StreamHandler()
                _h.setLevel(logging.DEBUG)
                _h.setFormatter(logging.Formatter('[%(levelname)s] %(name)s: %(message)s'))
                self._log.addHandler(_h)
            self._log.setLevel(logging.DEBUG)
            # Avoid duplicate propagation
            self._log.propagate = False
        except (AttributeError, ValueError):
            pass

    def _norm(self, p: str) -> str:
        p = (p or "").replace("\\", "/")
        # ensure starts from assets/
        if "/assets/" in "/" + p:
            idx = p.find("assets/")
            if idx >= 0:
                p = p[idx:]
        return p

    def _norm_key(self, p: str) -> str:
        """Normalize a path to our canonical key: forward slashes, assets/ prefix, and lowercase."""
        try:
            return self._norm(p).lower()
        except (AttributeError, TypeError):
            return (p or "").replace("\\", "/").lower()

    def _build_assets_index(self) -> None:
        try:
            with open(BUILDINGS_TEMPLATES_PATH, "r", encoding="utf-8") as f:
                arr = json.load(f)
            if isinstance(arr, list):
                # Temp aggregation for basename mapping
                _by_basename: Dict[str, set[int]] = {}
                for e in arr:
                    try:
                        tid = int(e.get("id"))
                        # Collect candidate asset paths
                        # 1) Legacy root image field
                        legacy_img = e.get("image")
                        if legacy_img:
                            k = self._norm_key(str(legacy_img))
                            self._assets_to_tpl[k] = tid
                            _by_basename.setdefault(os.path.basename(k), set()).add(tid)
                        # 2) assets dict: include 'idle', 'image', and any string values
                        assets = e.get("assets") or {}
                        if isinstance(assets, dict):
                            for k, v in assets.items():
                                if isinstance(v, str):
                                    kk = self._norm_key(v)
                                    self._assets_to_tpl[kk] = tid
                                    _by_basename.setdefault(os.path.basename(kk), set()).add(tid)
                                elif isinstance(v, (list, tuple)):
                                    for it in v:
                                        if isinstance(it, str):
                                            kk2 = self._norm_key(it)
                                            self._assets_to_tpl[kk2] = tid
                                            _by_basename.setdefault(os.path.basename(kk2), set()).add(tid)
                    except (AttributeError, TypeError, ValueError):
                        continue
                # Compute unique basename mapping
                for bn, tids in _by_basename.items():
                    if len(tids) == 1:
                        self._basename_to_tpl[bn.lower()] = next(iter(tids))
            # Debug mapping size
            try:
                import logging as _lg
                _lg.getLogger(__name__).debug(f"[VisualsPicker] assets->tpl mappings: {len(self._assets_to_tpl)}, basename uniques: {len(self._basename_to_tpl)}")
            except (ImportError, AttributeError):
                pass
        except (OSError, json.JSONDecodeError, ValueError, TypeError):
            self._assets_to_tpl = {}

    def open(self) -> None:
        self.state.picker_active = True
        # Allow selecting the same asset across different picker sessions
        self._last_selected_path = None

    def close(self) -> None:
        self.state.picker_active = False
        # Reset dedupe guard so next session can re-select the same asset
        self._last_selected_path = None

    # Methods referenced by BuildingPickerEventHandler through controller
    def close_picker(self) -> None:
        self.close()

    # Anchoring API so the picker can be positioned under the Instances panel
    def set_anchors(self, *, left_x: Optional[int] = None, top_y: Optional[int] = None, reserved_bottom_h: Optional[int] = None) -> None:
        try:
            if left_x is not None:
                setattr(self._picker_view, '_left_anchor_x', int(left_x))
            if top_y is not None:
                setattr(self._picker_view, '_top_anchor_y', int(top_y))
            if reserved_bottom_h is not None:
                setattr(self._picker_view, '_reserved_bottom_h', int(reserved_bottom_h))
            # If for some reason entries are empty (e.g., base path resolved late), refresh now
            if not getattr(self.state, 'entries', None):
                self._picker_ctrl.list_entries()
        except (AttributeError, TypeError, ValueError):
            pass

    def render(self, screen: pygame.Surface, camera) -> Optional[pygame.Rect]:
        if not self.state.picker_active:
            return None
        # Optional dark overlay behind the panel
        if self._dim_background:
            try:
                overlay = pygame.Surface(screen.get_size(), pygame.SRCALPHA)
                overlay.fill((0, 0, 0, 160))
                screen.blit(overlay, (0, 0))
            except (pygame.error, ValueError, TypeError):
                pass
        # Render picker panel
        self._picker_view.render(screen, camera)
        return getattr(self.state, "picker_panel_rect", None)

    def handle_event(self, event, camera) -> bool:
        if not self.state.picker_active:
            return False
        # Block RMB interactions in Visuals context to avoid spawn/drag flows
        try:
            if getattr(event, 'type', None) in (pygame.MOUSEBUTTONDOWN, pygame.MOUSEBUTTONUP) and getattr(event, 'button', None) == 3:
                self._log.debug("[VisualsPicker] RMB blocked in visuals context")
                return True
        except (AttributeError, TypeError):
            pass
        # Forward to buildings picker events
        try:
            self._picker_events.handle(event, camera)
        except (AttributeError, TypeError, ValueError) as ex:
            self._log.exception("[VisualsPicker] error forwarding event: %s", ex)
        # After LMB click selection, commit immediately if a file is selected
        selected = getattr(self.state, "selected_entry", None)
        if selected is not None and not getattr(selected, "is_dir", False):
            raw_path = getattr(selected, "path", "")
            path = self._norm(raw_path)
            key = self._norm_key(raw_path)
            # Avoid duplicate commits on move/drag sequences
            if path and path != self._last_selected_path:
                self._last_selected_path = path
                tpl_id = self._assets_to_tpl.get(key)
                if tpl_id is None:
                    # Try alternative match: sometimes entries may retain absolute path; normalize again
                    alt = path
                    if not alt.startswith("assets/"):
                        # Attempt to find tail starting at 'assets/'
                        idx = alt.find("assets/")
                        if idx >= 0:
                            alt = alt[idx:]
                    tpl_id = self._assets_to_tpl.get(self._norm_key(alt))
                if tpl_id is None:
                    # Fallback by basename if unique
                    bn = os.path.basename(path).lower()
                    tpl_id = self._basename_to_tpl.get(bn)
                    if tpl_id is not None:
                        self._log.debug(f"[VisualsPicker] basename fallback matched {bn} -> tpl_id={tpl_id}")
                self._log.debug(f"[VisualsPicker] LMB select file path={path} -> tpl_id={tpl_id}")
                if tpl_id is not None:
                    try:
                        self._on_template_selected(int(tpl_id))
                        self._log.info(f"[VisualsPicker] Applied template_id={tpl_id} and closing picker")
                    except (AttributeError, TypeError, ValueError):
                        pass
                    # Close after commit
                    self.close()
                else:
                    self._log.warning(f"[VisualsPicker] No template mapping found for path={path}")
        # ESC to close is already handled by BuildingPickerEventHandler via close_picker()
        return True


__all__ = ["VisualsPicker", "VisualsPickerState"]
