from __future__ import annotations


def render_editors(manager) -> None:
    """Render editors UI. Delegated from RendererManager._render_editors.

    Keeps behavior: when tile brush is active, re-render map and z-entities.
    
    OPTIMIZATION: Early exit checks to avoid any work when editors are inactive.
    """
    # Check if ANY editor is active before doing any work
    tiles_active = manager.tiles_editor.editor_state.active
    buildings_active = manager.buildings_editor.editor_state.active
    map_active = manager.map_editor.editor_state.active
    
    # Tiles Editor
    if tiles_active:
        if manager.tiles_editor.editor_state.current_tool == "brush":
            manager._render_map(manager.camera, manager.screen, manager.map)
            manager._render_z_entities(manager._last_state, manager.camera, manager.screen, manager.entities)
        manager.tiles_editor.view.render(manager.screen, manager.camera, manager.map)

    # Buildings Editor
    if buildings_active:
        manager.buildings_editor.render(manager.screen, manager.camera, manager.entities.buildings)

    # Map Editor
    if map_active:
        manager.map_editor.render(manager.screen, manager.camera, manager.map)

    # Lighting Editor panel (only check if no main editor is active to save attr lookups)
    if not (tiles_active or buildings_active or map_active):
        try:
            le = getattr(manager, 'lighting_editor', None)
            if le and getattr(getattr(le, 'model', None), 'visible', False):
                le.draw(manager.screen)
        except Exception:
            pass

    # FSM Editor UI (debug mode only)
    try:
        import roguelike_engine.config.config as config
        if getattr(config, "DEBUG_ENTITIES", False):
            manager._render_fsm_editor_ui(manager.screen)
    except Exception:
        pass

    try:
        from roguelike_editors.fsm.fsm_editor_events import FsmEditorEventHandler
        FsmEditorEventHandler.render(manager.screen)
    except Exception:
        pass
