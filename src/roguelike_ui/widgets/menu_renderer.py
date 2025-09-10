import pygame


class MenuRenderer:
    """
    Render profesional del menú con tamaño dinámico y estética moderna.
    - Fondo atenuado (overlay)
    - Panel con esquinas redondeadas y sombra
    - Resaltado de opción seleccionada
    - Dimensiones calculadas según contenido
    """
    def __init__(self, font_size=36):
        # Tipografía base
        self.font = pygame.font.SysFont("Arial", font_size)

        # Estilos
        self.panel_bg = (22, 24, 28)
        self.panel_alpha = 235
        self.overlay_color = (0, 0, 0, 140)
        self.text_color = (230, 233, 240)
        self.text_color_dim = (180, 185, 195)
        self.accent_color = (255, 200, 0)
        self.highlight_color = (255, 200, 0, 38)  # bajo alfa para pill
        self.border_color = (255, 220, 0)

        # Layout
        self.padding_x = 28
        self.padding_y = 24
        self.item_gap = max(8, font_size // 3)
        self.line_height = font_size + max(6, font_size // 6)
        self.radius = 12
        self.shadow_offset = (5, 6)

        # Registro de blits para pruebas/depuración
        self.last_blits = []

    # ---- Utilidades de dibujo ----
    def _draw_overlay(self, screen):
        w, h = screen.get_size()
        overlay = pygame.Surface((w, h), pygame.SRCALPHA)
        overlay.fill(self.overlay_color)
        surface_to_blit = overlay._surf if hasattr(overlay, '_surf') else overlay
        screen.blit(surface_to_blit, (0, 0))
        return pygame.Rect(0, 0, w, h)

    def _draw_shadow(self, screen, rect):
        # Sombra simple desplazada
        sx, sy = self.shadow_offset
        shadow_rect = rect.move(sx, sy)
        shadow_surf = pygame.Surface((shadow_rect.width, shadow_rect.height), pygame.SRCALPHA)
        pygame.draw.rect(shadow_surf, (0, 0, 0, 110), shadow_surf.get_rect(), border_radius=self.radius + 2)
        surface_to_blit = shadow_surf._surf if hasattr(shadow_surf, '_surf') else shadow_surf
        screen.blit(surface_to_blit, shadow_rect.topleft)

    def _draw_panel(self, size):
        w, h = size
        panel = pygame.Surface((w, h), pygame.SRCALPHA)
        rect = panel.get_rect()
        color = (*self.panel_bg, self.panel_alpha)
        pygame.draw.rect(panel, color, rect, border_radius=self.radius)
        return panel

    def _measure_menu(self, options):
        max_w = 0
        for opt in options:
            tw, th = self.font.size(opt)
            max_w = max(max_w, tw)
        width = self.padding_x * 2 + max_w + 8  # extra para acento
        if options:
            inner_h = len(options) * self.line_height + (len(options) - 1) * self.item_gap
        else:
            inner_h = self.line_height
        height = self.padding_y * 2 + inner_h
        return width, height

    def _center_rect(self, screen, size):
        sw, sh = screen.get_size()
        w, h = size
        x = (sw - w) // 2
        y = (sh - h) // 2
        return pygame.Rect(x, y, w, h)

    # ---- Render principal ----
    def draw(self, screen, selected, options, scroll_offset: int = 0, panel_top_min: int | None = None):
        """
        Dibuja el menú principal con estilo profesional.
        Devuelve el rect total actualizado (usamos el overlay a pantalla completa).
        """
        # 1) Overlay
        overlay_rect = self._draw_overlay(screen)

        # 2) Medidas dinámicas
        w, h = self._measure_menu(options)
        # Limitar a la pantalla con margen de seguridad
        sw, sh = screen.get_size()
        w = min(w, int(sw * 0.9))
        h = min(h, int(sh * 0.85))
        panel_rect = self._center_rect(screen, (w, h))
        # Si se pide que el panel no suba de cierta Y, empujarlo hacia abajo
        if isinstance(panel_top_min, int) and panel_rect.top < panel_top_min:
            panel_rect.top = panel_top_min
        # Exponer el rect del panel para overlays externos (logo, etc.)
        self.last_menu_panel_rect = panel_rect

        # 3) Sombra y panel
        self._draw_shadow(screen, panel_rect)
        panel = self._draw_panel((w, h))

        # 4) Items (con recorte y posible scroll)
        self.last_blits = []
        total = len(options)
        inner_height = h - self.padding_y * 2
        block_h = self.line_height + self.item_gap
        max_visible = max(1, (inner_height + self.item_gap) // block_h)

        if total <= max_visible:
            start = 0
            end = total
        else:
            # Clamp del offset
            max_offset = max(0, total - max_visible)
            scroll_offset = max(0, min(scroll_offset, max_offset))
            start = scroll_offset
            end = start + max_visible

        # Dibujo de opciones visibles
        y = self.padding_y
        for i in range(start, end):
            option = options[i]
            is_sel = (i == selected)
            if is_sel:
                pill_rect = pygame.Rect(0, 0, w - self.padding_x * 2, self.line_height)
                pill_rect.topleft = (self.padding_x, y)
                pygame.draw.rect(panel, self.highlight_color, pill_rect, border_radius=self.radius // 2)
                accent_rect = pygame.Rect(self.padding_x - 6, y, 4, self.line_height)
                pygame.draw.rect(panel, self.accent_color, accent_rect, border_radius=2)

            color = self.accent_color if is_sel else self.text_color
            text = self.font.render(option, True, color)
            tx = self.padding_x + 12
            ty = y + (self.line_height - text.get_height()) // 2
            panel.blit(text, (tx, ty))
            self.last_blits.append((tx, ty))
            y += block_h

        # Dibujar scrollbar si hay overflow
        if total > max_visible:
            track_x = w - self.padding_x // 2 - 6
            track_y = self.padding_y
            track_w = 6
            track_h = inner_height
            # Pista
            pygame.draw.rect(panel, (255, 255, 255, 28), pygame.Rect(track_x, track_y, track_w, track_h), border_radius=3)
            # Thumb
            thumb_h = max(24, int(track_h * (max_visible / total)))
            max_thumb_top = track_y + track_h - thumb_h
            if total - max_visible == 0:
                thumb_top = track_y
            else:
                thumb_top = int(track_y + (track_h - thumb_h) * (start / (total - max_visible)))
            thumb_top = max(track_y, min(thumb_top, max_thumb_top))
            pygame.draw.rect(panel, self.accent_color, pygame.Rect(track_x, thumb_top, track_w, thumb_h), border_radius=3)

        # 5) Blit
        surface_to_blit = panel._surf if hasattr(panel, '_surf') else panel
        screen.blit(surface_to_blit, panel_rect.topleft)

        # Devolvemos overlay_rect para garantizar repintado completo (overlay + panel)
        return overlay_rect

    def draw_confirm_dialog(self, screen, lines: list[str], *, hover_yes: bool = False, hover_cancel: bool = False):
        """
        Dibuja un cuadro de confirmación modal con botones Sí/Cancelar.
        Expone last_confirm_layout: {'panel_rect','yes_rect','cancel_rect'}
        """
        # Overlay adicional para modal
        overlay_rect = self._draw_overlay(screen)

        # Medir contenido
        max_w = 0
        for line in lines:
            tw, _ = self.font.size(line)
            max_w = max(max_w, tw)
        # Botones
        yes_t = self.font.render("Sí, borrar", True, self.text_color)
        cancel_t = self.font.render("Cancelar", True, self.text_color)
        pad_btn_x = 18
        btn_h = self.line_height
        yes_w = yes_t.get_width() + pad_btn_x * 2
        cancel_w = cancel_t.get_width() + pad_btn_x * 2
        gap = 20
        buttons_w = yes_w + gap + cancel_w

        w = self.padding_x * 2 + max(max_w, buttons_w)
        rows_h = (len(lines) or 1) * self.line_height + max(0, (len(lines) - 1)) * (self.item_gap - 2)
        h = self.padding_y * 2 + rows_h + self.item_gap + btn_h
        sw, sh = screen.get_size()
        w = min(w, int(sw * 0.8))
        h = min(h, int(sh * 0.5))
        panel_rect = self._center_rect(screen, (w, h))

        # Sombra y panel
        self._draw_shadow(screen, panel_rect)
        panel = self._draw_panel((w, h))

        # Texto
        y = self.padding_y
        for line in lines:
            t = self.font.render(line, True, self.text_color)
            ty = y + (self.line_height - t.get_height()) // 2
            panel.blit(t, (self.padding_x, ty))
            y += self.line_height + (self.item_gap - 2)

        # Botonera
        btn_y = h - self.padding_y - btn_h
        base_x = (w - buttons_w) // 2
        yes_rect_local = pygame.Rect(base_x, btn_y, yes_w, btn_h)
        cancel_rect_local = pygame.Rect(base_x + yes_w + gap, btn_y, cancel_w, btn_h)
        btn_bg = (255, 255, 255, 22)
        # Sí
        pygame.draw.rect(panel, btn_bg, yes_rect_local, border_radius=self.radius // 2)
        if hover_yes:
            pygame.draw.rect(panel, self.border_color, yes_rect_local, width=2, border_radius=self.radius // 2)
        yx = yes_rect_local.x + (yes_rect_local.width - yes_t.get_width()) // 2
        yy = yes_rect_local.y + (yes_rect_local.height - yes_t.get_height()) // 2
        panel.blit(yes_t, (yx, yy))
        # Cancelar
        pygame.draw.rect(panel, btn_bg, cancel_rect_local, border_radius=self.radius // 2)
        if hover_cancel:
            pygame.draw.rect(panel, self.border_color, cancel_rect_local, width=2, border_radius=self.radius // 2)
        cx = cancel_rect_local.x + (cancel_rect_local.width - cancel_t.get_width()) // 2
        cy = cancel_rect_local.y + (cancel_rect_local.height - cancel_t.get_height()) // 2
        panel.blit(cancel_t, (cx, cy))

        # Blit y layout
        surface_to_blit = panel._surf if hasattr(panel, '_surf') else panel
        screen.blit(surface_to_blit, panel_rect.topleft)
        self.last_confirm_layout = {
            'panel_rect': panel_rect,
            'yes_rect': yes_rect_local.move(panel_rect.topleft),
            'cancel_rect': cancel_rect_local.move(panel_rect.topleft),
        }
        return overlay_rect

    def draw_saves_panel(self, screen,
                          selected: int,
                          items: list[str],
                          detail_lines: list[str],
                          *,
                          row_scroll_offset: int = 0,
                          hovered_index: int | None = None,
                          fixed_panel_size: tuple[int, int] | None = None,
                          fixed_list_width: int | None = None,
                          fixed_details_width: int | None = None,
                          hover_details_name: bool = False,
                          editing_name: bool = False,
                          edit_name_text: str | None = None,
                          caret_pos: int = 0,
                          hover_load_button: bool = False,
                          hover_delete_button: bool = False,
                          select_all_edit: bool = False,
                          panel_top_min: int | None = None) -> pygame.Rect:
        """
        Dibuja un panel de "cargar partida" con estilo profesional, tamaño fijo opcional,
        scroll vertical en la lista y layout expuesto para hit-testing.

        - fixed_panel_size: (w, h) del panel si se quiere fijar. Si None, se calcula y clampa.
        - fixed_list_width / fixed_details_width: anchos fijos de columnas. Si None, se miden.
        - row_scroll_offset: desplazamiento de filas para scroll.
        - hovered_index: índice de fila con hover para borde.
        Guarda en self.last_saves_layout: panel_rect, row_rects, start, end.
        """
        # 1) Overlay
        overlay_rect = self._draw_overlay(screen)

        # 2) Medidas de columnas
        if fixed_list_width is None:
            list_max_w = 0
            for label in items:
                tw, _ = self.font.size(label)
                list_max_w = max(list_max_w, tw)
        else:
            list_max_w = int(fixed_list_width)

        if fixed_details_width is None:
            details_max_w = 0
            for line in detail_lines:
                tw, _ = self.font.size(line)
                details_max_w = max(details_max_w, tw)
        else:
            details_max_w = int(fixed_details_width)

        col_gap = 32
        n_items = len(items)

        # 3) Dimensiones del panel
        if fixed_panel_size is None:
            w = self.padding_x * 2 + list_max_w + col_gap + details_max_w + 12
            # Altura estimada por items (sin overflow); luego se clampa y se usa scroll
            inner_rows_h = (n_items or 1) * self.line_height + max(0, (n_items - 1)) * self.item_gap
            h = self.padding_y * 2 + max(inner_rows_h, self.line_height * 5)
            sw, sh = screen.get_size()
            w = min(w, int(sw * 0.95))
            h = min(h, int(sh * 0.85))
        else:
            w, h = fixed_panel_size

        panel_rect = self._center_rect(screen, (w, h))

        # 4) Sombra y panel
        self._draw_shadow(screen, panel_rect)
        panel = self._draw_panel((w, h))

        # 5) Lista con scroll
        self.last_saves_layout = {
            'panel_rect': panel_rect,
            'row_rects': {},  # idx -> screen rect
            'start': 0,
            'end': 0,
            'details_name_rect': None,
            'load_button_rect': None,
            'delete_button_rect': None,
        }

        list_x = self.padding_x
        list_y = self.padding_y
        inner_height = h - self.padding_y * 2
        block_h = self.line_height + self.item_gap
        max_visible = max(1, (inner_height + self.item_gap) // block_h)

        if n_items <= max_visible:
            start = 0
            end = n_items
            row_scroll_offset = 0
        else:
            max_offset = max(0, n_items - max_visible)
            row_scroll_offset = max(0, min(row_scroll_offset, max_offset))
            start = row_scroll_offset
            end = start + max_visible

        y = list_y
        for i in range(start, end):
            label = items[i]
            is_sel = (i == selected)
            # Highlight pill y acento
            if is_sel:
                pill_rect = pygame.Rect(list_x - 2, y, min(list_max_w + 16, w // 2), self.line_height)
                pygame.draw.rect(panel, self.highlight_color, pill_rect, border_radius=self.radius // 2)
                accent_rect = pygame.Rect(list_x - 8, y, 4, self.line_height)
                pygame.draw.rect(panel, self.accent_color, accent_rect, border_radius=2)

            color = self.accent_color if is_sel else self.text_color
            text = self.font.render(label, True, color)
            ty = y + (self.line_height - text.get_height()) // 2
            panel.blit(text, (list_x + 8, ty))
            # Borde por hover sobre fila
            row_rect = pygame.Rect(list_x - 4, y - 2, list_max_w + 24, self.line_height + 4)
            if hovered_index == i and not is_sel:
                pygame.draw.rect(panel, self.border_color, row_rect, width=2, border_radius=6)
            self.last_saves_layout['row_rects'][i] = row_rect.move(panel_rect.topleft)
            y += block_h

        # Scrollbar si overflow
        if n_items > max_visible:
            track_x = w - self.padding_x // 2 - 6
            track_y = self.padding_y
            track_w = 6
            track_h = inner_height
            pygame.draw.rect(panel, (255, 255, 255, 28), pygame.Rect(track_x, track_y, track_w, track_h), border_radius=3)
            thumb_h = max(24, int(track_h * (max_visible / n_items)))
            if n_items - max_visible == 0:
                thumb_top = track_y
            else:
                thumb_top = int(track_y + (track_h - thumb_h) * (start / (n_items - max_visible)))
            max_thumb_top = track_y + track_h - thumb_h
            thumb_top = max(track_y, min(thumb_top, max_thumb_top))
            pygame.draw.rect(panel, self.accent_color, pygame.Rect(track_x, thumb_top, track_w, thumb_h), border_radius=3)

        # 6) Panel de detalles a la derecha
        details_x = self.padding_x + (fixed_list_width or list_max_w) + col_gap
        details_y = self.padding_y
        # Detalles: dibujar con soporte especial para la línea de Nombre (índice 0)
        for i, line in enumerate(detail_lines):
            ty = details_y
            if i == 0:
                # Línea editable: "Nombre: <valor>"
                prefix = "Nombre: "
                # Separar prefijo y valor
                value = ""
                if editing_name:
                    value = edit_name_text or ""
                else:
                    if line.startswith(prefix):
                        value = line[len(prefix):]
                    else:
                        # fallback si cambia el formato
                        prefix = ""
                        value = line
                # Renderizar prefijo y valor
                pt = self.font.render(prefix, True, self.text_color)
                panel.blit(pt, (details_x, ty + (self.line_height - pt.get_height()) // 2))
                px = details_x + pt.get_width()
                vt_color = self.text_color
                vt = self.font.render(value if value else " ", True, vt_color)
                vy = ty + (self.line_height - vt.get_height()) // 2
                panel.blit(vt, (px, vy))
                # Calcular rect de edición/hover del valor
                name_rect = pygame.Rect(px - 4, ty - 2, max(vt.get_width(), 80) + 8, self.line_height + 4)
                # Fondo de selección si select-all activo
                if editing_name and select_all_edit:
                    sel_bg = (255, 220, 0, 48)
                    pygame.draw.rect(panel, sel_bg, name_rect, border_radius=6)
                # Borde amarillo si hover o en edición
                if hover_details_name or editing_name:
                    pygame.draw.rect(panel, self.border_color, name_rect, width=2, border_radius=6)
                # Caret si en edición
                if editing_name:
                    # Clamp posición de caret
                    cpos = max(0, min(caret_pos, len(value)))
                    caret_text = value[:cpos]
                    cw, _ = self.font.size(caret_text if caret_text else "")
                    cx = px + cw
                    cy = ty + 4
                    ch = self.line_height - 8
                    pygame.draw.rect(panel, self.accent_color, pygame.Rect(cx, cy, 2, ch), border_radius=1)
                # Guardar rect absoluto para hit-testing
                self.last_saves_layout['details_name_rect'] = name_rect.move(panel_rect.topleft)
            else:
                t = self.font.render(line, True, self.text_color)
                panel.blit(t, (details_x, ty + (self.line_height - t.get_height()) // 2))
            details_y += self.line_height + (self.item_gap - 2)

        # Guardar rango visible
        self.last_saves_layout['start'] = start
        self.last_saves_layout['end'] = end
        self.last_saves_layout['scroll_offset'] = row_scroll_offset

        # 6.5) Botones "Borrar" y "Cargar" en la parte baja del panel
        load_label = "Cargar"
        del_label = "Borrar"
        bt_load = self.font.render(load_label, True, self.text_color)
        bt_del = self.font.render(del_label, True, self.text_color)
        btn_pad_x = 18
        btn_h = self.line_height
        # Anchos individuales
        load_w = bt_load.get_width() + btn_pad_x * 2
        del_w = bt_del.get_width() + btn_pad_x * 2
        gap = 16
        total_w = load_w + gap + del_w
        base_y = h - self.padding_y - btn_h
        base_x = (w - total_w) // 2
        # Rects locales
        del_rect_local = pygame.Rect(base_x, base_y, del_w, btn_h)
        load_rect_local = pygame.Rect(base_x + del_w + gap, base_y, load_w, btn_h)
        # Estilos
        btn_bg = (255, 255, 255, 22)
        # Borrar
        pygame.draw.rect(panel, btn_bg, del_rect_local, border_radius=self.radius // 2)
        if hover_delete_button:
            pygame.draw.rect(panel, self.border_color, del_rect_local, width=2, border_radius=self.radius // 2)
        dx = del_rect_local.x + (del_rect_local.width - bt_del.get_width()) // 2
        dy = del_rect_local.y + (del_rect_local.height - bt_del.get_height()) // 2
        panel.blit(bt_del, (dx, dy))
        # Cargar
        pygame.draw.rect(panel, btn_bg, load_rect_local, border_radius=self.radius // 2)
        if hover_load_button:
            pygame.draw.rect(panel, self.border_color, load_rect_local, width=2, border_radius=self.radius // 2)
        lx = load_rect_local.x + (load_rect_local.width - bt_load.get_width()) // 2
        ly = load_rect_local.y + (load_rect_local.height - bt_load.get_height()) // 2
        panel.blit(bt_load, (lx, ly))
        # Guardar rects absolutos
        self.last_saves_layout['load_button_rect'] = load_rect_local.move(panel_rect.topleft)
        self.last_saves_layout['delete_button_rect'] = del_rect_local.move(panel_rect.topleft)

        # 7) Blit
        surface_to_blit = panel._surf if hasattr(panel, '_surf') else panel
        screen.blit(surface_to_blit, panel_rect.topleft)

        return overlay_rect

    def draw_saves(self, screen, selected, items, detail_lines):
        """
        Dibuja la lista de partidas con panel de detalles, con tamaño dinámico.
        """
        # 1) Overlay
        overlay_rect = self._draw_overlay(screen)

        # 2) Medidas dinámicas para columnas
        list_max_w = 0
        for label in items:
            tw, _ = self.font.size(label)
            list_max_w = max(list_max_w, tw)
        details_max_w = 0
        for line in detail_lines:
            tw, _ = self.font.size(line)
            details_max_w = max(details_max_w, tw)

        # Layout columnas
        col_gap = 32
        w = self.padding_x * 2 + list_max_w + col_gap + details_max_w + 12

        list_rows_h = (len(items) or 1) * self.line_height + max(0, (len(items) - 1)) * self.item_gap
        details_rows_h = (len(detail_lines) or 1) * self.line_height + max(0, (len(detail_lines) - 1)) * (self.item_gap - 2)
        inner_h = max(list_rows_h, details_rows_h)
        h = self.padding_y * 2 + inner_h

        # Limitar a la pantalla con un margen de seguridad
        sw, sh = screen.get_size()
        max_w = min(w, int(sw * 0.9))
        max_h = min(h, int(sh * 0.85))
        w, h = max_w, max_h

        panel_rect = self._center_rect(screen, (w, h))

        # 3) Sombra y panel
        self._draw_shadow(screen, panel_rect)
        panel = self._draw_panel((w, h))

        # 4) Contenido
        self.last_blits = []
        list_x = self.padding_x
        list_y = self.padding_y
        for i, label in enumerate(items):
            is_sel = (i == selected)
            if is_sel:
                pill_rect = pygame.Rect(list_x - 2, list_y, list_max_w + 16, self.line_height)
                pygame.draw.rect(panel, self.highlight_color, pill_rect, border_radius=self.radius // 2)
                accent_rect = pygame.Rect(list_x - 8, list_y, 4, self.line_height)
                pygame.draw.rect(panel, self.accent_color, accent_rect, border_radius=2)

            color = self.accent_color if is_sel else self.text_color
            text = self.font.render(label, True, color)
            ty = list_y + (self.line_height - text.get_height()) // 2
            panel.blit(text, (list_x + 8, ty))
            self.last_blits.append((list_x + 8, ty))
            list_y += self.line_height + self.item_gap

        # Detalles
        details_x = self.padding_x + list_max_w + col_gap
        details_y = self.padding_y
        for line in detail_lines:
            t = self.font.render(line, True, self.text_color)
            ty = details_y + (self.line_height - t.get_height()) // 2
            panel.blit(t, (details_x, ty))
            details_y += self.line_height + (self.item_gap - 2)

        # 5) Blit
        surface_to_blit = panel._surf if hasattr(panel, '_surf') else panel
        screen.blit(surface_to_blit, panel_rect.topleft)

        return overlay_rect

    def draw_table_with_tabs(self, screen, tabs, active_tab_index: int,
                              headers, rows,
                              selected_row: int = 0, selected_col: int = 0,
                              row_scroll_offset: int = 0,
                              hovered_row: int | None = None, hovered_col: int | None = None,
                              fixed_size: tuple[int, int] | None = None,
                              fixed_col_widths: list[int] | None = None):
        """
        Dibuja una tabla con una barra de pestañas encima.
        - tabs: etiquetas de pestañas (lista de strings)
        - active_tab_index: índice activo
        El layout expone last_table_layout['tab_rects'] para hit-testing (coordenadas de pantalla).
        """
        # 1) Overlay
        overlay_rect = self._draw_overlay(screen)

        # 2) Medidas de columnas como en draw_table (permitiendo override fijo)
        ncols = len(headers)
        col_gap = max(20, self.padding_x - 8)
        if fixed_col_widths is not None and len(fixed_col_widths) >= ncols:
            col_widths = list(fixed_col_widths[:ncols])
        else:
            col_widths = [0] * max(1, ncols)
            for i, htxt in enumerate(headers):
                tw, _ = self.font.size(htxt)
                col_widths[i] = max(col_widths[i], tw)
            for row in rows:
                for i, cell in enumerate(row[:ncols]):
                    tw, _ = self.font.size(cell)
                    col_widths[i] = max(col_widths[i], tw)
        inner_w = sum(col_widths) + col_gap * max(0, (ncols - 1))

        # Medidas de tabs
        tab_pad_x = 14
        tab_gap = 10
        tab_label_ws = [self.font.size(t)[0] for t in tabs]
        tabs_w = sum((w + tab_pad_x * 2) for w in tab_label_ws) + tab_gap * max(0, len(tabs) - 1)
        tabs_h = self.line_height

        # Dimensiones del panel
        w = self.padding_x * 2 + max(inner_w, tabs_w)
        total_rows = len(rows)
        header_h = self.line_height
        rows_h = (total_rows or 1) * self.line_height + max(0, (total_rows - 1)) * self.item_gap
        # altura: padding + tabs + gap + header + gap + rows
        h = (self.padding_y * 2 + tabs_h + self.item_gap // 2 + header_h + self.item_gap + rows_h)

        # Limitar a pantalla
        sw, sh = screen.get_size()
        if fixed_size is not None:
            fw, fh = fixed_size
            # Asegurar que no exceda la pantalla
            w = min(fw, int(sw * 0.95))
            h = min(fh, int(sh * 0.85))
        else:
            w = min(w, int(sw * 0.95))
            h = min(h, int(sh * 0.85))
        panel_rect = self._center_rect(screen, (w, h))

        # 3) Sombra y panel
        self._draw_shadow(screen, panel_rect)
        panel = self._draw_panel((w, h))

        # 4) Tabs
        self.last_blits = []
        tabs_x = self.padding_x
        tabs_y = self.padding_y
        tab_rects = []
        cx = tabs_x
        for i, label in enumerate(tabs):
            lw = tab_label_ws[i]
            tw = lw + tab_pad_x * 2
            rect = pygame.Rect(cx, tabs_y, tw, tabs_h)
            # Fondo y borde de la pestaña
            is_active = (i == active_tab_index)
            bg_col = (50, 52, 58, 160) if not is_active else (255, 200, 0, 38)
            pygame.draw.rect(panel, bg_col, rect, border_radius=10)
            if is_active:
                pygame.draw.rect(panel, self.border_color, rect, width=2, border_radius=10)
            # Etiqueta
            color = self.accent_color if is_active else self.text_color
            t = self.font.render(label, True, color)
            ty = rect.y + (rect.height - t.get_height()) // 2
            tx = rect.x + (rect.width - t.get_width()) // 2
            panel.blit(t, (tx, ty))
            # Guardar rect absoluto
            tab_rects.append(rect.move(panel_rect.topleft))
            cx += tw + tab_gap

        # 5) Cabeceras
        x = self.padding_x
        y = self.padding_y + tabs_h + self.item_gap // 2
        for i, htxt in enumerate(headers):
            t = self.font.render(htxt, True, self.text_color_dim)
            ty = y + (self.line_height - t.get_height()) // 2
            panel.blit(t, (x, ty))
            self.last_blits.append((x, ty))
            x += col_widths[i] + col_gap
        sep_y = y + header_h + (self.item_gap // 2)
        pygame.draw.line(panel, (255, 255, 255, 35), (self.padding_x, sep_y), (w - self.padding_x, sep_y), 1)

        # 6) Filas visibles con scroll
        inner_height = h - (self.padding_y * 2 + tabs_h + self.item_gap // 2 + header_h + self.item_gap)
        block_h = self.line_height + self.item_gap
        max_visible = max(1, (inner_height + self.item_gap) // block_h)
        if total_rows <= max_visible:
            start = 0
            end = total_rows
        else:
            max_offset = max(0, total_rows - max_visible)
            row_scroll_offset = max(0, min(row_scroll_offset, max_offset))
            start = row_scroll_offset
            end = start + max_visible

        # Layout para hit-testing
        self.last_table_layout = {
            'panel_rect': panel_rect,
            'start_row': 0,
            'end_row': 0,
            'cell_rects': {},
            'tab_rects': tab_rects,
        }

        y = self.padding_y + tabs_h + self.item_gap // 2 + header_h + self.item_gap
        for r in range(start, end):
            cells = rows[r]
            is_sel_row = (r == selected_row)
            if is_sel_row:
                pill_rect = pygame.Rect(self.padding_x, y, w - self.padding_x * 2, self.line_height)
                pygame.draw.rect(panel, self.highlight_color, pill_rect, border_radius=self.radius // 2)
                accent_rect = pygame.Rect(self.padding_x - 6, y, 4, self.line_height)
                pygame.draw.rect(panel, self.accent_color, accent_rect, border_radius=2)

            cx = self.padding_x
            for c in range(ncols):
                text_val = cells[c] if c < len(cells) else ""
                color = self.accent_color if (is_sel_row and c == selected_col) else (self.accent_color if is_sel_row else self.text_color)
                t = self.font.render(text_val, True, color)
                ty = y + (self.line_height - t.get_height()) // 2
                panel.blit(t, (cx, ty))
                self.last_blits.append((cx, ty))
                cell_rect = pygame.Rect(cx - 4, y - 2, col_widths[c] + 8, self.line_height + 4)
                is_hover = (hovered_row == r and hovered_col == c)
                is_sel_cell = (selected_row == r and selected_col == c)
                if is_hover or is_sel_cell:
                    pygame.draw.rect(panel, self.border_color, cell_rect, width=2, border_radius=6)
                screen_rect = cell_rect.move(panel_rect.topleft)
                self.last_table_layout['cell_rects'][(r, c)] = screen_rect
                cx += col_widths[c] + col_gap
            y += block_h

        self.last_table_layout['start_row'] = start
        self.last_table_layout['end_row'] = end

        # 7) Scrollbar si overflow
        if total_rows > max_visible:
            track_x = w - self.padding_x // 2 - 6
            track_y = self.padding_y + tabs_h + self.item_gap // 2 + header_h + self.item_gap
            track_w = 6
            track_h = inner_height
            pygame.draw.rect(panel, (255, 255, 255, 28), pygame.Rect(track_x, track_y, track_w, track_h), border_radius=3)
            thumb_h = max(24, int(track_h * (max_visible / total_rows)))
            if total_rows - max_visible == 0:
                thumb_top = track_y
            else:
                thumb_top = int(track_y + (track_h - thumb_h) * (start / (total_rows - max_visible)))
            max_thumb_top = track_y + track_h - thumb_h
            thumb_top = max(track_y, min(thumb_top, max_thumb_top))
            pygame.draw.rect(panel, self.accent_color, pygame.Rect(track_x, thumb_top, track_w, thumb_h), border_radius=3)

        # 8) Blit panel
        surface_to_blit = panel._surf if hasattr(panel, '_surf') else panel
        screen.blit(surface_to_blit, panel_rect.topleft)

        return overlay_rect

    def draw_table(self, screen, headers, rows, selected_row: int = 0, selected_col: int = 0, row_scroll_offset: int = 0, hovered_row: int | None = None, hovered_col: int | None = None):
        """
        Dibuja una tabla con cabeceras y filas usando el mismo estilo del menú.
        - headers: lista de strings
        - rows: lista de listas de strings (cada fila con mismas columnas que headers)
        - selected_row/col: posición seleccionada (resalta la fila completa)
        - row_scroll_offset: inicio de fila visible (para scrollbar)
        """
        # 1) Overlay
        overlay_rect = self._draw_overlay(screen)

        # 2) Medidas dinámicas de columnas
        ncols = len(headers)
        col_gap = max(20, self.padding_x - 8)
        col_widths = [0] * max(1, ncols)
        # Ancho por cabeceras
        for i, htxt in enumerate(headers):
            tw, _ = self.font.size(htxt)
            col_widths[i] = max(col_widths[i], tw)
        # Ancho por celdas
        for row in rows:
            for i, cell in enumerate(row[:ncols]):
                tw, _ = self.font.size(cell)
                col_widths[i] = max(col_widths[i], tw)
        # Dimensiones del panel
        inner_w = sum(col_widths) + col_gap * max(0, (ncols - 1))
        w = self.padding_x * 2 + inner_w
        total_rows = len(rows)
        header_h = self.line_height
        rows_h = (total_rows or 1) * self.line_height + max(0, (total_rows - 1)) * self.item_gap
        h = self.padding_y * 2 + header_h + self.item_gap + rows_h

        # Limitar a pantalla
        sw, sh = screen.get_size()
        w = min(w, int(sw * 0.95))
        h = min(h, int(sh * 0.85))
        panel_rect = self._center_rect(screen, (w, h))

        # 3) Sombra y panel
        self._draw_shadow(screen, panel_rect)
        panel = self._draw_panel((w, h))

        # 4) Cabeceras
        self.last_blits = []
        x = self.padding_x
        y = self.padding_y
        for i, htxt in enumerate(headers):
            t = self.font.render(htxt, True, self.text_color_dim)
            ty = y + (self.line_height - t.get_height()) // 2
            panel.blit(t, (x, ty))
            self.last_blits.append((x, ty))
            x += col_widths[i] + col_gap
        # Separador
        sep_y = y + header_h + (self.item_gap // 2)
        pygame.draw.line(panel, (255, 255, 255, 35), (self.padding_x, sep_y), (w - self.padding_x, sep_y), 1)

        # 5) Filas visibles con scroll
        inner_height = h - self.padding_y * 2 - header_h - self.item_gap
        block_h = self.line_height + self.item_gap
        max_visible = max(1, (inner_height + self.item_gap) // block_h)
        if total_rows <= max_visible:
            start = 0
            end = total_rows
        else:
            max_offset = max(0, total_rows - max_visible)
            row_scroll_offset = max(0, min(row_scroll_offset, max_offset))
            start = row_scroll_offset
            end = start + max_visible

        # Preparar layout para hit-testing desde UI
        self.last_table_layout = {
            'panel_rect': panel_rect,
            'start_row': 0,
            'end_row': 0,
            'cell_rects': {},  # (row, col) -> screen rect
        }

        y = self.padding_y + header_h + self.item_gap
        for r in range(start, end):
            cells = rows[r]
            is_sel_row = (r == selected_row)
            if is_sel_row:
                pill_rect = pygame.Rect(self.padding_x, y, w - self.padding_x * 2, self.line_height)
                pygame.draw.rect(panel, self.highlight_color, pill_rect, border_radius=self.radius // 2)
                accent_rect = pygame.Rect(self.padding_x - 6, y, 4, self.line_height)
                pygame.draw.rect(panel, self.accent_color, accent_rect, border_radius=2)

            cx = self.padding_x
            for c in range(ncols):
                text_val = cells[c] if c < len(cells) else ""
                color = self.accent_color if (is_sel_row and c == selected_col) else (self.accent_color if is_sel_row else self.text_color)
                t = self.font.render(text_val, True, color)
                ty = y + (self.line_height - t.get_height()) // 2
                panel.blit(t, (cx, ty))
                self.last_blits.append((cx, ty))
                # Borde amarillo para hovered o celda seleccionada
                cell_rect = pygame.Rect(cx - 4, y - 2, col_widths[c] + 8, self.line_height + 4)
                is_hover = (hovered_row == r and hovered_col == c)
                is_sel_cell = (selected_row == r and selected_col == c)
                if is_hover or is_sel_cell:
                    pygame.draw.rect(panel, self.border_color, cell_rect, width=2, border_radius=6)
                # Guardar rect absoluto para hit-test
                screen_rect = cell_rect.move(panel_rect.topleft)
                self.last_table_layout['cell_rects'][(r, c)] = screen_rect
                cx += col_widths[c] + col_gap
            y += block_h

        self.last_table_layout['start_row'] = start
        self.last_table_layout['end_row'] = end

        # 6) Scrollbar si overflow
        if total_rows > max_visible:
            track_x = w - self.padding_x // 2 - 6
            track_y = self.padding_y + header_h + self.item_gap
            track_w = 6
            track_h = inner_height
            pygame.draw.rect(panel, (255, 255, 255, 28), pygame.Rect(track_x, track_y, track_w, track_h), border_radius=3)
            thumb_h = max(24, int(track_h * (max_visible / total_rows)))
            if total_rows - max_visible == 0:
                thumb_top = track_y
            else:
                thumb_top = int(track_y + (track_h - thumb_h) * (start / (total_rows - max_visible)))
            max_thumb_top = track_y + track_h - thumb_h
            thumb_top = max(track_y, min(thumb_top, max_thumb_top))
            pygame.draw.rect(panel, self.accent_color, pygame.Rect(track_x, thumb_top, track_w, thumb_h), border_radius=3)

        # 7) Blit panel
        surface_to_blit = panel._surf if hasattr(panel, '_surf') else panel
        screen.blit(surface_to_blit, panel_rect.topleft)

        return overlay_rect

    def draw_message(self, screen, lines):
        """
        Dibuja un panel centrado con múltiples líneas de texto usando el mismo estilo.
        Devuelve el rect del overlay (pantalla completa) para dirty rects.
        """
        # 1) Overlay
        overlay_rect = self._draw_overlay(screen)

        # 2) Medidas dinámicas por contenido
        max_w = 0
        for line in lines:
            tw, _ = self.font.size(line)
            max_w = max(max_w, tw)
        w = self.padding_x * 2 + max_w
        rows_h = (len(lines) or 1) * self.line_height + max(0, (len(lines) - 1)) * (self.item_gap - 2)
        h = self.padding_y * 2 + rows_h

        # Limitar a pantalla
        sw, sh = screen.get_size()
        w = min(w, int(sw * 0.9))
        h = min(h, int(sh * 0.6))
        panel_rect = self._center_rect(screen, (w, h))

        # 3) Sombra y panel
        self._draw_shadow(screen, panel_rect)
        panel = self._draw_panel((w, h))

        # 4) Texto
        y = self.padding_y
        for line in lines:
            t = self.font.render(line, True, self.text_color)
            ty = y + (self.line_height - t.get_height()) // 2
            panel.blit(t, (self.padding_x, ty))
            y += self.line_height + (self.item_gap - 2)

        # 5) Blit
        surface_to_blit = panel._surf if hasattr(panel, '_surf') else panel
        screen.blit(surface_to_blit, panel_rect.topleft)

        return overlay_rect
