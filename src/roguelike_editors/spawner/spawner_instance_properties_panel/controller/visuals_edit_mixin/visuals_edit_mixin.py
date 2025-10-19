from __future__ import annotations

from typing import Optional

from ._validators import parse_int as _parse_int_helper, validate_template_text as _validate_tpl_text
from ._text_input import begin_edit_visual_flow as _begin_edit_flow, cancel_edit_visual_flow as _cancel_edit_flow
from ._commit_flow import commit_visual_edit_if_finished_flow as _commit_edit_flow
from ._picker_flow import set_visual_template_via_picker_flow as _picker_flow
from ._add_instance_flow import add_building_instance_for_visual_flow as _add_instance_flow
from ._clear_flow import clear_visual_for_state_flow as _clear_flow


class VisualsEditMixin:
    # --- Validation helpers --------------------------------------------------
    def _parse_int(self, t: str) -> Optional[int]:
        """Parse an integer from string safely. Returns None if invalid."""
        return _parse_int_helper(t)

    def _validate_template_text(self, text: str) -> tuple[bool, Optional[str], Optional[int]]:
        """Return (is_valid, error_msg, parsed_id). Empty text returns (True, None, None)."""
        return _validate_tpl_text(self, text)

    def get_visual_input_validation(self, state_key: str) -> tuple[bool, Optional[str]]:
        """Check current text being edited for a given state."""
        txt = (self.model.visuals_pending_templates or {}).get(state_key, '')
        if getattr(self.model, 'visuals_editing_state', None) == state_key:
            vti = getattr(self.visuals.model, 'text_input', None)
            if vti is not None:
                try:
                    txt = vti.text
                except AttributeError:
                    pass
        ok, msg, _ = self._validate_template_text(txt)
        return ok, msg

    # --- Visuals editing API -------------------------------------------------
    def begin_edit_visual(self, state_key: str) -> None:
        _begin_edit_flow(self, state_key)

    def cancel_edit_visual(self) -> None:
        _cancel_edit_flow(self)

    def commit_visual_edit_if_finished(self) -> bool:
        return _commit_edit_flow(self)

    def set_visual_template_via_picker(self, state_key: str, new_tpl_id: int) -> None:
        return _picker_flow(self, state_key, new_tpl_id)

    def add_building_instance_for_visual(self, state_key: str, reveal: bool = True) -> Optional[int]:
        return _add_instance_flow(self, state_key, reveal)

    def clear_visual_for_state(self, state_key: str) -> None:
        return _clear_flow(self, state_key)
