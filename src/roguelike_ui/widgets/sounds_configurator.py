import pygame
from roguelike_ui.widgets.menu_renderer import MenuRenderer

class SoundsConfigurator:
    """
    Configurador de sonido con sliders para music / ambient / sfx.
    Controles:
    - Arriba/Abajo: seleccionar fila
    - Izquierda/Derecha: ajustar volumen (-/+ 5%)
    - Teclas numéricas 0..9: salto rápido a 0%,10%,...,90% (Shift+0 -> 100%)
    - R: restablecer valores por defecto
    - ESC: volver
    Cambios se persisten en AudioConfig y se puede inyectar un callback on_change(kind, value)
    para aplicar en vivo (p.ej., música del menú).
    """
    def __init__(self, screen, audio_config, on_change=None, font=None, underlay_provider=None, base_font_size: int | None = None):
        self.screen = screen
        self.audio_config = audio_config
        self.on_change = on_change  # callable(kind:str, value:float)
        # Dibuja fondo/logo si aplica (menú de inicio) y devuelve Y mínima del panel
        self.underlay_provider = underlay_provider
        # Usar el tamaño de fuente estandarizado si se provee; si no, derivar del font
        if isinstance(base_font_size, int) and base_font_size > 6:
            self.renderer = MenuRenderer(font_size=int(base_font_size))
        else:
            try:
                base_size = font.get_height() if font else 18
                fs = int(base_size)
            except Exception:
                fs = 18
            self.renderer = MenuRenderer(font_size=fs)
        self._fixed_panel_size = None
        self._fixed_screen_size = None
        # Foco
        self.selected = 0  # 0: music, 1: ambient, 2: sfx
        # Cache de valores en 0..1
        self.values = {
            'music': float(self.audio_config.get('music')),
            'ambient': float(self.audio_config.get('ambient')),
            'sfx': float(self.audio_config.get('sfx')),
        }
        # Estado de mute y último valor distinto de cero para restaurar
        self._muted = {k: (self.values[k] <= 0.0) for k in self.values}
        self._last_non_zero = {k: (self.values[k] if self.values[k] > 0 else 0.6) for k in self.values}
        # Hover indices
        self._hover_value_idx = None
        self._hover_mute_idx = None
        self._hover_reset_idx = None

    def configure(self):
        running = True
        clock = pygame.time.Clock()
        while running:
            # Recalcular tamaño fijo respecto a pantalla
            if self._fixed_screen_size != self.screen.get_size():
                self._compute_fixed_layout()
            for event in pygame.event.get():
                if event.type == pygame.QUIT:
                    running = False
                    break
                if event.type == pygame.KEYDOWN:
                    if event.key in (pygame.K_ESCAPE,):
                        running = False
                        break
                    elif event.key in (pygame.K_UP, pygame.K_w):
                        self.selected = (self.selected - 1) % 3
                    elif event.key in (pygame.K_DOWN, pygame.K_s):
                        self.selected = (self.selected + 1) % 3
                    elif event.key in (pygame.K_LEFT, pygame.K_a):
                        self._nudge(-0.05)
                    elif event.key in (pygame.K_RIGHT, pygame.K_d):
                        self._nudge(+0.05)
                    elif event.key == pygame.K_m:
                        self._toggle_mute(self.selected)
                    elif event.key == pygame.K_d:
                        self._reset_channel(self.selected)
                    elif event.key == pygame.K_r:
                        self._reset_defaults()
                    elif pygame.K_0 <= event.key <= pygame.K_9:
                        # 0..9 -> 0..90%, con Shift+0 => 100%
                        num = event.key - pygame.K_0
                        pct = 1.0 if (num == 0 and pygame.key.get_mods() & pygame.KMOD_SHIFT) else (num / 10.0)
                        self._set_selected(pct)
                elif event.type == pygame.MOUSEWHEEL:
                    # Rueda: ajustar volumen del seleccionado
                    self._nudge(event.y * 0.02)
                elif event.type == pygame.MOUSEBUTTONDOWN and event.button == 1:
                    # Click sobre slider para posicionar
                    layout = getattr(self, '_last_layout', None)
                    if layout:
                        sliders = layout.get('slider_rects', [])
                        vals_rects = layout.get('value_rects', [])
                        mute_rects = layout.get('mute_rects', [])
                        reset_rects = layout.get('reset_rects', [])
                        mx, my = event.pos
                        handled = False
                        # 1) Mute tiene prioridad si se hace click sobre el botón
                        for i, mrect in enumerate(mute_rects):
                            if mrect and mrect.collidepoint((mx, my)):
                                self._toggle_mute(i)
                                handled = True
                                break
                        # 1b) Por defecto
                        if not handled:
                            for i, rrect in enumerate(reset_rects):
                                if rrect and rrect.collidepoint((mx, my)):
                                    self._reset_channel(i)
                                    handled = True
                                    break
                        # 2) Slider
                        if not handled:
                            for i, srect in enumerate(sliders):
                                if srect and srect.collidepoint((mx, my)):
                                    rel = (mx - srect.x) / max(1, srect.w)
                                    self._set_index(i, rel)
                                    handled = True
                                    break
                        # 3) Porcentaje -> seleccionar fila
                        if not handled:
                            for i, vrect in enumerate(vals_rects):
                                if vrect and vrect.collidepoint((mx, my)):
                                    self.selected = i
                                    handled = True
                                    break
                elif event.type == pygame.MOUSEMOTION:
                    layout = getattr(self, '_last_layout', None)
                    self._hover_value_idx = None
                    self._hover_mute_idx = None
                    self._hover_reset_idx = None
                    if layout:
                        vals_rects = layout.get('value_rects', [])
                        mute_rects = layout.get('mute_rects', [])
                        reset_rects = layout.get('reset_rects', [])
                        for i, vrect in enumerate(vals_rects):
                            if vrect and vrect.collidepoint(event.pos):
                                self._hover_value_idx = i
                                break
                        for i, mrect in enumerate(mute_rects):
                            if mrect and mrect.collidepoint(event.pos):
                                self._hover_mute_idx = i
                                break
                        for i, rrect in enumerate(reset_rects):
                            if rrect and rrect.collidepoint(event.pos):
                                self._hover_reset_idx = i
                                break
            # Dibujar panel
            self._draw()
            pygame.display.flip()
            clock.tick(60)

    def _reset_defaults(self):
        defaults = {'music': 0.6, 'ambient': 0.6, 'sfx': 0.7}
        for k, v in defaults.items():
            self.values[k] = v
            self.audio_config.set(k, v)
            if callable(self.on_change):
                self.on_change(k, v)

    def _nudge(self, delta: float):
        key = ('music', 'ambient', 'sfx')[self.selected]
        v = self.values[key]
        nv = max(0.0, min(1.0, v + delta))
        if abs(nv - v) >= 1e-6:
            self.values[key] = nv
            self.audio_config.set(key, nv)
            if callable(self.on_change):
                self.on_change(key, nv)
            # Actualizar estados de mute y último no-cero
            if nv <= 0.0:
                self._muted[key] = True
            else:
                self._muted[key] = False
                self._last_non_zero[key] = nv

    def _set_selected(self, value: float):
        key = ('music', 'ambient', 'sfx')[self.selected]
        nv = max(0.0, min(1.0, float(value)))
        self.values[key] = nv
        self.audio_config.set(key, nv)
        if callable(self.on_change):
            self.on_change(key, nv)
        if nv <= 0.0:
            self._muted[key] = True
        else:
            self._muted[key] = False
            self._last_non_zero[key] = nv

    def _set_index(self, index: int, value: float):
        key = ('music', 'ambient', 'sfx')[max(0, min(2, index))]
        nv = max(0.0, min(1.0, float(value)))
        self.values[key] = nv
        self.audio_config.set(key, nv)
        if callable(self.on_change):
            self.on_change(key, nv)
        if nv <= 0.0:
            self._muted[key] = True
        else:
            self._muted[key] = False
            self._last_non_zero[key] = nv

    def _toggle_mute(self, index: int):
        key = ('music', 'ambient', 'sfx')[max(0, min(2, index))]
        if not self._muted.get(key, False):
            # Mutear: guardar último valor no-cero y poner 0
            if self.values[key] > 0:
                self._last_non_zero[key] = self.values[key]
            self.values[key] = 0.0
            self._muted[key] = True
        else:
            # Desmutear: restaurar último valor o un default razonable
            restored = self._last_non_zero.get(key, 0.6)
            restored = 0.6 if restored <= 0.0 else restored
            self.values[key] = restored
            self._muted[key] = False
        # Persistir y aplicar
        self.audio_config.set(key, self.values[key])
        if callable(self.on_change):
            self.on_change(key, self.values[key])

    def _reset_channel(self, index: int):
        key = ('music', 'ambient', 'sfx')[max(0, min(2, index))]
        defaults = {'music': 0.6, 'ambient': 0.6, 'sfx': 0.7}
        nv = defaults.get(key, 0.6)
        self.values[key] = nv
        self._muted[key] = (nv <= 0.0)
        if nv > 0.0:
            self._last_non_zero[key] = nv
        self.audio_config.set(key, nv)
        if callable(self.on_change):
            self.on_change(key, nv)

    def _compute_fixed_layout(self):
        # Panel dinámico basado en contenido, clamped a la pantalla
        sw, sh = self.screen.get_size()
        # Ancho preferido ~70% pantalla (con mínimo para evitar solapes)
        w = min(int(sw * 0.8), max(520, int(sw * 0.7)))
        # Alto: título + 3 filas + instrucciones (2 líneas)
        title_h = self.renderer.line_height
        rows_h = 3 * (self.renderer.line_height + self.renderer.item_gap)
        instr_h = 2 * self.renderer.line_height
        h = self.renderer.padding_y * 2 + title_h + self.renderer.item_gap + rows_h + self.renderer.item_gap + instr_h
        # Clamp a 70% de pantalla
        h = min(h, int(sh * 0.7))
        self._fixed_panel_size = (w, h)
        self._fixed_screen_size = (sw, sh)

    def _draw(self):
        # Underlay (persistir background/logo si venimos del menú de inicio)
        panel_top_min = None
        if callable(self.underlay_provider):
            try:
                panel_top_min = self.underlay_provider(self.screen)
            except Exception:
                panel_top_min = None
        # Overlay
        overlay_rect = self.renderer._draw_overlay(self.screen)
        # Panel
        if not self._fixed_panel_size:
            self._compute_fixed_layout()
        w, h = self._fixed_panel_size
        panel_rect = self.renderer._center_rect(self.screen, (w, h))
        # Empujar panel hacia abajo si hay un logo encima
        if isinstance(panel_top_min, int) and panel_rect.top < panel_top_min:
            panel_rect.top = panel_top_min
        self.renderer._draw_shadow(self.screen, panel_rect)
        panel = self.renderer._draw_panel((w, h))
        # Título
        title = self.renderer.font.render("Opciones de Sonido", True, self.renderer.text_color)
        panel.blit(title, (self.renderer.padding_x, self.renderer.padding_y))
        y = self.renderer.padding_y + self.renderer.line_height + self.renderer.item_gap
        # Filas: Música, Ambiente, SFX
        self._last_layout = {"slider_rects": [], "value_rects": [], "mute_rects": [], "reset_rects": []}
        labels = ["Música", "Ambiente", "SFX"]
        # Columna de etiquetas: calcular ancho máximo
        label_max_w = 0
        for lbl in labels:
            tw, _ = self.renderer.font.size(lbl)
            label_max_w = max(label_max_w, tw)
        label_col_w = label_max_w + 20
        for i, key in enumerate(("music", "ambient", "sfx")):
            is_sel = (i == self.selected)
            color = self.renderer.accent_color if is_sel else self.renderer.text_color
            # Resaltado de fila seleccionada
            if is_sel:
                row_pill = pygame.Rect(self.renderer.padding_x - 6, y, w - (self.renderer.padding_x * 2) + 12, self.renderer.line_height)
                pygame.draw.rect(panel, self.renderer.highlight_color, row_pill, border_radius=self.renderer.radius // 2)
            # Etiqueta
            t = self.renderer.font.render(labels[i], True, color)
            panel.blit(t, (self.renderer.padding_x, y))

            # Reservas a la derecha: % + botón Mute/Unmute + botón Def.
            percent_probe = self.renderer.font.render("100%", True, color)
            percent_w = percent_probe.get_width()
            btn_label = "Mute" if not self._muted.get(key, False) else "Unmute"
            bt = self.renderer.font.render(btn_label, True, self.renderer.text_color)
            # Botón Por defecto (abreviado)
            rt = self.renderer.font.render("Def.", True, self.renderer.text_color)
            btn_pad_x = 14
            btn_w = bt.get_width() + btn_pad_x * 2
            reset_w = rt.get_width() + btn_pad_x * 2
            gap_btns = 12
            right_reserved = 12 + percent_w + 16 + btn_w + gap_btns + reset_w

            # Slider
            slider_h = 10
            sx = self.renderer.padding_x + max(120, label_col_w)
            sy = y + (self.renderer.line_height - slider_h) // 2
            slider_w = max(160, w - self.renderer.padding_x * 2 - (sx - self.renderer.padding_x) - right_reserved)
            track_rect = pygame.Rect(sx, sy, max(0, slider_w), slider_h)
            pygame.draw.rect(panel, (255, 255, 255, 35), track_rect, border_radius=6)
            # Ticks 0/25/50/75/100
            tick_color = (255, 255, 255, 60)
            for t in (0.0, 0.25, 0.5, 0.75, 1.0):
                tx = sx + int(slider_w * t)
                pygame.draw.line(panel, tick_color, (tx, sy - 4), (tx, sy + slider_h + 4), 1)
            val = self.values[key]
            thumb_w = max(4, int(slider_w * val))
            pygame.draw.rect(panel, self.renderer.accent_color, pygame.Rect(sx, sy, thumb_w, slider_h), border_radius=6)

            # Porcentaje a la derecha
            pct = int(round(val * 100))
            v_color = self.renderer.accent_color if (self._hover_value_idx == i) else color
            vtxt = self.renderer.font.render(f"{pct}%", True, v_color)
            vx = sx + slider_w + 12
            vy = y + (self.renderer.line_height - vtxt.get_height()) // 2
            panel.blit(vtxt, (vx, vy))
            # Indicador ON/OFF compacto junto al %
            if self._muted.get(key, False):
                st = self.renderer.font.render("OFF", True, (200, 80, 80))
                panel.blit(st, (vx - st.get_width() - 8, vy))
            else:
                st = self.renderer.font.render("ON", True, (120, 200, 120))
                panel.blit(st, (vx - st.get_width() - 8, vy))
            # Hover del %
            if self._hover_value_idx == i:
                vr = pygame.Rect(vx - 4, vy - 2, vtxt.get_width() + 8, vtxt.get_height() + 4)
                pygame.draw.rect(panel, self.renderer.border_color, vr, width=2, border_radius=6)

            # Botón Mute/Unmute
            btn_h = self.renderer.line_height
            bx = min(self.renderer.padding_x + w - self.renderer.padding_x - btn_w, vx + vtxt.get_width() + 16)
            by = y
            btn_rect = pygame.Rect(bx, by, btn_w, btn_h)
            pygame.draw.rect(panel, (255, 255, 255, 22), btn_rect, border_radius=self.renderer.radius // 2)
            is_hover_btn = (self._hover_mute_idx == i)
            if is_hover_btn or self._muted.get(key, False):
                pygame.draw.rect(panel, self.renderer.border_color, btn_rect, width=2, border_radius=self.renderer.radius // 2)
            btx = bx + (btn_w - bt.get_width()) // 2
            bty = by + (btn_h - bt.get_height()) // 2
            panel.blit(bt, (btx, bty))

            # Botón Def. a la derecha del Mute/Unmute
            rx = min(self.renderer.padding_x + w - self.renderer.padding_x - reset_w, bx + btn_w + gap_btns)
            ry = y
            reset_rect = pygame.Rect(rx, ry, reset_w, btn_h)
            pygame.draw.rect(panel, (255, 255, 255, 22), reset_rect, border_radius=self.renderer.radius // 2)
            is_hover_reset = (self._hover_reset_idx == i)
            if is_hover_reset:
                pygame.draw.rect(panel, self.renderer.border_color, reset_rect, width=2, border_radius=self.renderer.radius // 2)
            rtx = rx + (reset_w - rt.get_width()) // 2
            rty = ry + (btn_h - rt.get_height()) // 2
            panel.blit(rt, (rtx, rty))

            # Guardar rects para interacción
            self._last_layout['slider_rects'].append(track_rect.move(panel_rect.topleft))
            self._last_layout['value_rects'].append(pygame.Rect(vx, vy, vtxt.get_width(), vtxt.get_height()).move(panel_rect.topleft))
            self._last_layout['mute_rects'].append(btn_rect.move(panel_rect.topleft))
            self._last_layout['reset_rects'].append(reset_rect.move(panel_rect.topleft))

            # Siguiente fila
            y += self.renderer.line_height + self.renderer.item_gap
        # Instrucciones
        lines = [
            "Arriba/Abajo: seleccionar | Izq/Der: ajustar | Clic en barra: posicionar",
            "Ticks 0/25/50/75/100 | R: reset todo | D: Def. (canal) | M: Mute | ESC: volver",
        ]
        iy = h - self.renderer.padding_y - len(lines) * (self.renderer.line_height)
        for line in lines:
            lt = self.renderer.font.render(line, True, self.renderer.text_color_dim)
            panel.blit(lt, (self.renderer.padding_x, iy))
            iy += self.renderer.line_height
        # Blit panel
        surface_to_blit = panel._surf if hasattr(panel, '_surf') else panel
        self.screen.blit(surface_to_blit, panel_rect.topleft)
        return overlay_rect
