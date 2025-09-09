from __future__ import annotations

import pygame
from dataclasses import dataclass, field
from typing import List, Tuple


DOTS = [".", "..", "..."]


@dataclass
class ChatOverlayState:
    visible: bool = False
    is_typing: bool = False
    typing_elapsed_ms: int = 0
    typing_phase: int = 0  # 0..len(DOTS)-1
    typing_interval_ms: int = 300
    messages: List[Tuple[str, str]] = field(default_factory=list)  # (role, text)
    input_buffer: str = ""


class ChatOverlayController:
    def __init__(self) -> None:
        self.state = ChatOverlayState()
        self.font = pygame.font.SysFont(None, 18)
        self.title_font = pygame.font.SysFont(None, 22, bold=True)

    # Visibility
    def show(self) -> None:
        self.state.visible = True

    def hide(self) -> None:
        self.state.visible = False
        self.set_typing(False)

    # Typing indicator
    def set_typing(self, value: bool) -> None:
        self.state.is_typing = value
        if not value:
            self.state.typing_elapsed_ms = 0
            self.state.typing_phase = 0

    def update(self, dt_ms: int) -> None:
        if self.state.is_typing:
            self.state.typing_elapsed_ms += dt_ms
            if self.state.typing_elapsed_ms >= self.state.typing_interval_ms:
                self.state.typing_elapsed_ms = 0
                self.state.typing_phase = (self.state.typing_phase + 1) % len(DOTS)

    # Messages
    def append_message(self, role: str, text: str) -> None:
        self.state.messages.append((role, text))

    def set_messages(self, messages: List[Tuple[str, str]]) -> None:
        self.state.messages = list(messages)

    # Drawing
    def draw(self, surface: pygame.Surface, rect: pygame.Rect) -> None:
        if not self.state.visible:
            return

        # Panel background
        pygame.draw.rect(surface, (0, 0, 0, 180), rect)
        pygame.draw.rect(surface, (200, 200, 200), rect, 1)

        padding = 10
        content_rect = pygame.Rect(rect.x + padding, rect.y + padding, rect.w - 2*padding, rect.h - 2*padding)

        # Title
        title = self.title_font.render("Chat", True, (255, 255, 0))
        surface.blit(title, (content_rect.x, content_rect.y))
        y0 = content_rect.y + title.get_height() + 6

        # Reserve input area height
        line_h = self.font.get_linesize()
        input_h = line_h + 10
        messages_bottom = content_rect.bottom - input_h

        # Build wrapped lines for recent messages
        max_w = content_rect.w
        render_ops = []  # list of (prefix_surface_or_None, line_surface, x_offset)
        # Limitar número de mensajes a procesar para rendimiento
        for role, text in self.state.messages[-50:]:
            prefix = f"{role}: "
            prefix_surf = self.font.render(prefix, True, (220, 220, 100))
            prefix_w = prefix_surf.get_width()
            # Primera línea tiene prefijo; siguientes se indentan
            first_width = max(0, max_w - prefix_w)
            wrapped = self._wrap_text(text or "", first_width)
            if not wrapped:
                wrapped = [""]
            # Primera línea
            line0_surf = self.font.render(wrapped[0], True, (230, 230, 230))
            render_ops.append((prefix_surf, line0_surf, 0))
            # Resto de líneas con indent visual (x desplazado por el prefijo)
            for seg in wrapped[1:]:
                seg_surf = self.font.render(seg, True, (230, 230, 230))
                render_ops.append((None, seg_surf, prefix_w))

        # Añadir indicador de escritura como última línea virtual
        if self.state.is_typing:
            dots = DOTS[self.state.typing_phase]
            typing_surf = self.font.render(dots, True, (200, 200, 200))
            render_ops.append((None, typing_surf, 0))

        # Determinar qué líneas caben: tomar desde el final hasta llenar altura disponible
        visible_ops = []
        used_h = 0
        for op in reversed(render_ops):
            h = max(line_h, op[1].get_height())
            if used_h + h > max(0, messages_bottom - y0):
                break
            visible_ops.append(op)
            used_h += h
        visible_ops.reverse()

        # Clip para que nada se dibuje fuera del rectángulo
        prev_clip = surface.get_clip()
        surface.set_clip(pygame.Rect(content_rect.x, y0, content_rect.w, max(0, messages_bottom - y0)))

        # Dibujar líneas visibles
        y = y0
        for pref_surf, line_surf, x_off in visible_ops:
            x = content_rect.x + x_off
            if pref_surf is not None:
                surface.blit(pref_surf, (content_rect.x, y))
            surface.blit(line_surf, (x, y))
            y += max(line_h, line_surf.get_height())

        # Restaurar clip
        surface.set_clip(prev_clip)

        # Input buffer (una sola línea, recortando por la derecha si es muy largo)
        ibuf_text = "> " + (self.state.input_buffer or "")
        # Recortar por la izquierda hasta que quepa
        while self.font.size(ibuf_text)[0] > max_w and len(ibuf_text) > 0:
            ibuf_text = ibuf_text[1:]
        ibuf = self.font.render(ibuf_text, True, (180, 255, 180))
        surface.blit(ibuf, (content_rect.x, content_rect.bottom - ibuf.get_height()))

    # --- helpers ---
    def _wrap_text(self, text: str, max_width: int) -> list[str]:
        """Word-wrap por ancho en píxeles usando la métrica de la fuente.

        - Respeta espacios y saltos por palabra.
        - Corta palabras muy largas si exceden el ancho disponible.
        """
        if max_width <= 0:
            return [text]
        words = (text or "").split()
        lines: list[str] = []
        cur = ""
        for w in words:
            add = (cur + (" " if cur else "") + w).strip()
            if self.font.size(add)[0] <= max_width:
                cur = add
                continue
            # Si la palabra sola no cabe, partirla
            if not cur:
                chunks = self._split_long_word(w, max_width)
                if chunks:
                    lines.extend(chunks[:-1])
                    cur = chunks[-1]
                else:
                    lines.append(w)
                    cur = ""
            else:
                lines.append(cur)
                # reintentar con w en nueva línea
                if self.font.size(w)[0] <= max_width:
                    cur = w
                else:
                    chunks = self._split_long_word(w, max_width)
                    if chunks:
                        lines.extend(chunks[:-1])
                        cur = chunks[-1]
                    else:
                        cur = w
        if cur:
            lines.append(cur)
        return lines

    def _split_long_word(self, word: str, max_width: int) -> list[str]:
        """Divide una palabra demasiado larga en segmentos que quepan en max_width."""
        if self.font.size(word)[0] <= max_width:
            return [word]
        parts: list[str] = []
        buf = ""
        for ch in word:
            trial = buf + ch
            if self.font.size(trial)[0] <= max_width:
                buf = trial
            else:
                if buf:
                    parts.append(buf)
                buf = ch
        if buf:
            parts.append(buf)
        return parts
