class GameState:
    def __init__(self):
        self.running = True
        self.mode = "local"

        # Player class selection state
        self.current_player_class = None

        # Editor states (models assigned during initialization)
        self.item_editor_state = None
        self.inventory_editor_state = None
        self.entities_editor_state = None
        self.spell_editor_state = None

        # Building editor state alias
        self.editor = None

        # --- Chat UI State ----------------------------------------------------
        # Flag principal: determina si el chat está visible/activo
        self.chat_open: bool = False
        # Entidad objetivo con la que se chatea (NPC u otro)
        self.chat_target_eid: int | None = None
        # Buffer de entrada de texto actual (lo que el usuario escribe)
        self.chat_input_buffer: str = ""
        # Historial de mensajes mostrados en la UI de chat
        # Cada entrada puede ser una tupla (sender:str, text:str)
        self.chat_messages: list[tuple[str, str]] = []
        # Límite de mensajes a mantener en historial
        self.chat_max_messages: int = 100
        # Rectángulo de bloqueo de UI para suprimir inputs de gameplay
        # Puede ser un pygame.Rect o None; lo gestiona el ChatUISystem
        self.chat_block_rect = None
        # Rectángulo del botón de cerrar (X) del chat
        self.chat_close_rect = None

    # Helpers de chat mínimos (opcionales)
    def chat_add_message(self, sender: str, text: str) -> None:
        """Añade un mensaje al historial respetando el límite."""
        try:
            self.chat_messages.append((str(sender), str(text)))
            if len(self.chat_messages) > max(1, int(self.chat_max_messages)):
                overflow = len(self.chat_messages) - int(self.chat_max_messages)
                if overflow > 0:
                    del self.chat_messages[:overflow]
        except Exception:
            # No romper el juego por errores de log de chat
            pass

    def chat_reset(self) -> None:
        """Cierra el chat y limpia estado efímero, pero conserva historial si se desea."""
        self.chat_open = False
        self.chat_target_eid = None
        self.chat_input_buffer = ""
        self.chat_block_rect = None
        self.chat_close_rect = None

