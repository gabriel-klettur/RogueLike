"""
Vista del panel de Tutorial (Buildings Editor).
"""
import pygame
from roguelike_editors.common.ui.panels import draw_translucent_panel
from roguelike_ui.ui_blocker import register_blocker


class BuildingsTutorialPanelView:
    def __init__(self, state, editor_state, model, editor_view):
        self.state = state
        self.editor = editor_state
        self.model = model
        self.editor_view = editor_view
        # Para alinear relativo al toolbar, se inyecta desde el manager
        self.toolbar_view = None

        # Config visual
        self.width = 520  # ancho base; la altura será dinámica
        self.min_height = 200
        self.padding = 14
        self.spacing = 8
        self.title_color = (255, 255, 0)
        self.text_color = (230, 230, 230)
        self.btn_bg = (60, 60, 60)
        self.btn_fg = (255, 255, 255)
        self.btn_hover = (90, 90, 90)
        self.btn_disabled_bg = (45, 45, 45)
        self.btn_disabled_fg = (150, 150, 150)

        # Fuentes (fallbacks seguros)
        try:
            self.title_font = pygame.font.SysFont('arial', 22, bold=True)
            self.text_font = pygame.font.SysFont('arial', 18)
            self.button_font = pygame.font.SysFont('arial', 18, bold=True)
        except Exception:
            self.title_font = pygame.font.Font(None, 22)
            self.text_font = pygame.font.Font(None, 18)
            self.button_font = pygame.font.Font(None, 18)

    def _compute_position(self, screen: pygame.Surface, required_h: int) -> tuple[int, int]:
        """
        Calcula la posición del panel del tutorial teniendo en cuenta otros paneles.
        Reglas:
        - Si colliders_mode está activo, anclar a la DERECHA de la pantalla para no obstruir el centro.
        - Si el picker de Buildings está visible, colocar a la DERECHA del picker sin solaparse. Si no hay espacio, colocar DEBAJO del picker.
        - En caso contrario, anclar a la derecha del toolbar, bajo el título.
        Siempre respetar márgenes de pantalla y mantener el panel completamente visible.
        """
        margin = 16
        sw, sh = screen.get_size()
        # Utilidad: rect del título si existe
        def get_title_rect():
            try:
                r = getattr(self.editor_view, '_last_title_rect', None)
                if r is None and hasattr(self.editor_view, 'title_view'):
                    title_widget = getattr(self.editor_view.title_view, 'widget', None)
                    if title_widget is not None and hasattr(title_widget, 'rect'):
                        r = title_widget.rect
                return r
            except Exception:
                return None

        title_rect = get_title_rect()

        # 1) Si colliders está activo, acoplar a la derecha de la pantalla
        try:
            colliders_mode = bool(getattr(self.editor, 'colliders_mode', False))
        except Exception:
            colliders_mode = False
        if colliders_mode:
            x = max(margin, sw - self.width - margin)
            # Alinear verticalmente cerca del título si es posible
            y = (title_rect.bottom + 8) if title_rect is not None else margin
            # Ajustes para que no se salga por abajo
            y = max(margin, min(y, max(margin, sh - required_h - margin)))
            return (x, y)

        # 2) Si el picker de Buildings está abierto, colocar a su derecha
        try:
            picker_active = bool(getattr(self.editor, 'picker_active', False))
            picker_rect = getattr(self.editor, 'picker_panel_rect', None)
        except Exception:
            picker_active = False
            picker_rect = None
        if picker_active and isinstance(picker_rect, pygame.Rect):
            # Preferencia: a la derecha del picker
            pref_x = picker_rect.right + 12
            x = min(max(margin, pref_x), max(margin, sw - self.width - margin))
            # Alinear top con el picker, respetando límites verticales
            y = max(margin, picker_rect.top)
            if y + required_h > sh - margin:
                y = max(margin, sh - required_h - margin)
            # Verificar solapamiento con el picker; si aún solapa (no hay ancho suficiente), colocar debajo
            tutorial_rect = pygame.Rect(x, y, self.width, required_h)
            if tutorial_rect.colliderect(picker_rect):
                # Fallback: debajo del picker, intentando mantener mismo x del picker si cabe
                y2 = picker_rect.bottom + 12
                y2 = min(max(margin, y2), max(margin, sh - required_h - margin))
                x2 = min(max(margin, picker_rect.left), max(margin, sw - self.width - margin))
                return (x2, y2)
            return (x, y)

        # 3) Por defecto: anclar a la derecha del toolbar, debajo del título
        if title_rect is not None and self.toolbar_view is not None and hasattr(self.toolbar_view, 'widget'):
            try:
                tb_w = self.toolbar_view.widget.panel.surface.get_width()
                x = min(max(margin, title_rect.left + tb_w + 8), max(margin, sw - self.width - margin))
                y = (title_rect.bottom + 8) if title_rect is not None else margin
                y = max(margin, min(y, max(margin, sh - required_h - margin)))
                return (x, y)
            except Exception:
                pass
        # Fallback: margen superior izquierdo con límites
        x = max(margin, min(sw - self.width - margin, margin))
        y = max(margin, min(sh - required_h - margin, 96))
        return (x, y)

    def _wrap_text(self, text: str, font: pygame.font.Font, max_width: int) -> list[str]:
        words = text.split(' ')
        lines = []
        current = ''
        for w in words:
            test = (current + ' ' + w).strip()
            if font.size(test)[0] <= max_width:
                current = test
            else:
                if current:
                    lines.append(current)
                current = w
        if current:
            lines.append(current)
        return lines

    def _measure_required_height(self, step: dict) -> int:
        """Calcula la altura necesaria del panel en función del contenido."""
        # Título
        title_surf = self.title_font.render(step.get('title', ''), True, self.title_color)
        ty = self.padding + title_surf.get_height() + self.spacing
        # Texto envuelto
        text_max_w = self.width - 2 * self.padding
        wrapped = self._wrap_text(step.get('text', ''), self.text_font, text_max_w)
        for line in wrapped:
            line_h = self.text_font.size(line)[1]
            ty += line_h + 2
        # Checklist
        checklist = step.get('checklist', []) or []
        if checklist:
            ty += self.spacing
            box_size = 16
            for item in checklist:
                label = item.get('label', '')
                text_h = self.text_font.size(label)[1]
                ty += max(box_size, text_h) + 6
        # Botones (debajo) + padding inferior
        btn_h = 32
        ty += self.padding + btn_h
        # Margen inferior
        ty += self.padding
        return max(int(ty), self.min_height)

    def render(self, screen: pygame.Surface) -> None:
        if not getattr(self.model, 'active', False):
            return
        # Contenido del paso actual
        idx = int(getattr(self.model, 'step_index', 0) or 0)
        steps = getattr(self.model, 'steps', [])
        idx = max(0, min(idx, max(0, len(steps) - 1)))
        total = max(0, len(steps))
        is_last = (total > 0 and idx == total - 1)
        is_first = (idx == 0)
        step = steps[idx] if steps else {"title": "", "text": ""}
        # Progreso del checklist para condicionar highlights
        try:
            done_set = self.model.checklist_done_by_step.get(idx, set())
        except Exception:
            done_set = set()

        # Medir altura requerida antes de calcular la posición final (evita solapamientos)
        required_h = self._measure_required_height(step)
        x, y = self._compute_position(screen, required_h)
        panel_rect = pygame.Rect(x, y, self.width, required_h)
        draw_translucent_panel(screen, panel_rect)
        self.model.panel_rect = panel_rect
        # Registrar UI blocker para evitar interacciones con el mundo/editor debajo
        try:
            register_blocker(panel_rect)
        except Exception:
            pass

        # Título
        title_surf = self.title_font.render(step.get('title', ''), True, self.title_color)
        screen.blit(title_surf, (x + self.padding, y + self.padding))
        # Indicador de progreso x/y (arriba derecha)
        try:
            if total > 0:
                prog = f"{idx+1}/{total}"
                prog_surf = self.text_font.render(prog, True, (220, 220, 220))
                px = x + self.width - self.padding - prog_surf.get_width()
                py = y + self.padding + max(0, (title_surf.get_height() - prog_surf.get_height()) // 2)
                screen.blit(prog_surf, (px, py))
        except Exception:
            pass

        # Texto envuelto
        text_max_w = self.width - 2 * self.padding
        wrapped = self._wrap_text(step.get('text', ''), self.text_font, text_max_w)
        ty = y + self.padding + title_surf.get_height() + self.spacing
        for line in wrapped:
            line_surf = self.text_font.render(line, True, self.text_color)
            screen.blit(line_surf, (x + self.padding, ty))
            ty += line_surf.get_height() + 2

        # Checklist (si existe)
        checklist = step.get('checklist', []) or []
        if checklist:
            ty += self.spacing
            box_size = 16
            for item in checklist:
                label = item.get('label', '')
                iid = item.get('id')
                done = (iid in done_set)
                # Caja
                box_rect = pygame.Rect(x + self.padding, ty, box_size, box_size)
                pygame.draw.rect(screen, (200, 200, 200), box_rect, 2, border_radius=3)
                if done:
                    # Relleno y check
                    inner = box_rect.inflate(-4, -4)
                    pygame.draw.rect(screen, (60, 180, 75), inner, border_radius=3)
                    # Marca sencilla
                    pygame.draw.line(screen, (255, 255, 255), (box_rect.left + 3, box_rect.centery), (box_rect.centerx - 1, box_rect.bottom - 3), 3)
                    pygame.draw.line(screen, (255, 255, 255), (box_rect.centerx - 1, box_rect.bottom - 3), (box_rect.right - 3, box_rect.top + 3), 3)
                # Texto
                text_surf = self.text_font.render(label, True, (220, 220, 220))
                screen.blit(text_surf, (box_rect.right + 8, box_rect.top - 1))
                ty += max(box_size, text_surf.get_height()) + 6

        # Botones: Prev, Next, Close (abajo derecha)
        btn_w, btn_h = 90, 32
        gap = 10
        close_rect = pygame.Rect(x + self.width - self.padding - btn_w, y + panel_rect.height - self.padding - btn_h, btn_w, btn_h)
        next_rect = pygame.Rect(close_rect.left - gap - btn_w, close_rect.top, btn_w, btn_h)
        prev_rect = pygame.Rect(next_rect.left - gap - btn_w, close_rect.top, btn_w, btn_h)

        # Guardar rects para hit testing
        self.model.button_rects = {
            'prev': prev_rect,
            'next': next_rect,
            'close': close_rect,
        }

        # Dibujar botones
        mouse = pygame.mouse.get_pos()
        for key, rect in [('prev', prev_rect), ('next', next_rect), ('close', close_rect)]:
            # Estilos y etiquetas
            disabled = (key == 'next' and is_last) or (key == 'prev' and is_first)
            hovered = (rect.collidepoint(mouse) and not disabled)
            bg = self.btn_hover if hovered else (self.btn_disabled_bg if disabled else self.btn_bg)
            fg = self.btn_disabled_fg if disabled else self.btn_fg
            pygame.draw.rect(screen, bg, rect, border_radius=6)
            label = 'Anterior' if key == 'prev' else ('Siguiente' if key == 'next' else 'Cerrar')
            text_surf = self.button_font.render(label, True, fg)
            tx = rect.centerx - text_surf.get_width() // 2
            ty = rect.centery - text_surf.get_height() // 2
            screen.blit(text_surf, (tx, ty))

        # Highlight/flash del objetivo del paso actual (toolbar o edificio)
        try:
            highlights = step.get('highlight', {"kind": "none"})
            # Permitir uno o varios descriptores
            if isinstance(highlights, dict):
                highlights = [highlights]
            if isinstance(highlights, list):
                for hl in highlights:
                    if not isinstance(hl, dict):
                        continue
                    # Condiciones opcionales: ocultar si ciertos items ya están hechos,
                    # o depender de que ciertos items estén hechos.
                    hide_if = set(hl.get('hide_if_done') or [])
                    deps = set(hl.get('depends_on_done') or [])
                    if hide_if and (hide_if & done_set):
                        continue
                    if deps and not deps.issubset(done_set):
                        continue
                    if hl.get('kind') == 'toolbar' and self.toolbar_view is not None and hasattr(self.toolbar_view, 'widget'):
                        icon_rects = getattr(self.toolbar_view.widget, 'icon_rects', {}) or {}
                        r = icon_rects.get(hl.get('item'))
                        if r:
                            self._draw_flash_highlight(screen, r.inflate(8, 8))
                    elif hl.get('kind') == 'editor_building':
                        which = hl.get('which', 'hovered_or_active')
                        r = None
                        if which == 'hovered_or_active':
                            # Preferir SIEMPRE el edificio seleccionado (activo) para el parpadeo.
                            # Solo si no hay activo, usar el hovered como fallback.
                            r = getattr(self.editor_view, '_last_active_building_rect', None) or getattr(self.editor_view, '_last_hovered_building_rect', None)
                        elif which == 'active':
                            r = getattr(self.editor_view, '_last_active_building_rect', None)
                        elif which == 'hovered':
                            r = getattr(self.editor_view, '_last_hovered_building_rect', None)
                        if r:
                            self._draw_flash_highlight(screen, r.inflate(10, 10))
                    elif hl.get('kind') == 'tool_ui':
                        item = hl.get('item')
                        mapping = {
                            'split_handle': getattr(self.editor_view, '_last_split_handle_rect', None),
                            'z_bottom_minus': getattr(self.editor_view, '_last_z_bottom_minus_rect', None),
                            'z_bottom_plus': getattr(self.editor_view, '_last_z_bottom_plus_rect', None),
                            'z_top_minus': getattr(self.editor_view, '_last_z_top_minus_rect', None),
                            'z_top_plus': getattr(self.editor_view, '_last_z_top_plus_rect', None),
                        }
                        r = mapping.get(item)
                        if r:
                            self._draw_flash_highlight(screen, r.inflate(8, 8))
        except Exception:
            pass

    def _draw_flash_highlight(self, screen: pygame.Surface, rect: pygame.Rect) -> None:
        # Efecto parpadeante simple: alterna cada ~350ms
        try:
            ticks = pygame.time.get_ticks()
        except Exception:
            ticks = 0
        flash_on = ((ticks // 350) % 2) == 0
        # Halo translúcido
        color = (255, 215, 0)  # dorado
        if flash_on:
            s = pygame.Surface((rect.w, rect.h), pygame.SRCALPHA)
            s.fill((255, 215, 0, 60))
            screen.blit(s, rect.topleft)
        # Borde grueso
        pygame.draw.rect(screen, color, rect, 4)
        # Borde interno para más visibilidad
        pygame.draw.rect(screen, (255, 255, 255), rect.inflate(-6, -6), 2)
