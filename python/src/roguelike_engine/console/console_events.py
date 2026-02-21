import pygame
from typing import Callable
from roguelike_engine.console.console_controller import ConsoleController


class ConsoleEvents:
    """
    Gestión de eventos de teclado para la consola.
    """
    def __init__(self, controller: ConsoleController):
        self.controller = controller
        # Tamaño de página para PageUp/PageDown (en líneas)
        self.page_lines = 10

    def process_event(self, event: pygame.event.Event) -> bool:
        """
        Procesa un evento de Pygame. Devuelve True si la consola manejó el evento.
        Política de captura:
        - Si la consola está abierta, se consumen TODOS los eventos de teclado (KEYDOWN/KEYUP/TEXTINPUT)
          para evitar que otras partes del juego reaccionen.
        - Cuando está cerrada, solo se intercepta la tecla de toggle y se deja pasar el resto.
        """
        # Toggle desde KEYDOWN incluso si está cerrada
        if event.type == pygame.KEYDOWN:
            # Aceptar varias teclas equivalentes según layout (US/ES)
            # Preferir scancode físico para independencia de layout
            uni = getattr(event, 'unicode', '') or ''
            key = event.key
            sc = getattr(event, 'scancode', 0)
            st = self.controller.state
            is_toggle_key = (
                sc == getattr(pygame, 'SCANCODE_GRAVE', -1)
                or key == pygame.K_BACKQUOTE
                or uni in ('`', '~', '´', '¨', 'º', 'ª')
                # Si está abierta, también aceptar la misma firma con la que se abrió
                or (st.is_open and (
                    (st.toggle_scancode is not None and sc == st.toggle_scancode) or
                    (st.toggle_key is not None and key == st.toggle_key) or
                    (st.toggle_unicode is not None and uni == st.toggle_unicode)
                ))
            )
            if is_toggle_key:
                # Edge-triggered: evitar autorepeat; solo actuar en primera pulsación
                if not st.toggle_held:
                    # Si está cerrada y vamos a abrir, recordar la firma usada
                    if not st.is_open:
                        st.toggle_scancode = sc
                        st.toggle_key = key
                        st.toggle_unicode = uni if uni else None
                    st.toggle_held = True
                    self.controller.toggle()
                return True

        # Si la consola NO está abierta, aún podemos aceptar TEXTINPUT para layouts que no
        # emiten unicode en KEYDOWN para la tecla de acento (ES):
        if not self.controller.state.is_open:
            if event.type == pygame.TEXTINPUT:
                txt = getattr(event, 'text', '') or ''
                if txt in ('`', '~', '´', '¨', 'º', 'ª'):
                    # Recordar firma por unicode cuando abrimos por TEXTINPUT
                    st = self.controller.state
                    st.toggle_scancode = None
                    st.toggle_key = None
                    st.toggle_unicode = txt
                    self.controller.toggle()
                    return True
            # Escape cuando está cerrada no se consume, para que lo maneje el juego/menú
            return False

        # A partir de aquí, la consola está ABIERTA y debemos consumir teclado
        # Consumir TEXTINPUT primero (Unicode / pegado). No cerrar aquí para evitar
        # doble toggle en teclas que emiten texto además de KEYDOWN.
        if event.type == pygame.TEXTINPUT:
            txt = getattr(event, 'text', '') or ''
            st = self.controller.state
            # Si el texto corresponde a la tecla de toggle, consumir SIN escribir
            if txt in ('`', '~', '´', '¨', 'º', 'ª') or (st.toggle_unicode and txt == st.toggle_unicode):
                return True
            self.controller.add_text(txt)
            return True

        # Manejar texto en composición (dead keys envían TEXTEDITING en algunos layouts)
        if getattr(pygame, 'TEXTEDITING', None) is not None and event.type == pygame.TEXTEDITING:
            # Consumir para evitar que gameplay reaccione durante composición, sin toggle
            return True

        # Consumir KEYUP y liberar la tecla de toggle para permitir siguiente flanco
        if event.type == pygame.KEYUP:
            key = event.key
            sc = getattr(event, 'scancode', 0)
            st = self.controller.state
            if (
                sc == getattr(pygame, 'SCANCODE_GRAVE', -1)
                or key == pygame.K_BACKQUOTE
                or (st.toggle_scancode is not None and sc == st.toggle_scancode)
                or (st.toggle_key is not None and key == st.toggle_key)
            ):
                st.toggle_held = False
            return True

        if event.type != pygame.KEYDOWN:
            # Otros tipos de evento no teclado: no se consumen aquí
            return False

        key = event.key
        mods = getattr(event, 'mod', 0)

        # Escape cierra la consola (y consume)
        if key == pygame.K_ESCAPE:
            self.controller.toggle()
            return True

        # Enter: ejecutar comando
        if key == pygame.K_RETURN:
            self.controller.submit()
            return True
        # Autocomplete
        if key == pygame.K_TAB:
            self.controller.autocomplete()
            return True
        # Historial
        if key == pygame.K_UP:
            self.controller.navigate_history(up=True)
            return True
        if key == pygame.K_DOWN:
            self.controller.navigate_history(up=False)
            return True
        # PageUp/PageDown: scroll del historial visual
        if key == pygame.K_PAGEUP:
            self.controller.scroll_history(self.page_lines)
            return True
        if key == pygame.K_PAGEDOWN:
            self.controller.scroll_history(-self.page_lines)
            return True
        # Edición: Backspace y Delete (con o sin Ctrl)
        if key == pygame.K_BACKSPACE:
            if mods & pygame.KMOD_CTRL:
                self.controller.backspace_word()
            else:
                self.controller.backspace()
            return True
        if key == pygame.K_DELETE:
            if mods & pygame.KMOD_CTRL:
                self.controller.delete_word_forward()
            else:
                self.controller.delete_forward()
            return True
        # Movimiento del cursor
        if key == pygame.K_LEFT:
            self.controller.move_left()
            return True
        if key == pygame.K_RIGHT:
            self.controller.move_right()
            return True
        if key == pygame.K_HOME:
            self.controller.move_home()
            return True
        if key == pygame.K_END:
            self.controller.move_end()
            return True
        # Nota: No añadimos caracteres desde KEYDOWN.unicode cuando TEXTINPUT está activo
        # para evitar duplicados (TEXTINPUT ya entrega el texto). Si se detectan entornos
        # donde TEXTINPUT no llega, esta ruta podría reactivarse con una bandera.

        # Consola abierta: consumir cualquier otra tecla para que el juego no reaccione
        return True
