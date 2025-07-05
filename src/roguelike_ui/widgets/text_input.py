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
                    self.selection_end = new_cursor
                else:
                    self.selection_start = new_cursor
                    self.selection_end = new_cursor
                self.cursor = new_cursor
                return True
            # End
            if event.key == pygame.K_END:
                new_cursor = len(self.text)
                if mod & pygame.KMOD_SHIFT:
                    self.selection_end = new_cursor
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
        # Mouse click: reposition caret
        if event.type == pygame.MOUSEBUTTONDOWN and getattr(event, 'button', None) == 1:
            mx, my = event.pos
            if hasattr(self, 'last_rect') and self.last_rect.collidepoint(mx, my):
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
