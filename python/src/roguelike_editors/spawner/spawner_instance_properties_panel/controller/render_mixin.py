from __future__ import annotations

from typing import Optional
import pygame


class RenderMixin:
    def _show_toast(self, text: str, duration_ms: Optional[int] = None) -> None:
        try:
            dur = int(duration_ms if duration_ms is not None else getattr(self, '_toast_ms', 1600))
        except Exception:
            dur = 1600
        try:
            now = 0
            try:
                now = int(pygame.time.get_ticks() or 0)
            except Exception:
                now = 0
            if hasattr(self, 'model') and self.model is not None:
                setattr(self.model, 'toast_message', str(text))
                setattr(self.model, 'toast_until_ms', int(now + max(0, dur)))
        except Exception:
            pass

    def render(self, screen, *, anchor=None):
        if not self.model.visible:
            return None
        # Keep rows up to date
        self._rows = self._flatten_instance()
        # Rebuild visuals rows if visuals changed externally
        self._build_visuals_rows()
        # Sanitize mappings during render in case external GC removed instances
        try:
            # Ensure fresh building index to avoid false removals
            try:
                self._building_index = None
            except AttributeError:
                pass
            self._ensure_buildings_index()
            self._sanitize_visuals_instances()
        except (AttributeError, TypeError, ValueError):
            pass
        # While holding on a visuals row, keep camera centered on its building
        try:
            vmodel = getattr(self.visuals, 'model', None)
            if vmodel is not None and getattr(vmodel, 'hold_active', False):
                j = getattr(vmodel, 'hold_row_index', None)
                vis_rows = self.get_visuals_rows()
                if j is not None and 0 <= int(j) < len(vis_rows):
                    st = str(vis_rows[int(j)][0])
                    self.visuals.center_camera_on_state(st)
        except (AttributeError, TypeError, ValueError):
            pass
        return self.view.render(self, screen, anchor=anchor)

    def handle_event(self, event) -> bool:
        if not self.model.visible:
            return False
        return self.events.handle_event(self, event)
