import pygame
from roguelike_ui.ui_blocker import register_blocker
from .chat_input_controller import ChatInputController

import logging
logger = logging.getLogger(__name__)

class ChatUISystem:
    """
    Sistema de renderizado de la UI de chat.

    - Dibuja un panel en pantalla con historial y un campo de texto.
    - Registra un rectángulo bloqueador de inputs (ui_blocker) para evitar gameplay debajo.
    - Asegura que el controlador de entrada (ChatInputController) esté listo.
    """
    def __init__(self, perf_log=None):
        self.perf_log = perf_log
        self._font = None
        self._small = None

    def _get_fonts(self):
        if self._font is None:
            self._font = pygame.font.SysFont("Consolas", 16)
        if self._small is None:
            self._small = pygame.font.SysFont("Consolas", 14)
        return self._font, self._small

    def update(self, world, screen, camera):
        state = getattr(world, 'state', None)
        if not state or not getattr(state, 'chat_open', False):
            return
        font, small = self._get_fonts()
        sw, sh = screen.get_size()
        # Panel en parte baja de la pantalla
        pad = 8
        panel_w = min(520, sw - pad * 2)
        panel_h = min(220, sh - pad * 2)
        panel_x = pad
        panel_y = sh - panel_h - pad
        panel_rect = pygame.Rect(panel_x, panel_y, panel_w, panel_h)
        # Fondo semitransparente
        bg = pygame.Surface((panel_w, panel_h), flags=pygame.SRCALPHA)
        bg.fill((10, 10, 10, 200))
        screen.blit(bg, (panel_x, panel_y))
        pygame.draw.rect(screen, (200, 200, 200), panel_rect, width=2)
        # Registrar bloqueo de UI
        try:
            register_blocker(panel_rect)
            state.chat_block_rect = panel_rect
        except Exception:
            pass
        # Título (dinámico: mostrar nombre del vendor si aplica)
        title_text = "Chat"
        try:
            target_eid = getattr(state, 'chat_target_eid', None)
            if target_eid is not None:
                chat_comp = world.components.get('ChatComponent', {}).get(target_eid)
                role = getattr(chat_comp, 'role', 'generic') if chat_comp else 'generic'
                if role == 'vendor':
                    ident = world.components.get('Identity', {}).get(target_eid)
                    name = getattr(ident, 'name', None)
                    if name:
                        title_text = str(name)
        except Exception:
            pass
        title = small.render(title_text, True, (255,255,0))
        screen.blit(title, (panel_x + pad, panel_y + pad))
        # Botón de cierre 'X' en esquina superior derecha
        btn_size = small.get_height() + 6
        btn_x = panel_x + panel_w - pad - btn_size
        btn_y = panel_y + pad
        close_rect = pygame.Rect(btn_x, btn_y, btn_size, btn_size)
        # fondo del botón (ligera transparencia)
        btn_bg = pygame.Surface((btn_size, btn_size), flags=pygame.SRCALPHA)
        btn_bg.fill((40, 40, 40, 200))
        screen.blit(btn_bg, (btn_x, btn_y))
        pygame.draw.rect(screen, (220, 220, 220), close_rect, width=1)
        x_text = small.render("X", True, (230, 100, 100))
        # centrar el texto dentro del botón
        tx = btn_x + (btn_size - x_text.get_width()) // 2
        ty = btn_y + (btn_size - x_text.get_height()) // 2
        screen.blit(x_text, (tx, ty))
        # Guardar rect del botón en el estado para manejo de input
        try:
            state.chat_close_rect = close_rect
        except Exception:
            pass
        # Área de mensajes
        msg_area_x = panel_x + pad
        msg_area_y = panel_y + pad + title.get_height() + 4
        msg_area_w = panel_w - pad * 2
        msg_area_h = panel_h - (pad * 3 + title.get_height() + 28)
        # Calcular cuántas líneas caben
        line_height = small.get_linesize()
        max_lines = max(1, msg_area_h // line_height)
        messages = list(getattr(state, 'chat_messages', []))[-max_lines:]
        y = msg_area_y
        for sender, text in messages:
            txt = small.render(f"{sender}: {text}", True, (230,230,230))
            screen.blit(txt, (msg_area_x, y))
            y += line_height
        # Campo input
        input_y = panel_y + panel_h - pad - font.get_height()
        input_x = panel_x + pad
        # Dibuja prompt
        prompt = small.render(">", True, (0,255,0))
        screen.blit(prompt, (input_x, input_y))
        input_x2 = input_x + prompt.get_width() + 6
        # Asegurar controlador y dibujar
        ctrl = getattr(world, '_chat_input_ctrl', None)
        if ctrl is None:
            ctrl = ChatInputController()
            setattr(world, '_chat_input_ctrl', ctrl)
        ctrl.ensure_open(world)
        ctrl.draw_input(screen, input_x2, input_y, color=(255,255,255))
