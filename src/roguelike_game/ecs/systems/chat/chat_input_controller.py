import pygame
from roguelike_ui.widgets.text_input import TextInput
from roguelike_game.ecs.systems.chat.chat_bubble_utils import push_bubble

class ChatInputController:
    """
    Controlador de entrada de chat basado en TextInput.

    Mantiene un buffer editable y una cola de commits cuando el usuario pulsa Enter.
    Se almacena como `world._chat_input_ctrl` para acceso desde eventos y sistemas.
    """
    def __init__(self):
        # Fuente por defecto
        self.font = pygame.font.SysFont("Consolas", 16)
        self.text = TextInput(self.font)
        self._commits: list[str] = []
        self._active_cached = False

    def ensure_open(self, world):
        """Activa el widget con el buffer actual del estado si no está activo."""
        state = getattr(world, 'state', None)
        if state is None:
            return
        if not self.text.active:
            self.text.activate(initial_text=state.chat_input_buffer or "", select_all=False)
            self._active_cached = True

    def handle_events(self, world, events):
        """Procesa eventos de teclado y mouse relevantes al chat. Devuelve True si consumió alguno."""
        if not getattr(world, 'state', None) or not world.state.chat_open:
            return False
        consumed_any = False
        for ev in events:
            # Cerrar con clic en la 'X' del panel
            if ev.type == pygame.MOUSEBUTTONDOWN and getattr(ev, 'button', None) == 1:
                try:
                    close_rect = getattr(world.state, 'chat_close_rect', None)
                    if close_rect is not None:
                        mx, my = ev.pos
                        if close_rect.collidepoint(mx, my):
                            try:
                                self.text.deactivate()
                            except Exception:
                                pass
                            world.state.chat_reset()
                            consumed_any = True
                            # No seguir procesando el evento en el TextInput
                            continue
                except Exception:
                    pass
            # Cerrar con ESC
            if ev.type == pygame.KEYDOWN and ev.key == pygame.K_ESCAPE:
                try:
                    self.text.deactivate()
                except Exception:
                    pass
                world.state.chat_reset()
                consumed_any = True
                continue
            # Enviar al TextInput
            handled = self.text.handle_event(ev)
            if handled:
                consumed_any = True
                # Commit si se pulsó Enter (el TextInput se desactiva en ese caso)
                if ev.type == pygame.KEYDOWN and ev.key in (pygame.K_RETURN, pygame.K_KP_ENTER):
                    # Guardar commit y reflejar en GameState
                    msg = self.text.text.strip()
                    if msg:
                        self._commits.append(msg)
                        try:
                            world.state.chat_add_message('Tú', msg)
                        except Exception:
                            pass
                        # Burbuja flotante sobre el jugador
                        try:
                            player_eid = getattr(world, 'player_entity', None)
                            if player_eid is not None:
                                push_bubble(world, player_eid, msg, color=(220, 255, 220), ttl_ms=2800)
                        except Exception:
                            pass
                        # Reset buffer y re-activar para siguiente línea
                        self.text.activate(initial_text="", select_all=False)
                        world.state.chat_input_buffer = ""
                    else:
                        # Si no hay texto, cerrar el chat al pulsar Enter
                        try:
                            self.text.deactivate()
                        except Exception:
                            pass
                        world.state.chat_reset()
            # Evitar que la rueda del ratón haga scroll del mapa cuando está sobre el campo
            if ev.type == pygame.MOUSEWHEEL and getattr(self.text, 'last_rect', None):
                mx, my = pygame.mouse.get_pos()
                if self.text.last_rect.collidepoint(mx, my):
                    consumed_any = True
        # Mantener GameState.buffer sincronizado con TextInput
        try:
            if world.state.chat_open:
                world.state.chat_input_buffer = self.text.text
        except Exception:
            pass
        return consumed_any

    def get_commits(self) -> list[str]:
        commits = self._commits
        self._commits = []
        return commits

    def draw_input(self, surface: pygame.Surface, x: int, y: int, color=(255,255,255)):
        self.text.draw(surface, x, y, color=color)
