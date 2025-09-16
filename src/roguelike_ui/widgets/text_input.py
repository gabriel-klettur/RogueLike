import pygame
from pygame.time import get_ticks

class TextInput:
    """
    Widget for inline text editing with blinking caret, cursor movement, and key repeat.
    """
    def __init__(self, font: pygame.font.Font, blink_interval: int = 500):
        self.font = font
        self.blink_interval = blink_interval
        self.text = ""
        self.cursor = 0
        self.active = False
        # wrapping cache (for multi-line draw)
        self._wrap_lines: list[dict] | None = None  # each: {'text': str, 'start': int, 'end': int}
        self._wrap_x: int = 0
        self._wrap_y: int = 0
        self._wrap_line_h: int = self.font.get_height()
        self._wrap_max_w: int = 0
        # selection and rendering state
        self.selection_start = 0
        self.selection_end = 0
        self.last_draw_x = 0
        self.last_draw_y = 0
        self.last_rect = pygame.Rect(0, 0, 0, 0)
        self.cursor = 0
        self.active = False

    def activate(self, initial_text: str = "", select_all: bool = False):
        self.text = initial_text
        # set cursor and selection
        self.cursor = len(initial_text)
        if select_all:
            self.selection_start = 0
            self.selection_end = self.cursor
        else:
            self.selection_start = self.cursor
            self.selection_end = self.cursor
        self.cursor = len(initial_text)
        self.active = True
        pygame.key.set_repeat(300, 50)

    def deactivate(self):
        self.active = False

    def handle_event(self, event: pygame.event.Event) -> bool:
        if not self.active:
            return False
        # Key events
        if event.type == pygame.KEYDOWN:
            mod = event.mod if hasattr(event, 'mod') else 0
            # Enter commits edit
            if event.key in (pygame.K_RETURN, pygame.K_KP_ENTER):
                self.active = False
                return True
            # Ctrl+A: select all
            if event.key == pygame.K_a and (mod & pygame.KMOD_CTRL):
                self.selection_start = 0
                self.selection_end = len(self.text)
                self.cursor = self.selection_end
                return True
            # Home
            if event.key == pygame.K_HOME:
                new_cursor = 0
                if mod & pygame.KMOD_SHIFT:
                    anchor = self.selection_start
                    self.selection_start = min(anchor, new_cursor)
                    self.selection_end = max(anchor, new_cursor)
                else:
                    self.selection_start = new_cursor
                    self.selection_end = new_cursor
                self.cursor = new_cursor
                return True
            # End
            if event.key == pygame.K_END:
                new_cursor = len(self.text)
                if mod & pygame.KMOD_SHIFT:
                    anchor = self.selection_start
                    self.selection_start = min(anchor, new_cursor)
                    self.selection_end = max(anchor, new_cursor)
                else:
                    self.selection_start = new_cursor
                    self.selection_end = new_cursor
                self.cursor = new_cursor
                return True
            # Left arrow
            if event.key == pygame.K_LEFT:
                new_cursor = max(0, self.cursor - 1)
                if mod & pygame.KMOD_SHIFT:
                    self.cursor = new_cursor
                    self.selection_end = self.cursor
                else:
                    self.cursor = new_cursor
                    self.selection_start = self.cursor
                    self.selection_end = self.cursor
                return True
            # Right arrow
            if event.key == pygame.K_RIGHT:
                new_cursor = min(len(self.text), self.cursor + 1)
                if mod & pygame.KMOD_SHIFT:
                    self.cursor = new_cursor
                    self.selection_end = self.cursor
                else:
                    self.cursor = new_cursor
                    self.selection_start = self.cursor
                    self.selection_end = self.cursor
                return True
            # Backspace
            if event.key == pygame.K_BACKSPACE:
                if self.selection_start != self.selection_end:
                    i0, i1 = sorted((self.selection_start, self.selection_end))
                    self.text = self.text[:i0] + self.text[i1:]
                    self.cursor = i0
                elif self.cursor > 0:
                    self.text = self.text[:self.cursor - 1] + self.text[self.cursor:]
                    self.cursor -= 1
                self.selection_start = self.cursor
                self.selection_end = self.cursor
                return True
            # Character insertion
            ch = event.unicode
            if ch:
                if self.selection_start != self.selection_end:
                    i0, i1 = sorted((self.selection_start, self.selection_end))
                    self.text = self.text[:i0] + ch + self.text[i1:]
                    self.cursor = i0 + len(ch)
                else:
                    i = self.cursor
                    self.text = self.text[:i] + ch + self.text[i:]
                    self.cursor += len(ch)
                self.selection_start = self.cursor
                self.selection_end = self.cursor
            return True
        # Mouse click: reposition caret (supports wrapped and single-line)
        if event.type == pygame.MOUSEBUTTONDOWN and getattr(event, 'button', None) == 1:
            mx, my = event.pos
            if hasattr(self, 'last_rect') and self.last_rect.collidepoint(mx, my):
                # If we have wrapping info from last draw_wrapped(), use it
                if self._wrap_lines and self._wrap_max_w > 0:
                    rel_x = mx - self._wrap_x
                    rel_y = my - self._wrap_y
                    line_h = self._wrap_line_h
                    line_idx = max(0, min(len(self._wrap_lines) - 1, rel_y // max(1, line_h)))
                    line = self._wrap_lines[int(line_idx)]
                    lx = max(0, int(rel_x))
                    # find nearest char within this line
                    best_i = line['start']
                    best_diff = abs(lx)
                    segment = line['text']
                    for i in range(1, len(segment) + 1):
                        pos = self.font.size(segment[:i])[0]
                        diff = abs(lx - pos)
                        if diff < best_diff:
                            best_diff = diff
                            best_i = line['start'] + i
                    self.cursor = max(line['start'], min(line['end'], best_i))
                else:
                    # single-line fallback
                    rel_x = mx - self.last_draw_x
                    best_i = 0
                    best_diff = abs(rel_x)
                    for i in range(1, len(self.text) + 1):
                        pos = self.font.size(self.text[:i])[0]
                        diff = abs(rel_x - pos)
                        if diff < best_diff:
                            best_diff = diff
                            best_i = i
                    self.cursor = best_i
                self.selection_start = self.cursor
                self.selection_end = self.cursor
                return True
        return False
        if not self.active:
            return False
        if event.type == pygame.KEYDOWN:
            if event.key in (pygame.K_RETURN, pygame.K_KP_ENTER):
                self.active = False
                return True
            if event.key == pygame.K_BACKSPACE:
                if self.cursor > 0:
                    self.text = self.text[:self.cursor-1] + self.text[self.cursor:]
                    self.cursor -= 1
                return True
            if event.key == pygame.K_LEFT:
                self.cursor = max(0, self.cursor-1)
                return True
            if event.key == pygame.K_RIGHT:
                self.cursor = min(len(self.text), self.cursor+1)
                return True
            # insert unicode char
            ch = event.unicode
            if ch:
                self.text = self.text[:self.cursor] + ch + self.text[self.cursor:]
                self.cursor += len(ch)
            return True
        return False

    def draw(self, surface: pygame.Surface, x: int, y: int, color=(255,255,255)):
        # store draw pos for mouse events
        self.last_draw_x = x
        self.last_draw_y = y
        text_w = self.font.size(self.text)[0]
        text_h = self.font.get_height()
        self.last_rect = pygame.Rect(x, y, text_w, text_h)
        # selection highlight
        if self.selection_start < self.selection_end:
            start_x = x + self.font.size(self.text[:self.selection_start])[0]
            sel_width = self.font.size(self.text[self.selection_start:self.selection_end])[0]
            sel_rect = pygame.Rect(start_x, y, sel_width, text_h)
            surface.fill((173, 216, 230), sel_rect)
        # render text
        txt_surf = self.font.render(self.text, True, color)
        surface.blit(txt_surf, (x, y))
        # blinking caret
        if self.active:
            t = get_ticks()
            if (t % self.blink_interval) < (self.blink_interval // 2):
                before = self.font.size(self.text[:self.cursor])[0]
                cx = x + before
                cy1 = y
                cy2 = y + text_h
                pygame.draw.line(surface, color, (cx, cy1), (cx, cy2), 1)
        # render text
        # draw selection highlight
        if self.selection_start < self.selection_end:
            start_x = x + self.font.size(self.text[:self.selection_start])[0]
            sel_width = self.font.size(self.text[self.selection_start:self.selection_end])[0]
            sel_rect = pygame.Rect(start_x, y, sel_width, self.font.get_height())
            surface.fill((173, 216, 230), sel_rect)
        # render text
        txt_surf = self.font.render(self.text, True, color)
        surface.blit(txt_surf, (x, y))
        # blinking caret
        if self.active:
            t = get_ticks()
            if (t % self.blink_interval) < (self.blink_interval // 2):
                before = self.font.size(self.text[:self.cursor])[0]
                cx = x + before
                cy1 = y
                cy2 = y + self.font.get_height()
                pygame.draw.line(surface, color, (cx, cy1), (cx, cy2), 1)

    def draw_wrapped(self, surface: pygame.Surface, x: int, y: int, max_width: int, color=(255,255,255), align_bottom: bool = True):
        """Renderiza el texto con word-wrap dentro de max_width.

        Si align_bottom es True, el bloque total se alinea desde abajo (última línea
        coincide verticalmente con y + font_height), ideal para paneles donde el input
        debe crecer hacia arriba.
        """
        # Build wrapped lines and index mapping
        words = list(self.text)
        # We'll wrap based on words split by spaces, but keep indices
        tokens: list[tuple[str, int, int]] = []  # (tok, start, end)
        i = 0
        buf = ''
        buf_start = 0
        while i < len(self.text):
            ch = self.text[i]
            if ch.isspace():
                if buf:
                    tokens.append((buf, buf_start, buf_start + len(buf)))
                    buf = ''
                tokens.append((ch, i, i + 1))
                i += 1
                buf_start = i
                continue
            if not buf:
                buf_start = i
            buf += ch
            i += 1
        if buf:
            tokens.append((buf, buf_start, buf_start + len(buf)))

        lines: list[dict] = []  # {'text': str, 'start': int, 'end': int}
        cur_text = ''
        cur_start = 0
        cur_end = 0
        first_token = True
        def flush_line():
            nonlocal cur_text, cur_start, cur_end
            if cur_text:
                lines.append({'text': cur_text, 'start': cur_start, 'end': cur_end})
                cur_text = ''
        for tok, s, e in tokens:
            add = tok if first_token else (tok)
            first_token = False if cur_text else True  # not used further
            proposal = (cur_text + tok)
            if self.font.size(proposal)[0] <= max_width or not cur_text:
                if not cur_text:
                    cur_start = s
                cur_text = proposal
                cur_end = e
            else:
                # wrap
                flush_line()
                cur_text = tok
                cur_start = s
                cur_end = e
        flush_line()
        if not lines:
            lines = [{'text': '', 'start': 0, 'end': 0}]

        line_h = self.font.get_linesize()
        total_h = line_h * len(lines)
        start_y = y
        if align_bottom:
            start_y = y - (total_h - self.font.get_height())

        # Save wrap info for mouse interactions
        self._wrap_lines = lines
        self._wrap_x = x
        self._wrap_y = start_y
        self._wrap_line_h = line_h
        self._wrap_max_w = max_width

        # Define last_rect covering entire area
        self.last_draw_x = x
        self.last_draw_y = start_y
        self.last_rect = pygame.Rect(x, start_y, max_width, total_h)

        # Selection highlight per line
        i0, i1 = sorted((self.selection_start, self.selection_end))
        for li, line in enumerate(lines):
            ly = start_y + li * line_h
            # selection range overlap in this line
            sel_s = max(line['start'], i0)
            sel_e = min(line['end'], i1)
            if sel_e > sel_s:
                pre = line['text'][:max(0, sel_s - line['start'])]
                mid = line['text'][max(0, sel_s - line['start']):max(0, sel_e - line['start'])]
                pre_w = self.font.size(pre)[0]
                mid_w = self.font.size(mid)[0]
                sel_rect = pygame.Rect(x + pre_w, ly, mid_w, self.font.get_height())
                surface.fill((173, 216, 230), sel_rect)

        # Draw text lines
        for li, line in enumerate(lines):
            ly = start_y + li * line_h
            txt_surf = self.font.render(line['text'], True, color)
            surface.blit(txt_surf, (x, ly))

        # Caret blinking
        if self.active:
            t = get_ticks()
            if (t % self.blink_interval) < (self.blink_interval // 2):
                # Find caret line
                caret_line_idx = 0
                for li, line in enumerate(lines):
                    if line['start'] <= self.cursor <= line['end']:
                        caret_line_idx = li
                        line_obj = line
                        break
                else:
                    caret_line_idx = len(lines) - 1
                    line_obj = lines[-1]
                within = max(0, self.cursor - line_obj['start'])
                cx_off = self.font.size(line_obj['text'][:within])[0]
                cx = x + cx_off
                cy1 = start_y + caret_line_idx * line_h
                cy2 = cy1 + self.font.get_height()
                pygame.draw.line(surface, color, (cx, cy1), (cx, cy2), 1)

    def measure_wrapped(self, max_width: int) -> tuple[int, int]:
        """Calcula número de líneas y altura total al envolver dentro de max_width.

        Devuelve (num_lineas, altura_total_en_px). También actualiza el caché
        de envoltura (_wrap_lines) para que los clics funcionen antes de dibujar.
        """
        # Tokenizar por espacios preservando índices
        tokens: list[tuple[str, int, int]] = []
        i = 0
        buf = ''
        buf_start = 0
        while i < len(self.text):
            ch = self.text[i]
            if ch.isspace():
                if buf:
                    tokens.append((buf, buf_start, buf_start + len(buf)))
                    buf = ''
                tokens.append((ch, i, i + 1))
                i += 1
                buf_start = i
                continue
            if not buf:
                buf_start = i
            buf += ch
            i += 1
        if buf:
            tokens.append((buf, buf_start, buf_start + len(buf)))

        lines: list[dict] = []
        cur_text = ''
        cur_start = 0
        cur_end = 0
        for tok, s, e in tokens:
            proposal = cur_text + tok
            if self.font.size(proposal)[0] <= max_width or not cur_text:
                if not cur_text:
                    cur_start = s
                cur_text = proposal
                cur_end = e
            else:
                lines.append({'text': cur_text, 'start': cur_start, 'end': cur_end})
                cur_text = tok
                cur_start = s
                cur_end = e
        if cur_text:
            lines.append({'text': cur_text, 'start': cur_start, 'end': cur_end})
        if not lines:
            lines = [{'text': '', 'start': 0, 'end': 0}]
        self._wrap_lines = lines
        self._wrap_max_w = max_width
        line_h = self.font.get_linesize()
        total_h = line_h * len(lines)
        return len(lines), total_h
