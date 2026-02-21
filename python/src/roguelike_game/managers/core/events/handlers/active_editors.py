import pygame
from roguelike_engine.input.events import handle_events as engine_handle_events
from roguelike_editors.fsm.fsm_editor_events import FsmEditorEventHandler
from ..utils import is_mmb_held as _is_mmb_held


def handle_active_editors(game, events, overlay) -> bool:
    # Lighting editor: when visible, delegate its events
    try:
        le = getattr(game, 'lighting_editor', None)
        if le and getattr(getattr(le, 'model', None), 'visible', False):
            for event in events:
                try:
                    le.handle_event(event)
                except Exception:
                    pass
            return True
    except Exception:
        pass
    # Si el editor de ítems está activo, permitir MMB pan (pase al engine) y delegar sus eventos
    if getattr(getattr(game, 'item_editor', None), 'model', None) and game.item_editor.model.visible:
        for event in events:
            game.item_editor.handle_event(event)
        mmb_events = []
        for ev in events:
            if ev.type in (pygame.MOUSEBUTTONDOWN, pygame.MOUSEBUTTONUP) and getattr(ev, 'button', None) == 2:
                mmb_events.append(ev)
            elif ev.type == pygame.MOUSEMOTION:
                try:
                    mmb_held = _is_mmb_held(ev)
                except Exception:
                    mmb_held = False
                if mmb_held:
                    mmb_events.append(ev)
        if mmb_events:
            engine_handle_events(
                game.state,
                game.camera,
                game.clock,
                game.menu,
                game.map,
                game.buildings,
                game.tiles_editor,
                game.buildings_editor,
                game.map_editor,
                game.spawner_editor,
                mmb_events,
                diagnostics_overlay=overlay,
                particles_editor=getattr(game, 'particles_editor', None),
                spells_editor=getattr(game, 'spells_editor', None),
                item_editor=getattr(game, 'item_editor', None),
                fsm_visible=getattr(__import__('roguelike_engine.config.config', fromlist=['config']), 'DEBUG_ENTITIES', False),
            )
        return True

    # Si el editor de partículas está activo, delegar sus eventos y permitir MMB pan (pase al engine)
    if getattr(getattr(game, 'particles_editor', None), 'model', None) and getattr(game.particles_editor.model, 'visible', False):
        for event in events:
            try:
                game.particles_editor.handle_event(event)
            except Exception:
                pass
        mmb_events = []
        for ev in events:
            if ev.type in (pygame.MOUSEBUTTONDOWN, pygame.MOUSEBUTTONUP) and getattr(ev, 'button', None) == 2:
                mmb_events.append(ev)
            elif ev.type == pygame.MOUSEMOTION:
                try:
                    mmb_held = _is_mmb_held(ev)
                except Exception:
                    mmb_held = False
                if mmb_held:
                    mmb_events.append(ev)
        if mmb_events:
            engine_handle_events(
                game.state,
                game.camera,
                game.clock,
                game.menu,
                game.map,
                game.buildings,
                game.tiles_editor,
                game.buildings_editor,
                game.map_editor,
                game.spawner_editor,
                mmb_events,
                diagnostics_overlay=overlay,
                particles_editor=getattr(game, 'particles_editor', None),
                spells_editor=getattr(game, 'spells_editor', None),
                item_editor=getattr(game, 'item_editor', None),
                fsm_visible=getattr(__import__('roguelike_engine.config.config', fromlist=['config']), 'DEBUG_ENTITIES', False),
            )
        return True

    # Si el editor de hechizos está activo, permitir MMB pan (pase al engine) y delegar sus eventos
    if getattr(getattr(game, 'spells_editor', None), 'model', None) and game.spells_editor.model.visible:
        for event in events:
            game.spells_editor.handle_event(event)
        mmb_events = []
        for ev in events:
            if ev.type in (pygame.MOUSEBUTTONDOWN, pygame.MOUSEBUTTONUP) and getattr(ev, 'button', None) == 2:
                mmb_events.append(ev)
            elif ev.type == pygame.MOUSEMOTION:
                try:
                    mmb_held = _is_mmb_held(ev)
                except Exception:
                    mmb_held = False
                if mmb_held:
                    mmb_events.append(ev)
        if mmb_events:
            engine_handle_events(
                game.state,
                game.camera,
                game.clock,
                game.menu,
                game.map,
                game.buildings,
                game.tiles_editor,
                game.buildings_editor,
                game.map_editor,
                game.spawner_editor,
                mmb_events,
                diagnostics_overlay=overlay,
                spells_editor=getattr(game, 'spells_editor', None),
                item_editor=getattr(game, 'item_editor', None),
                fsm_visible=getattr(__import__('roguelike_engine.config.config', fromlist=['config']), 'DEBUG_ENTITIES', False),
            )
        return True

    # Si el editor FSM (Entities Spy) está visible, delegar al editor y pasar MMB al motor para pan
    try:
        import roguelike_engine.config.config as cfg
        fsm_vis = bool(getattr(cfg, 'DEBUG_ENTITIES', False))
    except Exception:
        fsm_vis = False
    if fsm_vis:
        for event in events:
            try:
                FsmEditorEventHandler.handle_event(event)
            except Exception:
                pass
        mmb_events = []
        for ev in events:
            if ev.type in (pygame.MOUSEBUTTONDOWN, pygame.MOUSEBUTTONUP) and getattr(ev, 'button', None) == 2:
                mmb_events.append(ev)
            elif ev.type == pygame.MOUSEMOTION:
                try:
                    mmb_held = _is_mmb_held(ev)
                except Exception:
                    mmb_held = False
                if mmb_held:
                    mmb_events.append(ev)
        if mmb_events:
            engine_handle_events(
                game.state,
                game.camera,
                game.clock,
                game.menu,
                game.map,
                game.buildings,
                game.tiles_editor,
                game.buildings_editor,
                game.map_editor,
                game.spawner_editor,
                mmb_events,
                diagnostics_overlay=overlay,
                spells_editor=getattr(game, 'spells_editor', None),
                item_editor=getattr(game, 'item_editor', None),
                fsm_visible=True,
            )
        return True

    # Si el editor de entidades está activo
    if getattr(getattr(game, 'entities_editor', None), 'model', None) and game.entities_editor.model.visible:
        for event in events:
            game.entities_editor.handle_event(event)
        return True

    # Si el editor de spawner está activo...
    if getattr(getattr(game, 'spawner_editor', None), 'model', None) and getattr(game.spawner_editor.model, 'visible', False):
        for i, event in enumerate(events):
            try:
                handled = game.spawner_editor.handle_event(event)
                if handled:
                    if event.type in (pygame.MOUSEBUTTONDOWN, pygame.MOUSEBUTTONUP) and getattr(event, 'button', None) == 2:
                        continue
                    if event.type == pygame.MOUSEMOTION:
                        mmb_held = False
                        try:
                            mmb_held = _is_mmb_held(event)
                        except Exception:
                            mmb_held = False
                        if mmb_held:
                            continue
                    consumed_idx = getattr(game, '_events_consumed_idx_tmp', None)
                    if consumed_idx is None:
                        consumed_idx = set()
                        setattr(game, '_events_consumed_idx_tmp', consumed_idx)
                    consumed_idx.add(i)
            except Exception:
                pass

    # Si un editor de tiles está activo
    if getattr(getattr(game, 'tiles_editor', None), 'editor_state', None) and game.tiles_editor.editor_state.active:
        game.tiles_editor.handle(game.camera, game.map, events)
        return True

    # Si un editor de edificios está activo
    if getattr(getattr(game, 'buildings_editor', None), 'editor_state', None) and game.buildings_editor.editor_state.active:
        game.buildings_editor.handle(game.camera, game.buildings, events)
        return True

    # Si un editor de mapa está activo
    if getattr(getattr(game, 'map_editor', None), 'editor_state', None) and getattr(game.map_editor.editor_state, 'active', False):
        game.map_editor.handle(game.camera, game.map, events)
        return True

    return False
