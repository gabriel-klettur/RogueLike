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
        # Historiales por NPC/entidad. Clave: eid (int). Valor: lista de (sender, text)
        # Mantener un historial independiente por cada NPC evita mezclar conversaciones.
        self.chat_histories: dict[int, list[tuple[str, str]]] = {}
        # Compat: referencia al historial activo (se re-vincula con chat_bind_target)
        self.chat_messages: list[tuple[str, str]] = []
        # Límite de mensajes a mantener en historial
        self.chat_max_messages: int = 100
        # Rectángulo de bloqueo de UI para suprimir inputs de gameplay
        # Puede ser un pygame.Rect o None; lo gestiona el ChatUISystem
        self.chat_block_rect = None
        # Rectángulo del botón de cerrar (X) del chat
        self.chat_close_rect = None
        # Indicador de escritura (IA "pensando")
        self.chat_typing: bool = False
        self.chat_typing_phase: int = 0  # 0..2 para '.', '..', '...'
        self.chat_typing_last_ms: int | None = None

    # Helpers de chat mínimos (opcionales)
    def chat_add_message(self, sender: str, text: str) -> None:
        """Añade un mensaje al historial del target actual respetando el límite.

        Conserva compatibilidad con código existente que no especifica target.
        """
        try:
            # Vincular a un historial válido (target actual o buffer global -1)
            target = self.chat_target_eid if self.chat_target_eid is not None else -1
            hist = self.chat_histories.setdefault(int(target), [])
            # Asegurar que chat_messages referencia el historial activo
            if self.chat_target_eid is not None:
                self.chat_messages = hist
            hist.append((str(sender), str(text)))
            # Enforzar límite por-historial
            max_msgs = max(1, int(self.chat_max_messages))
            if len(hist) > max_msgs:
                overflow = len(hist) - max_msgs
                if overflow > 0:
                    del hist[:overflow]
        except Exception:
            # No romper el juego por errores de log de chat
            pass

    def chat_add_message_for(self, target_eid: int | None, sender: str, text: str) -> int:
        """Añade un mensaje para un target específico y devuelve el índice dentro de ese historial.

        Devuelve -1 si no se pudo calcular el índice (en caso de error).
        """
        try:
            key = int(target_eid) if target_eid is not None else -1
            hist = self.chat_histories.setdefault(key, [])
            hist.append((str(sender), str(text)))
            max_msgs = max(1, int(self.chat_max_messages))
            if len(hist) > max_msgs:
                overflow = len(hist) - max_msgs
                if overflow > 0:
                    del hist[:overflow]
            # Mantener referencia activa al cambiar de target
            if self.chat_target_eid == target_eid:
                self.chat_messages = hist
            return len(hist) - 1
        except Exception:
            return -1

    def chat_history_for(self, target_eid: int | None) -> list[tuple[str, str]]:
        """Obtiene el historial para un target, sin crear si no existe."""
        try:
            key = int(target_eid) if target_eid is not None else -1
            return self.chat_histories.get(key, [])
        except Exception:
            return self.chat_messages

    def chat_bind_target(self, target_eid: int | None) -> None:
        """Vincula el historial activo (`chat_messages`) al target especificado.

        Si no existe historial para el target, lo crea vacío.
        """
        try:
            self.chat_target_eid = target_eid
            key = int(target_eid) if target_eid is not None else -1
            self.chat_messages = self.chat_histories.setdefault(key, [])
        except Exception:
            # Ante error, mantener referencias previas
            pass

    def chat_reset(self) -> None:
        """Cierra el chat y limpia estado efímero, pero conserva historial si se desea."""
        self.chat_open = False
        # No borrar historiales; sólo desvincular target actual
        self.chat_target_eid = None
        self.chat_input_buffer = ""
        self.chat_block_rect = None
        self.chat_close_rect = None
        self.chat_typing = False
        self.chat_typing_phase = 0
        self.chat_typing_last_ms = None

