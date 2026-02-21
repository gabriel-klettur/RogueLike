import pygame

class ColliderScopeTool:
    """
    Toggle de alcance de edición de colliders:
    - CG (Cambios Globales): pinta también en todos los buildings con el mismo image_path.
    - CU (Cambios Únicos): pinta sólo en el building actual.

    Almacenamos el estado en editor_state.collider_scope = 'CG' | 'CU'
    """
    def __init__(self, state, editor_state):
        self.state = state
        self.editor_state = editor_state
        # Valor por defecto si no existe
        if not hasattr(self.editor_state, 'collider_scope'):
            self.editor_state.collider_scope = 'CG'

    def current_scope(self) -> str:
        return getattr(self.editor_state, 'collider_scope', 'CG')

    def toggle_scope(self, building=None):
        """Alterna el alcance. Si se pasa building, se guarda por edificio y
        se refleja en editor_state como valor por defecto visual."""
        if building is not None:
            cur = getattr(building, 'collider_scope', getattr(self.editor_state, 'collider_scope', 'CG'))
            new_val = 'CU' if cur == 'CG' else 'CG'
            try:
                building.collider_scope = new_val
            except Exception:
                pass
            # Mantener un default global coherente con el último toggle
            self.editor_state.collider_scope = new_val
        else:
            cur = self.current_scope()
            self.editor_state.collider_scope = 'CU' if cur == 'CG' else 'CG'

    def get_handle_rect(self, building, camera) -> pygame.Rect:
        """Rectángulo del botón en la esquina inferior-derecha del building."""
        x, y = camera.apply((building.x, building.y))
        w, h = camera.scale(building.image.get_size())
        # Tamaño proporcional al ancho, coherente con otros handles
        size = max(15, min(65, int(w * 0.10)))
        return pygame.Rect(x + w - size, y + h - size, size, size)

    def handle_click(self, mouse_pos, building, camera) -> bool:
        mx, my = mouse_pos
        rect = self.get_handle_rect(building, camera)
        if rect and rect.collidepoint(mx, my):
            self.toggle_scope(building)
            return True
        return False
