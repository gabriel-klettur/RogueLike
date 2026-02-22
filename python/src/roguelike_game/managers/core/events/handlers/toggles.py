import pygame
import roguelike_engine.config.config as config
from roguelike_engine.diagnostics.recorder import recorder


def handle_toggles(game, events, close_all_editors, open_editor_exclusive) -> bool:
    for event in events:
        if event.type != pygame.KEYDOWN:
            continue
        # Lighting: Alt+F2 toggles point lights manager (independent from overlay)
        if event.key == pygame.K_F2:
            try:
                mods = pygame.key.get_mods()
            except Exception:
                mods = 0
            if mods & pygame.KMOD_ALT:
                try:
                    from roguelike_engine.rendering.lighting import get_global_lighting
                    lm = get_global_lighting()
                    lm.set_enabled(not lm.enabled)
                except Exception:
                    pass
                return True
        # Lighting Editor: Alt+F3 open/close editor panel (place before class selector to avoid conflicts)
        if event.key == pygame.K_F3:
            try:
                mods = pygame.key.get_mods()
            except Exception:
                mods = 0
            if mods & pygame.KMOD_LCTRL:
                try:
                    le = getattr(game, 'lighting_editor', None)
                    if le is None:
                        try:
                            from roguelike_game.managers.editors.lighting_editor_manager import LightingEditorManager
                            game.lighting_editor = LightingEditorManager(game)
                            le = game.lighting_editor
                            try:
                                # Attach to renderer for drawing
                                if hasattr(game, 'renderer') and game.renderer is not None:
                                    game.renderer.lighting_editor = le
                            except Exception:
                                pass
                        except Exception:
                            le = None
                    if le is not None:
                        vis = bool(getattr(getattr(le, 'model', None), 'visible', False))
                        if vis:
                            le.model.visible = False
                        else:
                            # Close other editors, then show lighting editor
                            try:
                                from .editors_common import close_all_editors
                                close_all_editors(game)
                            except Exception:
                                pass
                            le.model.visible = True
                except Exception:
                    pass
                return True
        # Toggle class selector
        if event.key == game.input_config.get_key('select_class'):
            game.class_selector.show = not game.class_selector.show
            try:
                game.state.class_selector_open = bool(game.class_selector.show)
            except Exception:
                pass
            return True
        # Spawner
        if event.key == game.input_config.get_key('toggle_spawner_editor'):
            try:
                is_vis = bool(getattr(getattr(game.spawner_editor, 'model', None), 'visible', False))
            except Exception:
                is_vis = False
            if is_vis:
                close_all_editors(game)
            else:
                open_editor_exclusive(game, 'spawner')
            return True
        # Spells
        if event.key == game.input_config.get_key('toggle_spells_editor'):
            try:
                is_vis = bool(getattr(getattr(game.spells_editor, 'model', None), 'visible', False))
            except Exception:
                is_vis = False
            if is_vis:
                close_all_editors(game)
            else:
                open_editor_exclusive(game, 'spells')
            return True
        # Particles (Left Alt required)
        if event.key == game.input_config.get_key('toggle_particles_editor'):
            try:
                ctrl = bool(pygame.key.get_mods() & pygame.KMOD_CTRL)
            except Exception:
                ctrl = False
            if ctrl:
                try:
                    is_vis = bool(getattr(getattr(game, 'particles_editor', None), 'model', None) and getattr(game.particles_editor.model, 'visible', False))
                except Exception:
                    is_vis = False
                if is_vis:
                    close_all_editors(game)
                else:
                    open_editor_exclusive(game, 'particles')
                return True
        # Entities
        if event.key == game.input_config.get_key('toggle_entities_editor'):
            try:
                is_vis = bool(getattr(getattr(game, 'entities_editor', None), 'model', None) and getattr(game.entities_editor.model, 'visible', False))
            except Exception:
                is_vis = False
            if is_vis:
                close_all_editors(game)
            else:
                open_editor_exclusive(game, 'entities')
            return True
        # Inventory
        if event.key == game.input_config.get_key('toggle_inventory_editor'):
            try:
                is_vis = bool(getattr(getattr(game, 'inventory_editor', None), 'model', None) and getattr(game.inventory_editor.model, 'visible', False))
            except Exception:
                is_vis = False
            if is_vis:
                close_all_editors(game)
            else:
                open_editor_exclusive(game, 'inventory')
            return True
        # Items
        if event.key == game.input_config.get_key('toggle_item_editor'):
            try:
                is_vis = bool(getattr(getattr(game, 'item_editor', None), 'model', None) and getattr(game.item_editor.model, 'visible', False))
            except Exception:
                is_vis = False
            if is_vis:
                close_all_editors(game)
            else:
                open_editor_exclusive(game, 'items')
            return True
        # Debug overlay
        if event.key == game.input_config.get_key('toggle_debug_overlay'):
            new_val = not config.DEBUG
            config.DEBUG = new_val
            try:
                recorder.on_toggle(new_val, game)
            except Exception:
                pass
            return True
        # FSM editor
        if event.key == game.input_config.get_key('toggle_fsm_editor'):
            try:
                import roguelike_engine.config.config as cfg
                is_vis = bool(getattr(cfg, 'DEBUG_ENTITIES', False))
            except Exception:
                is_vis = False
            if is_vis:
                close_all_editors(game)
            else:
                open_editor_exclusive(game, 'fsm')
            return True
        # Tiles editor
        if event.key == game.input_config.get_key('toggle_tile_editor'):
            try:
                is_active = bool(getattr(getattr(game, 'tiles_editor', None), 'editor_state', None) and getattr(game.tiles_editor.editor_state, 'active', False))
            except Exception:
                is_active = False
            if is_active:
                close_all_editors(game)
            else:
                open_editor_exclusive(game, 'tiles')
            return True
        # Buildings editor
        if event.key == game.input_config.get_key('toggle_building_editor'):
            try:
                is_active = bool(getattr(getattr(game, 'buildings_editor', None), 'editor_state', None) and getattr(game.buildings_editor.editor_state, 'active', False))
            except Exception:
                is_active = False
            if is_active:
                close_all_editors(game)
            else:
                open_editor_exclusive(game, 'buildings')
            return True
        # Map editor
        if event.key == game.input_config.get_key('toggle_map_editor'):
            try:
                is_active = bool(getattr(getattr(game, 'map_editor', None), 'editor_state', None) and getattr(game.map_editor.editor_state, 'active', False))
            except Exception:
                is_active = False
            if is_active:
                close_all_editors(game)
            else:
                open_editor_exclusive(game, 'map')
            return True
    return False
