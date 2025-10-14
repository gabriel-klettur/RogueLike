from .chat_ui.renderer import ChatUIRenderer
from .chat_ui import events as _chat_ui_events

class ChatUISystem:
    """
    Sistema de renderizado de la UI de chat.

    - Dibuja un panel en pantalla con historial y un campo de texto.
    - Registra un rectángulo bloqueador de inputs (ui_blocker) para evitar gameplay debajo.
    - Asegura que el controlador de entrada (ChatInputController) esté listo.
    """
    def __init__(self, perf_log=None):
        self.perf_log = perf_log
        # Delegamos el render a un renderer especializado para mejorar mantenibilidad.
        self._renderer = ChatUIRenderer(perf_log=perf_log)

    def update(self, world, screen, camera):
        # Render delegado completamente, incluyendo bloqueos de UI, tooltips,
        # scroll, resize, dropdown de idioma, typing e input.
        self._renderer.render(world, screen, camera)

# ===== Manejador de eventos de UI del chat (scroll, resize, scrollbar) =====
def handle_chat_ui_events(world, events):
    # Delegamos al nuevo módulo especializado para evitar duplicación.
    return _chat_ui_events.handle_chat_ui_events(world, events)
