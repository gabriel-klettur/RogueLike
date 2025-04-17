import pygame

def handle_tile_editor_events(state, editor_state, tile_editor):
    """
    Consume TODOS los eventos de pygame cuando el Tile‑Editor está activo.
    """
    for ev in pygame.event.get():
        # --------------- SALIR ---------------
        if ev.type == pygame.QUIT:
            state.running = False

        elif ev.type == pygame.KEYDOWN:
            if ev.key == pygame.K_ESCAPE:
                editor_state.active = False
                state.tile_editor_active = False
                editor_state.selected_tile = None
                editor_state.picker_open   = False
                print("🔚 Tile‑Editor OFF")
            elif ev.key == pygame.K_F8:
                new_val = not getattr(state, "tile_editor_active", False)
                state.tile_editor_active = new_val
                if hasattr(state, "tile_editor_state"):
                    state.tile_editor_state.active = new_val        # ← NUEVO
                print("🟩 Tile‑Editor ON" if new_val else "🟥 Tile‑Editor OFF")
                return 

        # --------------- RATÓN ---------------
        elif ev.type == pygame.MOUSEBUTTONDOWN:
            if ev.button == 1:                      # click izq
                if editor_state.picker_open:
                    tile_editor.picker.handle_click(ev.pos)
                else:
                    tile_editor.select_tile_at(ev.pos)

        elif ev.type == pygame.MOUSEWHEEL and editor_state.picker_open:
            tile_editor.picker.scroll(ev.y)
