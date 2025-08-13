import roguelike_engine.config.config as config
from roguelike_engine.map.events.events import handle_expand_dungeon
import logging
logger = logging.getLogger(__name__)
import pygame
from roguelike_editors.fsm.fms_editor_events import FMSEventHandler

def handle_keyboard(event, state, camera, clock, menu, entities, tiles_editor, buildings_editor, map_editor, map_manager):
    if event.type == pygame.KEYDOWN:
        # ESC → toggle menú global
        if event.key == pygame.K_ESCAPE:
            menu.show_menu = not menu.show_menu
            return
        
        if event.key == pygame.K_F3:
            handle_expand_dungeon(event, map_manager, entities)

        # Alternar menú con tecla dinámica de 'pause'
        pause_key = menu.input_config.get_key('pause')
        if event.key == pause_key:
            menu.show_menu = not menu.show_menu

        elif menu.show_menu:
            result = menu.handle_input(event)
            if result:
                menu.execute_menu_option(result, state)

        # ---------- TEST / DEBUG ---------- #
        elif event.key == menu.input_config.get_key('toggle_building_editor'):
            if hasattr(state, "editor"):
                # Alternar editor, pero NO abrir el picker automáticamente
                new_val = not state.editor.active
                state.editor.active        = new_val
                state.editor.picker_active = False
                logger.debug("🛠️ Building Editor ON (picker oculto)"  if new_val else
                      "🛑 Building Editor OFF (picker cerrado)")
            return

        elif event.key == pygame.K_F9:
            config.DEBUG = not config.DEBUG
            logger.debug(f"🧪 DEBUG {'activado' if config.DEBUG else 'desactivado'}")

        elif FMSEventHandler.handle_event(event):
            # FMSEventHandler consumió el evento (F12: FSM Editor)
            return

        # Toggle Item Editor (F7)
        elif event.key == menu.input_config.get_key('toggle_item_editor'):
            new_val = not state.item_editor_state.visible
            state.item_editor_state.visible = new_val
            logger.debug("🛠️ Item Editor ON" if new_val else "🛑 Item Editor OFF")
            return

        # ---------- TILE-EDITOR (F8) --------- #
        elif event.key == pygame.K_F8:
            # Alternar Tile Editor con lógica del manager
            tiles_editor.toggle()
            return  # evitamos más atajos este frame

        elif event.key == pygame.K_F11:
            # Toggle Map Editor
            map_editor.toggle()
            return