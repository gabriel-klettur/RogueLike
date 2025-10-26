from __future__ import annotations


def render_editors(manager) -> None:
    """Render editors UI. Delegated from RendererManager._render_editors.

    Keeps behavior: when tile brush is active, re-render map and z-entities.
    """
    if manager.tiles_editor.editor_state.active:
        if manager.tiles_editor.editor_state.current_tool == "brush":
            manager._render_map(manager.camera, manager.screen, manager.map)
            manager._render_z_entities(manager._last_state, manager.camera, manager.screen, manager.entities)
        manager.tiles_editor.view.render(manager.screen, manager.camera, manager.map)

    if manager.buildings_editor.editor_state.active:
        manager.buildings_editor.render(manager.screen, manager.camera, manager.entities.buildings)

    if manager.map_editor.editor_state.active:
        manager.map_editor.render(manager.screen, manager.camera, manager.map)

    # Lighting Editor panel
    try:
        le = getattr(manager, 'lighting_editor', None)
        if le and getattr(getattr(le, 'model', None), 'visible', False):
            le.draw(manager.screen)
    except Exception:
        pass

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
