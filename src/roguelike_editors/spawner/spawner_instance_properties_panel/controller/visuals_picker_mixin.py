from __future__ import annotations

from typing import Optional
import pygame
from ..visuals.visuals_picker import VisualsPicker


class VisualsPickerMixin:
    def _on_visuals_picker_selected(self, state_key: str, template_id: int) -> None:
        """Callback invoked by the VisualsPicker when a template image is chosen."""
        try:
            self._log.info(f"[InstanceProps] Picker selected: state={state_key} tpl_id={template_id}")
            # Debounce sanitization for a short period while we create/persist/update indexes
            try:
                import pygame as _pg
                self._sanitize_block_until_ms = int((_pg.time.get_ticks() or 0) + 600)
            except (ImportError, AttributeError, TypeError, ValueError):
                self._sanitize_block_until_ms = 0
            self.set_visual_template_via_picker(state_key, int(template_id))
            # Toast feedback
            self._show_toast(f"Template aplicado: {int(template_id)} → {state_key}")
        except (AttributeError, TypeError, ValueError):
            # Best effort; keep UI consistent
            pass
        # Close picker after applying
        try:
            self.model.visuals_picker_open = False
            self.model.visuals_picker_state = None
        except AttributeError:
            pass
        self._visuals_picker = None
        # Force refresh rows/index after applying to ensure UI reflects changes immediately
        try:
            self._building_index = None
            self._ensure_buildings_index()
            self._build_visuals_rows()
            # Reload from disk to ensure we keep in sync
            self._reload_selected_from_json()
            self._log.debug(f"[InstanceProps] After picker close, visuals_rows: {self.model.visuals_rows}")
        except (AttributeError, TypeError, ValueError, OSError):
            pass

    def open_visuals_picker_for_state(self, state_key: str) -> None:
        """Open the visuals picker and bind it to the given visuals state key."""
        self.model.visuals_picker_state = str(state_key)
        self.model.visuals_picker_open = True
        # Create picker with callback bound to this state
        def _cb(tpl_id: int, _state=state_key):
            self._on_visuals_picker_selected(_state, int(tpl_id))
        self._visuals_picker = VisualsPicker(_cb)
        # Anchor below panel if available
        try:
            prec = getattr(self.view, 'panel_rect', None)
            if prec is not None and self._visuals_picker is not None:
                self._visuals_picker.set_anchors(left_x=prec.left, top_y=prec.bottom + 6, reserved_bottom_h=40)
        except AttributeError:
            pass
        try:
            self._log.debug(f"[InstanceProps] Opened VisualsPicker for state={state_key}")
        except (AttributeError, TypeError, ValueError):
            pass

    def get_visuals_picker(self) -> Optional[VisualsPicker]:
        return self._visuals_picker

    def handle_visuals_picker_event(self, event, camera) -> bool:
        if not getattr(self.model, 'visuals_picker_open', False) or self._visuals_picker is None:
            return False
        try:
            handled = self._visuals_picker.handle_event(event, camera)
            # Debug log only for mouse clicks and keydown
            et = getattr(event, 'type', None)
            btn = getattr(event, 'button', None)
            if et in (pygame.MOUSEBUTTONDOWN, pygame.MOUSEBUTTONUP):
                self._log.debug(f"[InstanceProps] Picker event: type={et} btn={btn}")
            return handled
        except Exception:
            # Be defensive: never let picker event handling crash the editor loop
            self._log.debug("[InstanceProps] handle_visuals_picker_event: exception while handling picker event", exc_info=True)
            return False

    def render_visuals_picker(self, screen, camera) -> None:
        if not getattr(self.model, 'visuals_picker_open', False) or self._visuals_picker is None:
            # Ensure UI reloads from persisted disk state
            try:
                self._reload_selected_from_json()
            except (AttributeError, OSError, ValueError, TypeError):
                pass
            return
        try:
            self._visuals_picker.render(screen, camera)
        except AttributeError:
            pass
