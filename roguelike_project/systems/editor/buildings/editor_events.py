# roguelike_project/systems/editor/buildings/editor_events.py
import pygame
from roguelike_project.systems.editor.json_handler import save_buildings_to_json
from roguelike_project.config import BUILDINGS_DATA_PATH

def handle_editor_events(state, editor_state, building_editor):
    for event in pygame.event.get():
        # ---------- SALIR ----------
        if event.type == pygame.QUIT:
            state.running = False

        # ---------- TECLADO ----------
        elif event.type == pygame.KEYDOWN:
            if event.key == pygame.K_ESCAPE:
                editor_state.active = False
                editor_state.selected_building = None
                editor_state.dragging = editor_state.resizing = editor_state.split_dragging = False
                print("🔚 Modo editor desactivado")
                save_buildings_to_json(state.buildings, BUILDINGS_DATA_PATH, z_state=state.z_state)

            elif event.key == pygame.K_F10:
                editor_state.active = not editor_state.active
                print("🛠️ Modo editor activado" if editor_state.active else "🛑 Modo editor desactivado")
                if not editor_state.active:
                    save_buildings_to_json(state.buildings, BUILDINGS_DATA_PATH, z_state=state.z_state)

        # ---------- RATÓN ----------
        elif event.type == pygame.MOUSEBUTTONDOWN:
            mx, my = pygame.mouse.get_pos()
            building_editor.on_mouse_down((mx, my), event.button)

        elif event.type == pygame.MOUSEBUTTONUP:
            building_editor.on_mouse_up(event.button)

        elif event.type == pygame.MOUSEMOTION:
            building_editor.on_mouse_motion(event.pos)
