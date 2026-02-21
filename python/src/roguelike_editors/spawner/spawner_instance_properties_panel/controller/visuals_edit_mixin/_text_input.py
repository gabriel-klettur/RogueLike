from __future__ import annotations

from typing import Any
import pygame
from roguelike_ui.widgets.text_input.text_input import TextInput


def begin_edit_visual_flow(owner: Any, state_key: str) -> None:
    owner.model.visuals_editing_state = str(state_key)

    cur_tpl = 'N/A'
    try:
        rows = owner.get_visuals_rows()
        for st, _iid, tpl in rows:
            if st == state_key:
                cur_tpl = str(tpl)
                break
    except (AttributeError, TypeError, ValueError):
        pass

    if cur_tpl.upper() == 'N/A':
        cur_tpl = ''

    try:
        if not hasattr(owner.model, 'visuals_pending_templates') or getattr(owner.model, 'visuals_pending_templates') is None:
            owner.model.visuals_pending_templates = {}
    except AttributeError:
        owner.model.visuals_pending_templates = {}

    owner.model.visuals_pending_templates[str(state_key)] = cur_tpl

    vti = getattr(owner.visuals.model, 'text_input', None)
    if vti is None:
        font = pygame.font.SysFont(None, 18)
        vti = TextInput(font)
        owner.visuals.model.text_input = vti

    vti.activate(cur_tpl, select_all=True)

    try:
        import pygame as _pg
        _pg.key.start_text_input()
    except (ImportError, AttributeError, pygame.error):
        pass


def cancel_edit_visual_flow(owner: Any) -> None:
    owner.model.visuals_editing_state = None
    try:
        import pygame as _pg
        _pg.key.stop_text_input()
    except (ImportError, AttributeError, pygame.error):
        pass
