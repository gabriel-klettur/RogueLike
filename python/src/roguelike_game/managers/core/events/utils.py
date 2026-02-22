import pygame


def is_mmb_held(ev=None) -> bool:
    try:
        if ev is not None:
            buttons = getattr(ev, 'buttons', None)
            if buttons and len(buttons) >= 3:
                return bool(buttons[1])
        return bool(pygame.mouse.get_pressed(3)[1])
    except Exception:
        return False


def allow_mmb_ui(game) -> bool:
    sp_vis = bool(getattr(getattr(game, 'spawner_editor', None), 'model', None) and getattr(game.spawner_editor.model, 'visible', False))
    spells_vis = bool(getattr(getattr(game, 'spells_editor', None), 'model', None) and getattr(game.spells_editor.model, 'visible', False))
    particles_vis = bool(getattr(getattr(game, 'particles_editor', None), 'model', None) and getattr(game.particles_editor.model, 'visible', False))
    items_vis = bool(getattr(getattr(game, 'item_editor', None), 'model', None) and getattr(game.item_editor.model, 'visible', False))
    try:
        import roguelike_engine.config.config as cfg
        fsm_vis = bool(getattr(cfg, 'DEBUG_ENTITIES', False))
    except Exception:
        fsm_vis = False
    return sp_vis or spells_vis or particles_vis or items_vis or fsm_vis
