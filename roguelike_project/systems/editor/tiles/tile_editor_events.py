# roguelike_project/systems/editor/tiles/tile_editor_events.py

import pygame

def handle_tile_editor_events(state, editor_state, tile_editor):
    """
    Consume TODOS los eventos cuando el Tile‑Editor está activo.
    Incluye:
      - Toolbar (select, brush, eyedropper)
      - Select para abrir paleta
      - Brush para pintar (drag)
      - Eyedropper para copiar
    """
    for ev in pygame.event.get():
        # ---------- SALIR ---------- #
        if ev.type == pygame.QUIT:
            state.running = False

        # ---------- TECLADO ---------- #
        elif ev.type == pygame.KEYDOWN:
            if ev.key == pygame.K_ESCAPE:
                editor_state.active = False
                state.tile_editor_active = False
                editor_state.selected_tile = None
                editor_state.picker_open   = False
                editor_state.brush_dragging = False
                print("🔚 Tile‑Editor OFF")
            elif ev.key == pygame.K_F8:
                new_val = not state.tile_editor_active
                state.tile_editor_active = new_val
                editor_state.active      = new_val
                if not new_val:
                    editor_state.picker_open = False
                    editor_state.selected_tile = None
                    editor_state.brush_dragging = False
                print("🟩 Tile‑Editor ON" if new_val else "🟥 Tile‑Editor OFF")
                return

        # ---------- RATÓN ---------- #
        elif ev.type == pygame.MOUSEBUTTONDOWN:
            # 1) Toolbar: override de herramienta
            if ev.button == 1 and tile_editor.toolbar.handle_click(ev.pos):
                continue

            tool = editor_state.current_tool

            # 2) Select (abrir paleta / seleccionar tile)
            if tool == "select" and ev.button == 1:
                if editor_state.picker_open:
                    handled = tile_editor.picker.handle_click(ev.pos, button=1)
                    if not handled:
                        tile_editor.select_tile_at(ev.pos)
                else:
                    tile_editor.select_tile_at(ev.pos)

            # 3) Brush (pintar)
            elif tool == "brush" and ev.button == 1:
                editor_state.brush_dragging = True
                tile_editor.apply_brush(ev.pos)

            # 4) Eyedropper (copiar)
            elif tool == "eyedropper" and ev.button == 1:
                tile_editor.apply_eyedropper(ev.pos)

            # 5) Drag de la paleta con botón derecho
            elif ev.button == 3 and editor_state.picker_open:
                handled = tile_editor.picker.handle_click(ev.pos, button=3)
                if handled:
                    continue

        elif ev.type == pygame.MOUSEMOTION:
            # Brush → pintar al arrastrar
            if editor_state.current_tool == "brush" and editor_state.brush_dragging:
                tile_editor.apply_brush(ev.pos)

            # Paleta → scroll/drag
            elif editor_state.picker_open and tile_editor.picker.dragging:
                tile_editor.picker.drag(ev.pos)

        elif ev.type == pygame.MOUSEBUTTONUP:
            # liberar brush
            if ev.button == 1 and editor_state.current_tool == "brush":
                editor_state.brush_dragging = False

            # parar drag de paleta
            if ev.button == 3 and editor_state.picker_open:
                tile_editor.picker.stop_drag()

        elif ev.type == pygame.MOUSEWHEEL and editor_state.picker_open:
            tile_editor.picker.scroll(ev.y)
