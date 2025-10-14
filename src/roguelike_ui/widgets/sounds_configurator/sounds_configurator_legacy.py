import pygame
import json
from pathlib import Path
from roguelike_ui.widgets.menu_renderer.menu_renderer import MenuRenderer
try:
    from roguelike_engine.audio.api import apply_audio_config_now
except Exception:
    def apply_audio_config_now():
        pass

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
        # Scroll
        self._scroll = 0.0
        self._max_scroll = 0.0
        self._last_viewport_h = 0
        # Foco
        # Mapeo de filas:
        # 0 music, 1 ambient, 2 sfx (volúmenes)
        # 3 intro_track, 4 ingame_track
        # 5 ambient_min_interval, 6 ambient_max_interval
        # 7 duck_amount_db, 8 duck_hold_ms, 9 duck_release_ms
        # 10 zone_select, 11 zone_music_track, 12 zone_ambient_min, 13 zone_ambient_max
        self.selected = 0
        self._row_count = 14
        # Cache de valores en 0..1
        self.values = {
            'music': float(self.audio_config.get('music')),
            'ambient': float(self.audio_config.get('ambient')),
            'sfx': float(self.audio_config.get('sfx')),
        }
        # Ruta y datos del JSON de audio
        try:
            self._audio_json_path = Path('data/config/audio.json')
        except Exception:
            self._audio_json_path = None
        self._audio_json = {}
        self._tracks = []
        self._intro_track = None
        self._ingame_track = None
        # Ambient defaults
        self._ambient_min = 6.0
        self._ambient_max = 18.0
        # Ducking defaults
        self._duck_db = -4.0
        self._duck_hold = 250
        self._duck_release = 200
        # Zonas (para asignación por zona)
        self._zones = []
        self._zone_index = 0
        self._zone_track = {}
        self._zone_ambient_min = {}
        self._zone_ambient_max = {}
        self._load_audio_json()
        self._load_zones_json()
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
                    elif event.key == pygame.K_p:
                        # Aplicar ahora (recargar catálogo y reprogramar ambient/música)
                        try:
                            apply_audio_config_now()
                        except Exception:
                            pass
                    elif event.key in (pygame.K_UP, pygame.K_w):
                        self.selected = (self.selected - 1) % self._row_count
                        self._ensure_visible(self.selected)
                    elif event.key in (pygame.K_DOWN, pygame.K_s):
                        self.selected = (self.selected + 1) % self._row_count
                        self._ensure_visible(self.selected)
                    elif event.key == pygame.K_PAGEUP:
                        self._scroll = max(0.0, self._scroll - max(24, int(self._last_viewport_h * 0.9)))
                    elif event.key == pygame.K_PAGEDOWN:
                        self._scroll = min(self._max_scroll, self._scroll + max(24, int(self._last_viewport_h * 0.9)))
                    elif event.key == pygame.K_HOME:
                        self._scroll = 0.0
                    elif event.key == pygame.K_END:
                        self._scroll = self._max_scroll
                    elif event.key in (pygame.K_LEFT, pygame.K_a):
                        self._nudge_selected(-1)
                    elif event.key in (pygame.K_RIGHT, pygame.K_d):
                        self._nudge_selected(+1)
                    elif event.key == pygame.K_m:
                        if self.selected in (0, 1, 2):
                            self._toggle_mute(self.selected)
                    elif event.key == pygame.K_d:
                        if self.selected in (0, 1, 2):
                            self._reset_channel(self.selected)
                    elif event.key == pygame.K_r:
                        self._reset_defaults()
                    elif pygame.K_0 <= event.key <= pygame.K_9:
                        # 0..9 -> 0..90%, con Shift+0 => 100%
                        if self.selected in (0, 1, 2):
                            num = event.key - pygame.K_0
                            pct = 1.0 if (num == 0 and pygame.key.get_mods() & pygame.KMOD_SHIFT) else (num / 10.0)
                            self._set_selected(pct)
                elif event.type == pygame.MOUSEWHEEL:
                    # Rueda: scroll por defecto; si el cursor está sobre un slider 0..2, ajustar volumen
                    layout = getattr(self, '_last_layout', None)
                    hovered_slider = None
                    if layout:
                        try:
                            mx, my = pygame.mouse.get_pos()
                            for i, srect in enumerate(layout.get('slider_rects') or []):
                                if srect and srect.collidepoint((mx, my)):
                                    hovered_slider = i
                                    break
                        except Exception:
                            hovered_slider = None
                    if hovered_slider in (0, 1, 2):
                        self.selected = hovered_slider
                        self._nudge(event.y * 0.02)
                    else:
                        step = self.renderer.line_height
                        self._scroll = max(0.0, min(self._max_scroll, self._scroll - event.y * step))
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
                        # 2) Slider solo para filas 0..2 (volúmenes)
                        if not handled:
                            for i, srect in enumerate(sliders[:3]):
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
        # Avanzado por defecto
        self._intro_track = self._tracks[0] if self._tracks else self._intro_track
        # Mantener ingame actual si existe; si no, first
        self._ingame_track = self._ingame_track or (self._tracks[0] if self._tracks else None)
        self._ambient_min, self._ambient_max = 6.0, 18.0
        self._duck_db, self._duck_hold, self._duck_release = -4.0, 250, 200
        self._save_audio_json()

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

    def _nudge_selected(self, step: int):
        # step: -1 izquierda, +1 derecha
        if self.selected in (0, 1, 2):
            self._nudge(0.05 * step)
            return
        # Tracks
        if self.selected in (3, 4):
            if not self._tracks:
                return
            cur = self._intro_track if self.selected == 3 else self._ingame_track
            try:
                idx = self._tracks.index(cur)
            except Exception:
                idx = 0
            idx = (idx + step) % len(self._tracks)
            if self.selected == 3:
                self._intro_track = self._tracks[idx]
            else:
                self._ingame_track = self._tracks[idx]
            self._save_audio_json()
            return
        # Ambient intervals (global)
        if self.selected == 5:
            self._ambient_min = max(0.0, min(60.0, float(self._ambient_min) + 0.5 * step))
            if self._ambient_min > self._ambient_max:
                self._ambient_max = self._ambient_min
            self._save_audio_json()
            return
        if self.selected == 6:
            self._ambient_max = max(0.0, min(120.0, float(self._ambient_max) + 0.5 * step))
            if self._ambient_max < self._ambient_min:
                self._ambient_min = self._ambient_max
            self._save_audio_json()
            return
        # Ducking params
        if self.selected == 7:
            self._duck_db = max(-24.0, min(0.0, float(self._duck_db) + 1.0 * step))
            self._save_audio_json()
            return
        if self.selected == 8:
            self._duck_hold = int(max(0, min(2000, int(self._duck_hold) + 25 * step)))
            self._save_audio_json()
            return
        if self.selected == 9:
            self._duck_release = int(max(0, min(2000, int(self._duck_release) + 25 * step)))
            self._save_audio_json()
            return
        # Zonas
        if self.selected == 10:
            if self._zones:
                self._zone_index = (self._zone_index + step) % len(self._zones)
            return
        if self.selected == 11:
            if not self._zones or not self._tracks:
                return
            zname = self._zones[self._zone_index]
            tr = self._zone_track.get(zname)
            try:
                idx = self._tracks.index(tr)
            except Exception:
                idx = 0
            idx = (idx + step) % len(self._tracks)
            self._zone_track[zname] = self._tracks[idx]
            self._save_audio_json()
            return
        if self.selected in (12, 13):
            if not self._zones:
                return
            zname = self._zones[self._zone_index]
            if self.selected == 12:
                self._zone_ambient_min[zname] = max(0.0, min(60.0, float(self._zone_ambient_min.get(zname, self._ambient_min)) + 0.5 * step))
                if self._zone_ambient_min[zname] > self._zone_ambient_max.get(zname, self._ambient_max):
                    self._zone_ambient_max[zname] = self._zone_ambient_min[zname]
            else:
                self._zone_ambient_max[zname] = max(0.0, min(120.0, float(self._zone_ambient_max.get(zname, self._ambient_max)) + 0.5 * step))
                if self._zone_ambient_max[zname] < self._zone_ambient_min.get(zname, self._ambient_min):
                    self._zone_ambient_min[zname] = self._zone_ambient_max[zname]
            self._save_audio_json()
            return

    def _ensure_visible(self, row_index: int) -> None:
        """Ajusta el scroll para que la fila indicada quede visible en el viewport."""
        # Cálculo local del área de contenido (entre título y leyendas)
        content_top = self.renderer.padding_y + self.renderer.line_height + self.renderer.item_gap
        # Leyendas fijas al fondo (4 líneas)
        h = (self._fixed_panel_size or (0, 0))[1] or self.screen.get_size()[1] * 0.7
        h = int(h)
        legends_h = 4 * self.renderer.line_height
        content_bottom = h - self.renderer.padding_y - legends_h
        viewport_h = max(0, content_bottom - content_top)
        self._last_viewport_h = viewport_h
        row_h = self.renderer.line_height + self.renderer.item_gap
        row_y = content_top + row_index * row_h
        # Asegurar visibilidad
        if row_y < content_top + self._scroll:
            self._scroll = max(0.0, row_y - content_top)
        elif row_y + self.renderer.line_height > content_top + self._scroll + viewport_h:
            self._scroll = min(max(0.0, row_y + self.renderer.line_height - content_top - viewport_h), self._max_scroll)

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
        rows_h = self._row_count * (self.renderer.line_height + self.renderer.item_gap)
        instr_h = 3 * self.renderer.line_height
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
        # Área scrolleable: desde debajo del título hasta antes de las leyendas
        content_top = self.renderer.padding_y + self.renderer.line_height + self.renderer.item_gap
        legends_lines = 4
        legends_h = legends_lines * self.renderer.line_height
        content_bottom = h - self.renderer.padding_y - legends_h
        viewport_h = max(0, content_bottom - content_top)
        self._last_viewport_h = viewport_h
        # Altura total del contenido (todas las filas)
        row_h = self.renderer.line_height + self.renderer.item_gap
        content_total_h = self._row_count * row_h
        self._max_scroll = max(0.0, content_total_h - viewport_h)
        # Clamp del scroll
        self._scroll = max(0.0, min(self._max_scroll, float(self._scroll)))
        # Punto inicial de dibujo considerando el scroll
        y = content_top - self._scroll
        # Filas: Música, Ambiente, SFX + avanzadas
        self._last_layout = {"slider_rects": [], "value_rects": [], "mute_rects": [], "reset_rects": []}
        labels = [
            "Música", "Ambiente", "SFX",
            "Intro (track)", "In-game (track)",
            "Ambiente: intervalo mínimo (s)", "Ambiente: intervalo máximo (s)",
            "Ducking: atenuación (dB)", "Ducking: hold (ms)", "Ducking: release (ms)",
            "Zona (seleccionar)", "Zona: música (track)", "Zona: ambiente mínimo (s)", "Zona: ambiente máximo (s)"
        ]
        # Columna de etiquetas: calcular ancho máximo
        label_max_w = 0
        for lbl in labels:
            tw, _ = self.renderer.font.size(lbl)
            label_max_w = max(label_max_w, tw)
        label_col_w = label_max_w + 20
        for i in range(self._row_count):
            # Si la fila queda fuera del viewport, saltar su dibujo pero mantener índices
            row_top = y
            row_bottom = y + self.renderer.line_height
            if row_bottom < content_top or row_top > content_bottom:
                self._last_layout['slider_rects'].append(None)
                self._last_layout['value_rects'].append(None)
                self._last_layout['mute_rects'].append(None)
                self._last_layout['reset_rects'].append(None)
                y += row_h
                continue
            # Determinar tipo de fila
            if i in (0, 1, 2):
                key = ("music", "ambient", "sfx")[i]
            else:
                key = None
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
            # Por defecto, no hay botones para filas que no son 0..2
            btn_rect = None
            reset_rect = None

            # Slider / valor
            slider_h = 10
            sx = self.renderer.padding_x + max(120, label_col_w)
            sy = y + (self.renderer.line_height - slider_h) // 2
            slider_w = max(160, w - self.renderer.padding_x * 2 - (sx - self.renderer.padding_x) - right_reserved)
            track_rect = pygame.Rect(sx, sy, max(0, slider_w), slider_h)
            pygame.draw.rect(panel, (255, 255, 255, 35), track_rect, border_radius=6)
            tick_color = (255, 255, 255, 60)
            # Diferenciar tipos
            if i in (0, 1, 2):
                # Ticks 0-100%
                for tck in (0.0, 0.25, 0.5, 0.75, 1.0):
                    tx = sx + int(slider_w * tck)
                    pygame.draw.line(panel, tick_color, (tx, sy - 4), (tx, sy + slider_h + 4), 1)
                val = self.values[key]
                thumb_w = max(4, int(slider_w * val))
                pygame.draw.rect(panel, self.renderer.accent_color, pygame.Rect(sx, sy, thumb_w, slider_h), border_radius=6)
            elif i in (3, 4):
                # Mostrar nombre de track
                track = self._intro_track if i == 3 else self._ingame_track
                disp = track or "(sin tracks)"
                tt = self.renderer.font.render(disp, True, color)
                panel.blit(tt, (sx + 8, y + (self.renderer.line_height - tt.get_height()) // 2))
            elif i in (5, 6):
                val = self._ambient_min if i == 5 else self._ambient_max
                vtxt = self.renderer.font.render(f"{val:.1f}s", True, color)
                panel.blit(vtxt, (sx + 8, y + (self.renderer.line_height - vtxt.get_height()) // 2))
            elif i in (7, 8, 9):
                if i == 7:
                    disp = f"{self._duck_db:.0f} dB"
                elif i == 8:
                    disp = f"{self._duck_hold} ms"
                else:
                    disp = f"{self._duck_release} ms"
                vtxt = self.renderer.font.render(disp, True, color)
                panel.blit(vtxt, (sx + 8, y + (self.renderer.line_height - vtxt.get_height()) // 2))
            elif i == 10:
                # Zona seleccionada (tomada de zones.json)
                disp = "(sin zonas)"
                if self._zones:
                    try:
                        disp = str(self._zones[self._zone_index])
                    except Exception:
                        disp = str(self._zones[0])
                vt = self.renderer.font.render(disp, True, color)
                panel.blit(vt, (sx + 8, y + (self.renderer.line_height - vt.get_height()) // 2))
            elif i == 11:
                # Track asignado a la zona actual
                zname = self._zones[self._zone_index] if self._zones else None
                tr = self._zone_track.get(zname) if zname else None
                disp = tr or "(sin tracks)"
                vt = self.renderer.font.render(disp, True, color)
                panel.blit(vt, (sx + 8, y + (self.renderer.line_height - vt.get_height()) // 2))
            elif i in (12, 13):
                # Min/Max de ambiente por zona
                zname = self._zones[self._zone_index] if self._zones else None
                val = float(self._zone_ambient_min.get(zname, self._ambient_min) if i == 12 else self._zone_ambient_max.get(zname, self._ambient_max))
                vtxt = self.renderer.font.render(f"{val:.1f}s", True, color)
                panel.blit(vtxt, (sx + 8, y + (self.renderer.line_height - vtxt.get_height()) // 2))

            # Botones Mute/Def. solo para canales 0..2
            if i in (0, 1, 2):
                btn_h = self.renderer.line_height
                bx = min(self.renderer.padding_x + w - self.renderer.padding_x - btn_w, sx + slider_w + 16)
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
            # Determinar superficie usada en la columna de valor/hints
            val_surface = None
            if i in (0, 1, 2):
                val = self.values[key]
                vtxt = self.renderer.font.render(f"{val*100:.0f}%", True, color)
                val_surface = vtxt
            elif i in (3, 4):
                track = self._intro_track if i == 3 else self._ingame_track
                disp = track or "(sin tracks)"
                tt = self.renderer.font.render(disp, True, color)
                val_surface = tt
            elif i in (5, 6):
                val = self._ambient_min if i == 5 else self._ambient_max
                vtxt = self.renderer.font.render(f"{val:.1f}s", True, color)
                val_surface = vtxt
            elif i in (7, 8, 9):
                if i == 7:
                    disp = f"{self._duck_db:.0f} dB"
                elif i == 8:
                    disp = f"{self._duck_hold} ms"
                else:
                    disp = f"{self._duck_release} ms"
                vtxt = self.renderer.font.render(disp, True, color)
                val_surface = vtxt
            elif i == 10:
                disp = "(sin zonas)"
                if self._zones:
                    try:
                        disp = str(self._zones[self._zone_index])
                    except Exception:
                        disp = str(self._zones[0])
                vt = self.renderer.font.render(disp, True, color)
                val_surface = vt
            elif i == 11:
                zname = self._zones[self._zone_index] if self._zones else None
                tr = self._zone_track.get(zname) if zname else None
                disp = tr or "(sin tracks)"
                vt = self.renderer.font.render(disp, True, color)
                val_surface = vt
            elif i in (12, 13):
                zname = self._zones[self._zone_index] if self._zones else None
                val = float(self._zone_ambient_min.get(zname, self._ambient_min) if i == 12 else self._zone_ambient_max.get(zname, self._ambient_max))
                vtxt = self.renderer.font.render(f"{val:.1f}s", True, color)
                val_surface = vtxt
            if val_surface is not None:
                vx = sx + 8
                vy = y + (self.renderer.line_height - val_surface.get_height()) // 2
                panel.blit(val_surface, (vx, vy))
                self._last_layout['value_rects'].append(pygame.Rect(vx, vy, val_surface.get_width(), val_surface.get_height()).move(panel_rect.topleft))
            else:
                self._last_layout['value_rects'].append(None)
            self._last_layout['mute_rects'].append(btn_rect.move(panel_rect.topleft) if btn_rect else None)
            self._last_layout['reset_rects'].append(reset_rect.move(panel_rect.topleft) if reset_rect else None)

            # Siguiente fila
            y += self.renderer.line_height + self.renderer.item_gap
        # Instrucciones (leyendas) fijas al pie del panel
        lines = [
            "Arriba/Abajo: seleccionar | Izq/Der: ajustar | P: Aplicar ahora",
            "Volúmenes: 0..100% | Tracks: ←/→ | Ambiente (s) y Ducking: ←/→ | Rueda: scroll",
            "Zonas: selecciona zona y asigna track e intervalos de ambiente",
            "R: reset volúmenes y avanzados por defecto | D: Def. (solo canal seleccionado) | M: Mute | ESC: volver",
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

    # --- JSON helpers ---
    def _load_audio_json(self):
        try:
            if self._audio_json_path and self._audio_json_path.exists():
                self._audio_json = json.loads(self._audio_json_path.read_text(encoding='utf-8'))
            else:
                self._audio_json = {}
        except Exception:
            self._audio_json = {}
        # Tracks
        self._tracks = list((self._audio_json.get('tracks') or {}).keys())
        dm = (self._audio_json.get('defaults') or {}).get('music') or {}
        self._intro_track = dm.get('startup_track_id') or (self._tracks[0] if self._tracks else None)
        self._ingame_track = dm.get('ingame_track_id') or (self._tracks[0] if self._tracks else None)
        # Ambient
        da = (self._audio_json.get('defaults') or {}).get('ambient') or {}
        self._ambient_min = float(da.get('min_interval', 6.0))
        self._ambient_max = float(da.get('max_interval', 18.0))
        # Ducking
        dk = (self._audio_json.get('defaults') or {}).get('ducking') or {}
        self._duck_db = float(dk.get('amount_db', -4.0))
        self._duck_hold = int(dk.get('hold_ms', 250))
        self._duck_release = int(dk.get('release_ms', 200))
        # Zonas configuradas en audio.json (track + ambient overrides)
        zones = (self._audio_json.get('zones') or {})
        for zname, zcfg in zones.items():
            if not isinstance(zcfg, dict):
                continue
            mt = zcfg.get('music_track_id')
            if isinstance(mt, str):
                self._zone_track[zname] = mt
            amb = zcfg.get('ambient') or {}
            if isinstance(amb, dict):
                if 'min_interval' in amb:
                    self._zone_ambient_min[zname] = float(amb.get('min_interval'))
                if 'max_interval' in amb:
                    self._zone_ambient_max[zname] = float(amb.get('max_interval'))

    def _save_audio_json(self):
        try:
            data = dict(self._audio_json or {})
            defaults = data.setdefault('defaults', {})
            # Music defaults
            md = defaults.setdefault('music', {})
            if self._intro_track:
                md['startup_track_id'] = self._intro_track
            if self._ingame_track:
                md['ingame_track_id'] = self._ingame_track
            # Ambient defaults
            ad = defaults.setdefault('ambient', {})
            ad['min_interval'] = float(self._ambient_min)
            ad['max_interval'] = float(self._ambient_max)
            # Ducking defaults
            dk = defaults.setdefault('ducking', {})
            dk['amount_db'] = float(self._duck_db)
            dk['hold_ms'] = int(self._duck_hold)
            dk['release_ms'] = int(self._duck_release)
            # Zonas: persistir solo las que tengan cambios
            if self._zone_track or self._zone_ambient_min or self._zone_ambient_max:
                zdict = data.setdefault('zones', {})
                # Unir claves vistas en cualquier diccionario zonal
                all_z = set(self._zone_track.keys()) | set(self._zone_ambient_min.keys()) | set(self._zone_ambient_max.keys())
                for zname in sorted(all_z):
                    rec = zdict.setdefault(zname, {})
                    mt = self._zone_track.get(zname)
                    if mt:
                        rec['music_track_id'] = mt
                    amb = rec.setdefault('ambient', {})
                    if zname in self._zone_ambient_min:
                        amb['min_interval'] = float(self._zone_ambient_min[zname])
                    if zname in self._zone_ambient_max:
                        amb['max_interval'] = float(self._zone_ambient_max[zname])
            # Guardar
            if self._audio_json_path:
                self._audio_json_path.write_text(json.dumps(data, indent=2), encoding='utf-8')
            self._audio_json = data
        except Exception:
            pass

    def _load_zones_json(self):
        """Lee data/map/zones/zones.json para descubrir zonas disponibles."""
        try:
            zpath = Path('data/map/zones/zones.json')
            if zpath.exists():
                data = json.loads(zpath.read_text(encoding='utf-8'))
                if isinstance(data, dict):
                    self._zones = sorted([str(k) for k in data.keys()])
                else:
                    self._zones = []
            else:
                self._zones = []
        except Exception:
            self._zones = []
