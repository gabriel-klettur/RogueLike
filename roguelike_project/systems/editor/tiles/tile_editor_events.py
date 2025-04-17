import pygame

def handle_tile_editor_events(state, editor_state, tile_editor):
    """
    Consume TODOS los eventos cuando el Tile‑Editor está activo.
    Añadido: clic‑derecho para arrastrar el Tile‑Picker.
    """
    for ev in pygame.event.get():
        # ---------- SALIR ---------- #
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
                new_val = not state.tile_editor_active
                state.tile_editor_active = new_val
                editor_state.active      = new_val
                if not new_val:
                    editor_state.picker_open = False
                    editor_state.selected_tile = None
                print("🟩 Tile‑Editor ON" if new_val else "🟥 Tile‑Editor OFF")
                return

        # ---------- RATÓN ---------- #
        elif ev.type == pygame.MOUSEBUTTONDOWN:
            if ev.button == 1:  # clic izquierdo
                if editor_state.picker_open:
                    handled = tile_editor.picker.handle_click(ev.pos, button=1)
                    if not handled:
                        tile_editor.select_tile_at(ev.pos)
                else:
                    tile_editor.select_tile_at(ev.pos)

            elif ev.button == 3:  # clic derecho (para drag)
                if editor_state.picker_open:
                    handled = tile_editor.picker.handle_click(ev.pos, button=3)
                    if handled:
                        continue   # se empezó un drag; no seleccionar tile

        elif ev.type == pygame.MOUSEMOTION:
            if editor_state.picker_open and tile_editor.picker.dragging:
                tile_editor.picker.drag(ev.pos)

        elif ev.type == pygame.MOUSEBUTTONUP:
            if ev.button == 3 and editor_state.picker_open:
                tile_editor.picker.stop_drag()

        elif ev.type == pygame.MOUSEWHEEL and editor_state.picker_open:
            tile_editor.picker.scroll(ev.y)
