# roguelike_project/editor/editor_events.py

import pygame

def handle_editor_events(state, editor_state, building_editor):
    for event in pygame.event.get():
        if event.type == pygame.QUIT:
            state.running = False

        elif event.type == pygame.KEYDOWN:
            if event.key == pygame.K_ESCAPE:
                editor_state.active = False
                editor_state.selected_building = None
                editor_state.dragging = False
                print("🔚 Modo editor desactivado")

            elif event.key == pygame.K_F10:
                editor_state.active = not editor_state.active
                print("🛠️ Modo editor activado" if editor_state.active else "🛑 Modo editor desactivado")

        elif event.type == pygame.MOUSEBUTTONDOWN:
            if event.button == 3:  # Mouse derecho
                building_editor.handle_mouse_down(pygame.mouse.get_pos())

        elif event.type == pygame.MOUSEBUTTONUP:
            if event.button == 3:
                building_editor.handle_mouse_up()
