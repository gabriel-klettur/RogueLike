# roguelike_project/systems/editor/buildings/editor_events.py
import pygame
from roguelike_project.systems.editor.json_handler import save_buildings_to_json
from roguelike_project.config import BUILDINGS_DATA_PATH


def handle_editor_events(state, editor_state, building_editor):
    """
    Consume todo el input cuando el Building‑Editor está activo.
    Atajos:
        •   Esc         → sale del editor
        •   F10         → toggle del modo
        •   Ctrl + S    → guarda buildings.json
        •   N           → coloca un edificio por defecto en el mouse
        •   Supr        → borra edificio bajo el mouse
    """
    for event in pygame.event.get():
        # ---------- cerrar juego ----------
        if event.type == pygame.QUIT:
            state.running = False

        # ---------- teclado ----------
        elif event.type == pygame.KEYDOWN:

            # --- control de modo ---
            if event.key == pygame.K_ESCAPE:
                editor_state.active = False
                editor_state.selected_building = None
                editor_state.dragging = editor_state.resizing = editor_state.split_dragging = False
                print("🔚 Building‑Editor OFF")
                save_buildings_to_json(state.buildings, BUILDINGS_DATA_PATH, z_state=state.z_state)

            elif event.key == pygame.K_F10:
                editor_state.active = not editor_state.active
                print("🛠️ Building‑Editor ON" if editor_state.active else "🛑 Building‑Editor OFF")
                if not editor_state.active:
                    save_buildings_to_json(state.buildings, BUILDINGS_DATA_PATH, z_state=state.z_state)

            # --- atajos rápidos ---
            elif event.key == pygame.K_s and (event.mod & pygame.KMOD_CTRL):
                save_buildings_to_json(state.buildings, BUILDINGS_DATA_PATH, z_state=state.z_state)
                print("💾 Buildings guardados (Ctrl+S)")

            elif event.key == pygame.K_n:
                if hasattr(state, "placer_tool"):
                    state.placer_tool.place_building_at_mouse()

            elif event.key == pygame.K_DELETE:
                if hasattr(state, "delete_tool"):
                    state.delete_tool.delete_building_at_mouse()

        # ---------- ratón ----------
        elif event.type == pygame.MOUSEBUTTONDOWN:
            mx, my = pygame.mouse.get_pos()
            building_editor.on_mouse_down((mx, my), event.button)

        elif event.type == pygame.MOUSEBUTTONUP:
            building_editor.on_mouse_up(event.button)

        elif event.type == pygame.MOUSEMOTION:
            building_editor.on_mouse_motion(event.pos)
