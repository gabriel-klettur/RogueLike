import pygame
from roguelike_project.systems.editor.buildings.model.persistence.json_handler import save_buildings_to_json
from roguelike_project.config import BUILDINGS_DATA_PATH

def handle_building_editor_events(state, editor_state, editor_controller):
    """
    Consume todo el input cuando el Building‑Editor está activo.
    """
    for event in pygame.event.get():
        if event.type == pygame.QUIT:
            state.running = False

        elif event.type == pygame.KEYDOWN:
            # Esc
            if event.key == pygame.K_ESCAPE:
                editor_state.active = False
                editor_state.selected_building = None
                editor_state.dragging = editor_state.resizing = editor_state.split_dragging = False
                print("🔚 Building‑Editor OFF")
                save_buildings_to_json(state.buildings, BUILDINGS_DATA_PATH, z_state=state.z_state)

            # F10
            elif event.key == pygame.K_F10:
                editor_state.active = not editor_state.active
                print("🛠️ Building‑Editor ON" if editor_state.active else "🛑 Building‑Editor OFF")
                if not editor_state.active:
                    save_buildings_to_json(state.buildings, BUILDINGS_DATA_PATH, z_state=state.z_state)

            # Ctrl+S
            elif event.key == pygame.K_s and (event.mod & pygame.KMOD_CTRL):
                save_buildings_to_json(state.buildings, BUILDINGS_DATA_PATH, z_state=state.z_state)
                print("💾 Buildings guardados (Ctrl+S)")

            # N: colocar
            elif event.key == pygame.K_n:
                editor_controller.placer_tool.place_building_at_mouse()

            # Supr: borrar
            elif event.key == pygame.K_DELETE:
                editor_controller.delete_tool.delete_building_at_mouse()

        elif event.type == pygame.MOUSEBUTTONDOWN:
            mx, my = pygame.mouse.get_pos()
            editor_controller.on_mouse_down((mx, my), event.button)

        elif event.type == pygame.MOUSEBUTTONUP:
            editor_controller.on_mouse_up(event.button)

        elif event.type == pygame.MOUSEMOTION:
            editor_controller.on_mouse_motion(event.pos)
