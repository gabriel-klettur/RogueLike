import pygame
from roguelike_project.systems.editor.json_handler import save_buildings_to_json
from roguelike_project.config import BUILDINGS_DATA_PATH

def handle_editor_events(state, editor_state, building_editor):
    for event in pygame.event.get():
        if event.type == pygame.QUIT:
            state.running = False

        elif event.type == pygame.KEYDOWN:
            if event.key == pygame.K_ESCAPE:
                editor_state.active = False
                editor_state.selected_building = None
                editor_state.dragging = False
                editor_state.resizing = False
                print("🔚 Modo editor desactivado")

                # 💾 Guardar automáticamente al salir
                save_buildings_to_json(
                    state.buildings,
                    BUILDINGS_DATA_PATH,
                    z_state=state.z_state
                )

            elif event.key == pygame.K_F10:
                editor_state.active = not editor_state.active
                print("🛠️ Modo editor activado" if editor_state.active else "🛑 Modo editor desactivado")

                # 💾 Guardar si se desactiva el editor
                if not editor_state.active:
                    save_buildings_to_json(
                        state.buildings,
                        BUILDINGS_DATA_PATH,
                        z_state=state.z_state
                    )

        elif event.type == pygame.MOUSEBUTTONDOWN:
            mx, my = pygame.mouse.get_pos()
            if event.button == 1:  # 👈 Botón izquierdo: para los botones + y -
                building_editor.z_tool.handle_mouse_click((mx, my))

            if event.button == 3:  # Botón derecho: para seleccionar edificio
                building_editor.handle_mouse_down((mx, my))

        elif event.type == pygame.MOUSEBUTTONUP:
            if event.button == 3:
                if editor_state.selected_building:
                    if editor_state.dragging or editor_state.resizing:
                        b = editor_state.selected_building
                        print(f"💾 Guardando edificio: {b.image_path} en ({int(b.x)}, {int(b.y)})")
                        save_buildings_to_json(
                            state.buildings,
                            BUILDINGS_DATA_PATH,
                            z_state=state.z_state
                        )
                building_editor.handle_mouse_up()

        elif event.type == pygame.MOUSEMOTION:
            if editor_state.resizing:
                building_editor.update_resizing(event.pos)
