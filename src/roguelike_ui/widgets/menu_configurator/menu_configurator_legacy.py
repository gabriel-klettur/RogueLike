import pygame
from roguelike_ui.widgets.menu_renderer.menu_renderer import MenuRenderer
from roguelike_ui.services.formatting import format_key_label


class MenuConfigurator:
    """
    Proporciona una interfaz para reasignar bindings de teclas y guardar la configuración,
    usando el mismo estilo visual del menú principal (MenuRenderer).
    Navegación: Arriba/Abajo (o W/S, A/D), Enter para reasignar, ESC para volver.
    """

    def __init__(self, input_config, screen, font, underlay_provider=None, base_font_size: int | None = None):
        self.config = input_config
        self.screen = screen
        self.font = font
        # Dibuja el background/logo del menú de inicio y devuelve la Y mínima del panel
        # Firma esperada: underlay_provider(screen) -> panel_top_min | None
        self.underlay_provider = underlay_provider
        # Usamos un tamaño de fuente estandarizado si se provee.
        if isinstance(base_font_size, int) and base_font_size > 6:
            self.renderer = MenuRenderer(font_size=base_font_size)
        else:
            try:
                font_size = int(self.font.get_height()) if font else 18
            except Exception:
                font_size = 18
            self.renderer = MenuRenderer(font_size=font_size)
        # Tabs: (label visible, key interna)
        self.tabs: list[tuple[str, str]] = [
            ("General", "general"),
            ("Movimientos", "movements"),
            ("Hechizos", "spells"),
            ("Editores", "editors"),
        ]
        self.active_tab_index: int = 0
        # Layout fijo (se calcula antes del loop de UI)
        self._fixed_col_widths: list[int] | None = None
        self._fixed_panel_size: tuple[int, int] | None = None
        self._fixed_screen_size: tuple[int, int] | None = None

    def configure(self):
        """
        Carga configuración y entra en el loop del configurador.
        Bloquea hasta que el usuario presione ESC.
        """
        # Cargar configuraciones previas si existen
        if hasattr(self.config, 'load'):
            self.config.load()
        elif hasattr(self.config, '_load'):
            self.config._load()

        self._show_menu()

    # ---- UI principal ----
    def _show_menu(self):
        selected_row = 0
        selected_col = 1  # por defecto nos colocamos en Keyboard A
        row_scroll_offset = 0
        hovered_row = None
        hovered_col = None
        running = True
        clock = pygame.time.Clock()
        # Repetición profesional de teclas: retardo inicial + frecuencia de repetición
        repeat_cfg = {"initial": 260, "interval": 70}  # ms
        hold = {
            'up':    {"keys": (pygame.K_UP, pygame.K_w),    "held": False, "next": 0},
            'down':  {"keys": (pygame.K_DOWN, pygame.K_s),  "held": False, "next": 0},
            'left':  {"keys": (pygame.K_LEFT, pygame.K_a),  "held": False, "next": 0},
            'right': {"keys": (pygame.K_RIGHT, pygame.K_d), "held": False, "next": 0},
        }

        # Construir cabeceras de forma temprana para precomputar layout fijo
        headers = ["Acción", "Keyboard A", "Keyboard B", "Mouse"]
        # Precalcular layout fijo (tamaños de columna y panel) en función de todas las pestañas
        self._compute_fixed_layout(headers)

        while running:
            # Recalcular layout fijo si cambia el tamaño de la ventana
            if self._fixed_screen_size != self.screen.get_size():
                self._compute_fixed_layout(headers)
            # Construir especificaciones de filas (agrupando acciones tri-slot) y filtrar por pestaña activa
            _, tab_key = self.tabs[self.active_tab_index]
            row_specs, rows = self._build_row_specs(category=tab_key)
            total_rows = len(rows)
            # Asegurar que el índice seleccionado esté dentro del rango actual
            if total_rows:
                if selected_row >= total_rows:
                    selected_row = max(0, total_rows - 1)
            for event in pygame.event.get():
                if event.type == pygame.QUIT:
                    running = False
                    break
                if event.type == pygame.KEYDOWN:
                    if event.key in (pygame.K_UP, pygame.K_w):
                        selected_row = (selected_row - 1) % max(1, total_rows)
                        hold['up']['held'] = True
                        hold['up']['next'] = repeat_cfg['initial']
                    elif event.key in (pygame.K_DOWN, pygame.K_s):
                        selected_row = (selected_row + 1) % max(1, total_rows)
                        hold['down']['held'] = True
                        hold['down']['next'] = repeat_cfg['initial']
                    elif event.key in (pygame.K_LEFT, pygame.K_a):
                        selected_col = max(0, selected_col - 1)
                        hold['left']['held'] = True
                        hold['left']['next'] = repeat_cfg['initial']
                    elif event.key in (pygame.K_RIGHT, pygame.K_d):
                        selected_col = min(3, selected_col + 1)
                        hold['right']['held'] = True
                        hold['right']['next'] = repeat_cfg['initial']
                    # Cambiar pestañas con Q/E o PageUp/PageDown
                    elif event.key in (pygame.K_q, pygame.K_PAGEUP):
                        self.active_tab_index = (self.active_tab_index - 1) % len(self.tabs)
                        selected_row, selected_col, row_scroll_offset = 0, 1, 0
                    elif event.key in (pygame.K_e, pygame.K_PAGEDOWN):
                        self.active_tab_index = (self.active_tab_index + 1) % len(self.tabs)
                        selected_row, selected_col, row_scroll_offset = 0, 1, 0
                    elif event.key in (pygame.K_RETURN, pygame.K_SPACE):
                        if not row_specs:
                            continue
                        spec = row_specs[selected_row]
                        if spec['kind'] == 'tri':
                            if selected_col == 1:
                                self._prompt_key(spec['kb_a_key'], slot='keyboard_a')
                            elif selected_col == 2:
                                self._prompt_key(spec['kb_b_key'], slot='keyboard_b')
                            elif selected_col == 3:
                                self._prompt_mouse(spec['mouse_key'])
                            else:
                                pass
                        else:  # single
                            if selected_col == 1:
                                self._prompt_key(spec['action_key'], slot='keyboard_a')
                            elif selected_col == 3 and isinstance(self.config.bindings.get(spec['action_key']), str) and self.config.bindings.get(spec['action_key'], '').startswith('M_'):
                                self._prompt_mouse(spec['action_key'])
                            else:
                                self._flash_message(["Esa celda no es editable", "Usa Keyboard A o Mouse donde aplique"]) 
                    elif event.key == pygame.K_ESCAPE:
                        running = False
                        break
                elif event.type == pygame.KEYUP:
                    # Reseteo rápido al soltar; el estado exacto se corrige con get_pressed abajo
                    if event.key in (pygame.K_UP, pygame.K_w):
                        hold['up']['held'] = False
                        hold['up']['next'] = 0
                    elif event.key in (pygame.K_DOWN, pygame.K_s):
                        hold['down']['held'] = False
                        hold['down']['next'] = 0
                    elif event.key in (pygame.K_LEFT, pygame.K_a):
                        hold['left']['held'] = False
                        hold['left']['next'] = 0
                    elif event.key in (pygame.K_RIGHT, pygame.K_d):
                        hold['right']['held'] = False
                        hold['right']['next'] = 0
                elif event.type == pygame.MOUSEMOTION:
                    # Actualizar hover usando el último layout de tabla
                    hovered_row, hovered_col = self._hit_test_cell(event.pos)
                elif event.type == pygame.MOUSEWHEEL:
                    # Scroll por rueda del ratón (invertido como en listas)
                    row_scroll_offset = max(0, row_scroll_offset - event.y)
                elif event.type == pygame.MOUSEBUTTONDOWN and event.button == 1:
                    # Primero, comprobar click en pestañas
                    layout = getattr(self.renderer, 'last_table_layout', None)
                    if layout:
                        tab_rects = layout.get('tab_rects', [])
                        for i, rect in enumerate(tab_rects):
                            if rect.collidepoint(event.pos):
                                if i != self.active_tab_index:
                                    self.active_tab_index = i
                                    selected_row, selected_col, row_scroll_offset = 0, 1, 0
                                # No procesar celdas si fue click de pestaña
                                break
                        else:
                            # No se ha hecho click en pestaña -> seguir a celdas
                            pass
                        # Si sí se hizo click en pestaña, saltar a próximo frame
                        if any(rect.collidepoint(event.pos) for rect in tab_rects):
                            continue
                    # Seleccionar celda por click
                    hr, hc = self._hit_test_cell(event.pos)
                    if hr is not None and hc is not None:
                        selected_row, selected_col = hr, hc
                        if row_specs:
                            spec = row_specs[selected_row]
                            if spec['kind'] == 'tri':
                                if selected_col == 1:
                                    self._prompt_key(spec['kb_a_key'], slot='keyboard_a')
                                elif selected_col == 2:
                                    self._prompt_key(spec['kb_b_key'], slot='keyboard_b')
                                elif selected_col == 3:
                                    self._prompt_mouse(spec['mouse_key'])
                            else:
                                if selected_col == 1:
                                    self._prompt_key(spec['action_key'], slot='keyboard_a')
                                elif selected_col == 3 and isinstance(self.config.bindings.get(spec['action_key']), str) and self.config.bindings.get(spec['action_key'], '').startswith('M_'):
                                    self._prompt_mouse(spec['action_key'])

            # Construir cabeceras y filas
            # headers ya definidos
            # rows ya construido por _build_row_specs()

            # Actualizar repetición por tecla mantenida (profesional)
            dt = clock.get_time()  # ms desde el último tick
            pressed = pygame.key.get_pressed()
            # Recalcular estado held a partir de pressed para robustez
            for name, st in hold.items():
                st['held'] = any(pressed[k] for k in st['keys'])
            def _repeat_step(name: str):
                nonlocal selected_row, selected_col
                if name == 'up':
                    selected_row = (selected_row - 1) % max(1, total_rows)
                elif name == 'down':
                    selected_row = (selected_row + 1) % max(1, total_rows)
                elif name == 'left':
                    selected_col = max(0, selected_col - 1)
                elif name == 'right':
                    selected_col = min(3, selected_col + 1)
            for name, st in hold.items():
                if not st['held']:
                    st['next'] = 0
                    continue
                if st['next'] <= 0:
                    # Ejecutar paso y programar siguiente repetición
                    _repeat_step(name)
                    st['next'] = repeat_cfg['interval']
                else:
                    st['next'] -= dt

            # Calcular ventana visible (filas) para mantener seleccionado dentro de vista (layout fijo)
            total_rows = len(rows)
            if total_rows:
                header_h = self.renderer.line_height
                tabs_h = self.renderer.line_height
                # Usar alto fijo precomputado
                panel_h = self._fixed_panel_size[1] if self._fixed_panel_size else int(self.screen.get_size()[1] * 0.85)
                inner_height = panel_h - (self.renderer.padding_y * 2 + tabs_h + (self.renderer.item_gap // 2) + header_h + self.renderer.item_gap)
                block_h = self.renderer.line_height + self.renderer.item_gap
                max_visible = max(1, (inner_height + self.renderer.item_gap) // block_h)
                # Ajuste del scroll por fila
                if selected_row < row_scroll_offset:
                    row_scroll_offset = selected_row
                elif selected_row >= row_scroll_offset + max_visible:
                    row_scroll_offset = selected_row - max_visible + 1
                max_offset = max(0, total_rows - max_visible)
                row_scroll_offset = max(0, min(row_scroll_offset, max_offset))

            # Underlay: persistir background/logo cuando venimos del menú de inicio
            panel_top_min = None
            if callable(self.underlay_provider):
                try:
                    panel_top_min = self.underlay_provider(self.screen)
                except Exception:
                    panel_top_min = None
            # Añadir un margen extra bajo el logo para que el panel no lo roce
            try:
                sh = self.screen.get_size()[1]
            except Exception:
                sh = 720
            if isinstance(panel_top_min, int):
                # Margen pequeño para despegar del logo
                extra = max(24, int(self.renderer.line_height))
                panel_top_min = panel_top_min + extra
            else:
                # Sin logo (pausa, etc.): no forzamos desplazamiento
                panel_top_min = None
            # Dibujar tabla con pestañas
            self.renderer.draw_table_with_tabs(
                self.screen,
                tabs=[lbl for (lbl, _key) in self.tabs],
                active_tab_index=self.active_tab_index,
                headers=headers,
                rows=rows,
                selected_row=selected_row,
                selected_col=selected_col,
                row_scroll_offset=row_scroll_offset,
                hovered_row=hovered_row,
                hovered_col=hovered_col,
                fixed_size=self._fixed_panel_size,
                fixed_col_widths=self._fixed_col_widths,
                panel_top_min=panel_top_min,
            )
            pygame.display.flip()
            clock.tick(60)

    def _build_row_specs(self, category: str | None = None):
        """Construye especificaciones de filas para el renderer y edición.
        Retorna (row_specs, rows) donde:
        - row_specs: lista de dicts con 'kind' ('tri'), 'display', y claves subyacentes.
        - rows: lista de listas de strings para el renderer.
        """
        bindings = self.config.bindings

        # 1) Construir el conjunto de acciones base a partir de todas las claves
        base_actions: set[str] = set()
        for k in bindings.keys():
            if k.startswith('kb_'):
                body = k[len('kb_'):]
                # remover sufijos _a/_b si existen
                if body.endswith('_a') or body.endswith('_b'):
                    body = body[:-2]
                base_actions.add(body)
            elif k.startswith('mouse_'):
                base_actions.add(k[len('mouse_'):])
            else:
                base_actions.add(k)

        # 2) Construir especificaciones tri-slot para TODAS las acciones base
        all_specs: list[dict] = []

        def label_for(name: str) -> str:
            val = bindings.get(name, "")
            return format_key_label(val) if isinstance(val, str) and val else "—"

        for base in sorted(base_actions):
            kb_a_key = f"kb_{base}_a"
            kb_b_key = f"kb_{base}_b"
            mouse_key = f"mouse_{base}"
            # Etiquetas: A/B/mouse desde sus slots; A cae al binding base si slot vacío
            kb_a_label = label_for(kb_a_key)
            if kb_a_label == "—":
                raw_base = bindings.get(base, "")
                if isinstance(raw_base, str) and raw_base:
                    kb_a_label = format_key_label(raw_base)
            kb_b_label = label_for(kb_b_key)
            mouse_label = label_for(mouse_key)

            all_specs.append({
                'kind': 'tri',
                'display': self._format_action_name(base),
                'kb_a_key': kb_a_key,
                'kb_b_key': kb_b_key,
                'mouse_key': mouse_key,
                'base_key': base,
                'labels': (kb_a_label, kb_b_label, mouse_label),
            })

        # 3) Categorizar
        def categorize(spec: dict) -> str:
            base = spec.get('base_key', '')
            if isinstance(base, str):
                if base == 'dash' or base.startswith('move_'):
                    return 'movements'
                if base in ('fireball', 'laser_beam') or base.startswith('spell_'):
                    return 'spells'
                if base.startswith('toggle_') and base.endswith('_editor'):
                    return 'editors'
                if base in ('pause', 'toggle_inventory', 'select_class'):
                    return 'general'
            return 'general'

        if category:
            all_specs = [s for s in all_specs if categorize(s) == category]

        # 4) Ordenar y construir filas
        all_specs.sort(key=lambda s: s['display'])
        rows = [[s['display'], s['labels'][0], s['labels'][1], s['labels'][2]] for s in all_specs]
        return all_specs, rows

    def _compute_fixed_layout(self, headers: list[str]) -> None:
        """Calcula tamaños fijos de columnas y panel para que no varíen entre pestañas.
        Usa el máximo ancho de texto por columna a través de todas las pestañas y
        define una altura fija basada en el máximo número de filas (clamp al 85% de pantalla).
        """
        # Medir columnas
        ncols = len(headers)
        # Gap y métricas deben reflejar las del renderer
        col_gap = max(20, self.renderer.padding_x - 8)
        col_widths = [0] * ncols
        # Cabeceras
        for i, htxt in enumerate(headers):
            tw, _ = self.renderer.font.size(htxt)
            col_widths[i] = max(col_widths[i], tw)
        # Recorrer todas las pestañas
        max_total_rows = 0
        tab_label_ws = [self.renderer.font.size(lbl)[0] for (lbl, _k) in self.tabs]
        tabs_w = sum((w + 14 * 2) for w in tab_label_ws) + 10 * max(0, len(self.tabs) - 1)
        for (_lbl, key) in self.tabs:
            _specs, rows = self._build_row_specs(category=key)
            max_total_rows = max(max_total_rows, len(rows))
            for row in rows:
                for i in range(ncols):
                    cell = row[i] if i < len(row) else ""
                    tw, _ = self.renderer.font.size(cell)
                    col_widths[i] = max(col_widths[i], tw)

        inner_w = sum(col_widths) + col_gap * max(0, (ncols - 1))
        # Ancho del panel basado en el mayor entre columnas y tabs
        w = self.renderer.padding_x * 2 + max(inner_w, tabs_w)
        # Alto del panel usando máximo de filas (clamp a 85% pantalla)
        header_h = self.renderer.line_height
        tabs_h = self.renderer.line_height
        rows_h = (max_total_rows or 1) * self.renderer.line_height + max(0, (max_total_rows - 1)) * self.renderer.item_gap
        h = (self.renderer.padding_y * 2 + tabs_h + self.renderer.item_gap // 2 + header_h + self.renderer.item_gap + rows_h)
        # Clamp a pantalla
        sw, sh = self.screen.get_size()
        # Clamp siempre respecto al tamaño actual de pantalla
        w = min(w, int(sw * 0.95))
        h = min(h, int(sh * 0.85))
        self._fixed_screen_size = (sw, sh)
        self._fixed_col_widths = col_widths
        self._fixed_panel_size = (w, h)

    def _prompt_key(self, action, slot='keyboard_a'):
        """
        Muestra un modal para capturar una nueva tecla con botones BORRAR/CANCELAR/ACEPTAR.
        En lugar de aceptar inmediatamente, mantiene el modal abierto mostrando la
        selección, y solo aplica o sale mediante BORRAR / ACEPTAR / CANCELAR.
        """
        pretty = self._format_action_friendly(action, slot_hint=slot)
        buttons = ["BORRAR", "CANCELAR", "ACEPTAR"]
        hovered = None
        candidate_value: str | None = None
        candidate_label: str | None = None

        waiting = True
        while waiting:
            # Eventos
            for event in pygame.event.get():
                if event.type == pygame.KEYDOWN:
                    # Guardar como candidato, no aplicar aún
                    key_const = self._key_const_from_code(event.key)
                    candidate_value = key_const or f"K_{pygame.key.name(event.key).upper()}"
                    candidate_label = format_key_label(candidate_value)
                elif event.type == pygame.MOUSEMOTION:
                    # Actualizar hover de botones
                    lines = [f"Pulsa una tecla para {pretty}"]
                    if candidate_label:
                        lines.append({"text": f"Acción: {pretty}", "color": (0, 220, 255), "bold": True})
                        lines.append({"text": f"Botón seleccionado: {candidate_label}", "color": (255, 210, 0), "bold": True})
                    layout = self._draw_modal_with_buttons(lines, buttons, redraw=False)
                    hovered = None
                    for idx, rect in enumerate(layout['button_rects']):
                        if rect.collidepoint(event.pos):
                            hovered = idx
                            break
                elif event.type == pygame.MOUSEBUTTONDOWN and event.button == 1:
                    # Click en botones
                    lines = [f"Pulsa una tecla para {pretty}"]
                    if candidate_label:
                        lines.append({"text": f"Acción: {pretty}", "color": (0, 220, 255), "bold": True})
                        lines.append({"text": f"Botón seleccionado: {candidate_label}", "color": (255, 210, 0), "bold": True})
                    layout = self._draw_modal_with_buttons(lines, buttons, redraw=False)
                    for idx, rect in enumerate(layout['button_rects']):
                        if rect.collidepoint(event.pos):
                            label = buttons[idx]
                            if label == "BORRAR":
                                self.config.set_binding(action, "")
                                if hasattr(self.config, 'save'):
                                    self.config.save()
                                waiting = False
                            elif label == "CANCELAR":
                                waiting = False
                            elif label == "ACEPTAR":
                                # Aplicar si hay candidato
                                if candidate_value:
                                    self.config.set_key(action, candidate_value)
                                    if hasattr(self.config, 'save'):
                                        self.config.save()
                                waiting = False
                            break
                elif event.type == pygame.QUIT:
                    waiting = False
                    break

            # Dibujar modal con botones y hover actual
            lines = [f"Pulsa una tecla para {pretty}"]
            if candidate_label:
                lines.append({"text": f"Acción: {pretty}", "color": (0, 220, 255), "bold": True})
                lines.append({"text": f"Botón seleccionado: {candidate_label}", "color": (255, 210, 0), "bold": True})
            self._draw_modal_with_buttons(lines, buttons, hover_index=hovered, redraw=True)
            pygame.display.flip()

    def _flash_message(self, lines, ms=750):
        """Muestra un mensaje temporal con el mismo estilo del menú."""
        clock = pygame.time.Clock()
        elapsed = 0
        while elapsed < ms:
            for event in pygame.event.get():
                if event.type == pygame.QUIT:
                    return
            self.renderer.draw_message(self.screen, lines)
            pygame.display.flip()
            dt = clock.tick(60)
            elapsed += dt

    # ---- Utilidades ----
    def _hit_test_cell(self, pos):
        """Devuelve (row, col) si la posición del mouse cae sobre una celda visible.
        Usa el layout almacenado por el renderer.
        """
        layout = getattr(self.renderer, 'last_table_layout', None)
        if not layout:
            return (None, None)
        cell_rects = layout.get('cell_rects', {})
        for (r, c), rect in cell_rects.items():
            if rect.collidepoint(pos):
                return (r, c)
        return (None, None)

    def _format_action_name(self, action: str) -> str:
        name = action
        # No mostrar prefijo 'mouse_' en la UI ("Mouse Fireball" -> "Fireball")
        if isinstance(name, str) and name.startswith('mouse_'):
            name = name[len('mouse_'):]
        return name.replace('_', ' ').title()

    def _format_action_friendly(self, action: str, slot_hint: str | None = None) -> str:
        """Devuelve un nombre de acción entendible para el usuario, con el canal.
        Ejemplos:
        - kb_dash_a -> "Dash (Teclado A)"
        - kb_dash_b -> "Dash (Teclado B)"
        - mouse_dash -> "Dash (Ratón)"
        - interact (con slot_hint='keyboard_a') -> "Interact (Teclado)"
        - interact (mouse) -> "Interact (Ratón)"
        """
        if not isinstance(action, str):
            return str(action)
        # Teclado A/B explícito
        if action.startswith('kb_'):
            body = action[len('kb_'):]
            base = body[:-2] if body.endswith('_a') or body.endswith('_b') else body
            nice = base.replace('_', ' ').title()
            if body.endswith('_a') or (slot_hint == 'keyboard_a'):
                return f"{nice} (Teclado A)"
            if body.endswith('_b') or (slot_hint == 'keyboard_b'):
                return f"{nice} (Teclado B)"
            return f"{nice} (Teclado)"
        # Ratón
        if action.startswith('mouse_'):
            base = action[len('mouse_'):]
            nice = base.replace('_', ' ').title()
            return f"{nice} (Ratón)"
        # Genérico con pista
        nice = action.replace('_', ' ').title()
        if slot_hint == 'keyboard_a' or slot_hint == 'keyboard_b' or slot_hint == 'keyboard':
            return f"{nice} (Teclado)"
        if slot_hint == 'mouse':
            return f"{nice} (Ratón)"
        return nice

    _KEYCODE_CONST_CACHE = None
    def _key_const_from_code(self, key_code: int) -> str | None:
        """Obtiene el nombre de constante 'K_*' a partir del keycode de pygame.
        Construye una caché la primera vez para hacer lookups O(1).
        """
        if MenuConfigurator._KEYCODE_CONST_CACHE is None:
            cache = {}
            for name in dir(pygame):
                if not name.startswith('K_'):
                    continue
                try:
                    val = getattr(pygame, name)
                except Exception:
                    continue
                if isinstance(val, int):
                    cache[val] = name
            MenuConfigurator._KEYCODE_CONST_CACHE = cache
        return MenuConfigurator._KEYCODE_CONST_CACHE.get(key_code)

    def _prompt_mouse(self, action: str):
        """Modal para capturar botón de ratón con botones BORRAR/CANCELAR/ACEPTAR.
        No aplica inmediatamente; muestra la selección y solo sale con botones.
        """
        pretty = self._format_action_name(action)
        buttons = ["BORRAR", "CANCELAR", "ACEPTAR"]
        hovered = None
        candidate_value: str | None = None
        candidate_label: str | None = None
        waiting = True
        while waiting:
            for event in pygame.event.get():
                if event.type == pygame.MOUSEMOTION:
                    lines = [f"Haz click para asignar ratón a {pretty}"]
                    if candidate_label:
                        lines.append({"text": f"Acción: {pretty}", "color": (0, 220, 255), "bold": True})
                        lines.append({"text": f"Botón seleccionado: {candidate_label}", "color": (255, 210, 0), "bold": True})
                    layout = self._draw_modal_with_buttons(lines, buttons, redraw=False)
                    hovered = None
                    for idx, rect in enumerate(layout['button_rects']):
                        if rect.collidepoint(event.pos):
                            hovered = idx
                            break
                if event.type == pygame.MOUSEBUTTONDOWN:
                    btn = event.button
                    # Primero, chequear botones del modal si fue click izquierdo
                    if btn == 1:
                        lines = [f"Haz click para asignar ratón a {pretty}"]
                        if candidate_label:
                            lines.append({"text": f"Acción: {pretty}", "color": (0, 220, 255), "bold": True})
                            lines.append({"text": f"Botón seleccionado: {candidate_label}", "color": (255, 210, 0), "bold": True})
                        layout = self._draw_modal_with_buttons(lines, buttons, redraw=False)
                        clicked = None
                        for idx, rect in enumerate(layout['button_rects']):
                            if rect.collidepoint(event.pos):
                                clicked = buttons[idx]
                                break
                        if clicked == "BORRAR":
                            self.config.set_binding(action, "")
                            if hasattr(self.config, 'save'):
                                self.config.save()
                            waiting = False
                            # saltar asignación directa si era sobre botón
                            self._draw_modal_with_buttons(lines, buttons, hover_index=hovered, redraw=True)
                            pygame.display.flip()
                            continue
                        elif clicked == "CANCELAR":
                            waiting = False
                            self._draw_modal_with_buttons(lines, buttons, hover_index=hovered, redraw=True)
                            pygame.display.flip()
                            continue
                        elif clicked == "ACEPTAR":
                            # Aplicar si hay candidato
                            if candidate_value:
                                self.config.set_key(action, candidate_value)
                                if hasattr(self.config, 'save'):
                                    self.config.save()
                            waiting = False
                            self._draw_modal_with_buttons(lines, buttons, hover_index=hovered, redraw=True)
                            pygame.display.flip()
                            continue
                    # Asignación directa según botón físico (excepto rueda)
                    mname = None
                    if btn == 1:
                        mname = 'M_LEFT'
                    elif btn == 2:
                        mname = 'M_MIDDLE'
                    elif btn == 3:
                        mname = 'M_RIGHT'
                    elif btn == 8:
                        mname = 'M_X1'
                    elif btn == 9:
                        mname = 'M_X2'
                    elif btn in (4, 5, 6, 7):
                        self._flash_message(["La rueda del ratón no es asignable"], ms=500)
                        continue
                    if mname:
                        candidate_value = mname
                        candidate_label = format_key_label(mname)
                        # no cerrar aún; esperar ACEPTAR
                elif event.type == pygame.QUIT:
                    waiting = False
                    break
            lines = [f"Haz click para asignar ratón a {pretty}"]
            if candidate_label:
                lines.append({"text": f"Acción: {pretty}", "color": (0, 220, 255), "bold": True})
                lines.append({"text": f"Botón seleccionado: {candidate_label}", "color": (255, 210, 0), "bold": True})
            self._draw_modal_with_buttons(lines, buttons, hover_index=hovered, redraw=True)
            pygame.display.flip()

    # ---- UI helpers for modal ----
    def _draw_modal_with_buttons(self, lines, buttons: list[str], hover_index: int | None = None, redraw: bool = True):
        """Dibuja un modal tipo draw_message con una fila de botones centrados.
        Devuelve un dict con panel_rect y lista de button_rects en coordenadas de pantalla.
        Si redraw=False, no repinta; solo calcula y devuelve rects según el layout actual.
        """
        # Medidas como draw_message
        max_w = 0
        for line in lines:
            if isinstance(line, dict):
                txt = line.get("text", "")
                bold = bool(line.get("bold", False))
                prev = self.renderer.font.get_bold()
                self.renderer.font.set_bold(bold)
                tw, _ = self.renderer.font.size(txt)
                self.renderer.font.set_bold(prev)
            else:
                tw, _ = self.renderer.font.size(str(line))
            max_w = max(max_w, tw)
        w = self.renderer.padding_x * 2 + max_w
        rows_h = (len(lines) or 1) * self.renderer.line_height + max(0, (len(lines) - 1)) * (self.renderer.item_gap - 2)
        # Fila de botones ocupa otra altura de línea
        rows_h += self.renderer.item_gap + self.renderer.line_height
        h = self.renderer.padding_y * 2 + rows_h
        sw, sh = self.screen.get_size()
        w = min(w, int(sw * 0.9))
        h = min(h, int(sh * 0.6))
        # Centrado
        x = (sw - w) // 2
        y = (sh - h) // 2
        panel_rect = pygame.Rect(x, y, w, h)

        # Calcular rects de botones centrados
        gap = max(16, self.renderer.item_gap)
        padding_btn_x = 16
        btn_h = self.renderer.line_height
        labels_w = [self.renderer.font.size(t)[0] for t in buttons]
        btn_ws = [lw + padding_btn_x * 2 for lw in labels_w]
        total_btn_w = sum(btn_ws) + gap * (len(buttons) - 1 if buttons else 0)
        # Asegurar que el panel sea al menos tan ancho como los botones
        if w < total_btn_w + self.renderer.padding_x * 2:
            w = min(total_btn_w + self.renderer.padding_x * 2, int(sw * 0.95))
            x = (sw - w) // 2
            panel_rect = pygame.Rect(x, y, w, h)
        start_x = x + (w - total_btn_w) // 2
        btn_y = y + h - self.renderer.padding_y - btn_h
        button_rects = []
        cx = start_x
        for bw in btn_ws:
            button_rects.append(pygame.Rect(cx, btn_y, bw, btn_h))
            cx += bw + gap

        if not redraw:
            return {"panel_rect": panel_rect, "button_rects": button_rects}

        # Dibujo
        # Overlay
        overlay = pygame.Surface((sw, sh), pygame.SRCALPHA)
        overlay.fill(self.renderer.overlay_color)
        self.screen.blit(overlay, (0, 0))
        # Sombra y panel
        # Sombra simple
        sx, sy = self.renderer.shadow_offset
        shadow_rect = panel_rect.move(sx, sy)
        shadow_surf = pygame.Surface((shadow_rect.width, shadow_rect.height), pygame.SRCALPHA)
        pygame.draw.rect(shadow_surf, (0, 0, 0, 110), shadow_surf.get_rect(), border_radius=self.renderer.radius + 2)
        self.screen.blit(shadow_surf, shadow_rect.topleft)
        # Panel
        panel = pygame.Surface((w, h), pygame.SRCALPHA)
        color = (*self.renderer.panel_bg, self.renderer.panel_alpha)
        pygame.draw.rect(panel, color, panel.get_rect(), border_radius=self.renderer.radius)
        # Texto
        ty = self.renderer.padding_y
        for line in lines:
            if isinstance(line, dict):
                txt = line.get("text", "")
                color = line.get("color", self.renderer.text_color)
                bold = bool(line.get("bold", False))
                prev = self.renderer.font.get_bold()
                self.renderer.font.set_bold(bold)
                t = self.renderer.font.render(txt, True, color)
                self.renderer.font.set_bold(prev)
            else:
                t = self.renderer.font.render(str(line), True, self.renderer.text_color)
            ly = ty + (self.renderer.line_height - t.get_height()) // 2
            panel.blit(t, (self.renderer.padding_x, ly))
            ty += self.renderer.line_height + (self.renderer.item_gap - 2)
        # Dibujar botones
        for i, rect in enumerate(button_rects):
            # Estilos por botón
            label = buttons[i]
            if label == "CANCELAR":
                bg = (206, 64, 64)
                br = (240, 96, 96)
            elif label == "ACEPTAR":
                bg = (60, 160, 95)
                br = (100, 200, 140)
            elif label == "BORRAR":
                bg = (210, 170, 60)
                br = (240, 200, 90)
            else:
                bg = (50, 52, 58)
                br = (180, 185, 195)

            local_rect = pygame.Rect(rect.x - x, rect.y - y, rect.width, rect.height)
            # Hover glow
            if hover_index == i:
                glow = pygame.Surface((local_rect.width + 8, local_rect.height + 8), pygame.SRCALPHA)
                pygame.draw.rect(glow, (*br, 60), glow.get_rect(), border_radius=10)
                panel.blit(glow, (local_rect.x - 4, local_rect.y - 4))
            # Fondo botón
            pygame.draw.rect(panel, bg, local_rect, border_radius=10)
            # Borde (usar mismo tono de borde, con hover ya resaltado por "glow")
            border_color = br
            pygame.draw.rect(panel, border_color, local_rect, width=2, border_radius=10)
            # Etiqueta (blanco para buen contraste)
            tt = self.renderer.font.render(label, True, (255, 255, 255))
            tx = local_rect.x + (local_rect.width - tt.get_width()) // 2
            ty2 = local_rect.y + (local_rect.height - tt.get_height()) // 2
            panel.blit(tt, (tx, ty2))
        self.screen.blit(panel, panel_rect.topleft)

        return {"panel_rect": panel_rect, "button_rects": button_rects}
