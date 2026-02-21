import pygame
import logging
from roguelike_ui.widgets.text_input._caret import caret_on
from roguelike_ui.widgets.text_input._wrap_utils import tokenize, wrap_from_tokens
from roguelike_ui.widgets.text_input._events import handle_key as _handle_key, handle_mouse as _handle_mouse
from roguelike_ui.widgets.text_input._render_single import draw_singleline as _draw_singleline
from roguelike_ui.widgets.text_input._render_wrapped import draw_wrapped_block as _draw_wrapped_block

logger = logging.getLogger(__name__)

class TextInput:
    """
    Widget for inline text editing with blinking caret, cursor movement, and key repeat.
    """
    def __init__(self, font: pygame.font.Font, blink_interval: int = 500):
        # Provide a safe default font if None is passed in (e.g., tests)
        if font is None:
            try:
                if not pygame.get_init():
                    pygame.init()
            except Exception:
                pass
            try:
                if not pygame.font.get_init():
                    pygame.font.init()
            except Exception:
                pass
            try:
                font = pygame.font.Font(None, 16)
                logger.info("[TextInput] No font provided; using default pygame font (size=%d)", font.get_height())
            except Exception:
                # As a last resort, keep None; downstream will raise with clear error
                logger.error("[TextInput] Could not create default font; TextInput may fail without a valid font.")
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
        if event.type == pygame.KEYDOWN:
            return _handle_key(self, event)
        if event.type == pygame.MOUSEBUTTONDOWN:
            return _handle_mouse(self, event)
        return False

    def draw(self, surface: pygame.Surface, x: int, y: int, color=(255,255,255)):
        caret_visible = self.active and caret_on(self.blink_interval)
        self.last_rect = _draw_singleline(
            surface,
            font=self.font,
            text=self.text,
            x=x,
            y=y,
            color=color,
            selection_start=self.selection_start,
            selection_end=self.selection_end,
            cursor=self.cursor,
            caret_visible=caret_visible,
        )
        self.last_draw_x = x
        self.last_draw_y = y

    def draw_wrapped(self, surface: pygame.Surface, x: int, y: int, max_width: int, color=(255,255,255), align_bottom: bool = True):
        """Renderiza el texto con word-wrap dentro de max_width.

        Si align_bottom es True, el bloque total se alinea desde abajo (última línea
        coincide verticalmente con y + font_height), ideal para paneles donde el input
        debe crecer hacia arriba.
        """
        caret_visible = self.active and caret_on(self.blink_interval)
        last_rect, lines, start_y, line_h = _draw_wrapped_block(
            surface,
            font=self.font,
            text=self.text,
            x=x,
            y=y,
            max_width=max_width,
            color=color,
            align_bottom=align_bottom,
            selection_start=self.selection_start,
            selection_end=self.selection_end,
            cursor=self.cursor,
            caret_visible=caret_visible,
        )
        # Cache for interactions
        self._wrap_lines = lines
        self._wrap_x = x
        self._wrap_y = start_y
        self._wrap_line_h = line_h
        self._wrap_max_w = max_width
        self.last_draw_x = x
        self.last_draw_y = start_y
        self.last_rect = last_rect

    def measure_wrapped(self, max_width: int) -> tuple[int, int]:
        """Calcula número de líneas y altura total al envolver dentro de max_width.

        Devuelve (num_lineas, altura_total_en_px). También actualiza el caché
        de envoltura (_wrap_lines) para que los clics funcionen antes de dibujar.
        """
        tokens = tokenize(self.text)
        lines = wrap_from_tokens(self.font, tokens, max_width)
        self._wrap_lines = lines
        self._wrap_max_w = max_width
        line_h = self.font.get_linesize()
        total_h = line_h * len(lines)
        return len(lines), total_h
