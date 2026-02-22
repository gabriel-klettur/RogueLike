from __future__ import annotations

from typing import Any
import pygame


def commit_visual_edit_if_finished_flow(owner: Any) -> bool:
    display_state = getattr(owner.model, 'visuals_editing_state', None)
    if not display_state:
        return False

    vti = getattr(owner.visuals.model, 'text_input', None)
    if vti is None or vti.active:
        return False

    new_txt = vti.text if vti else ''
    try:
        owner.model.visuals_pending_templates[display_state] = new_txt
    except Exception:
        pass

    ok, _msg, new_tpl_id = owner._validate_template_text(new_txt)
    if not ok and new_txt.strip() != '':
        try:
            if vti is not None:
                vti.activate(new_txt, select_all=False)
        except (AttributeError, TypeError, ValueError):
            pass
        return True

    if new_txt.strip() == '':
        try:
            key_map = getattr(owner.model, 'visuals_key_map', {}) or {}
            json_key = key_map.get(display_state, display_state)
            visuals = dict(getattr(owner.model, 'visuals', {}) or {})
            if json_key in visuals:
                visuals.pop(json_key, None)
                owner.model.visuals = visuals
                try:
                    if owner.model.selected_instance is not None:
                        owner.model.selected_instance['visuals'] = visuals
                except AttributeError:
                    pass
                owner._persist_instance()
                owner._build_visuals_rows()
        except (AttributeError, TypeError, ValueError):
            pass
        owner.model.visuals_editing_state = None
        try:
            import pygame as _pg
            _pg.key.stop_text_input()
        except (ImportError, AttributeError, pygame.error):
            pass
        return True

    try:
        if new_tpl_id is not None:
            try:
                import pygame as _pg
                owner._sanitize_block_until_ms = int((_pg.time.get_ticks() or 0) + 600)
            except (ImportError, AttributeError, TypeError, ValueError):
                owner._sanitize_block_until_ms = 0
            owner.set_visual_template_via_picker(str(display_state), int(new_tpl_id))
    except (AttributeError, TypeError, ValueError):
        pass

    owner.model.visuals_editing_state = None
    try:
        import pygame as _pg
        _pg.key.stop_text_input()
    except (ImportError, AttributeError, pygame.error):
        pass
    return True
