"""
Controlador del panel de Tutorial (Buildings Editor).
"""
import logging
from .buildings_tutorial_panel_model import BuildingsTutorialPanelModel
from .buildings_tutorial_panel_view import BuildingsTutorialPanelView
from .buildings_tutorial_panel_events import BuildingsTutorialPanelEventHandler
from .services.checklist_service import BuildingsTutorialChecklistService
from .utils.logging_utils import short_stack as _short_stack_util
from .utils.editor_state_utils import (
    clear_hover_highlight as _clear_hover_util,
    clear_all_tutorial_pulses as _clear_pulses_util,
    reset_runtime_metrics as _reset_metrics_util,
    init_picker_tracking_from_state as _init_picker_tracking_util,
)

logger = logging.getLogger("buildings.tutorial")

class BuildingsTutorialPanelController:
    def __init__(self, state, editor_state, editor_view, editor_manager):
        self.state = state
        self.editor_state = editor_state
        self.editor_view = editor_view
        self.editor_manager = editor_manager

        self.model = BuildingsTutorialPanelModel()
        self.view = BuildingsTutorialPanelView(state, editor_state, self.model, editor_view)
        self.events = BuildingsTutorialPanelEventHandler(state, editor_state, self, self.model)

        # Inyección para alineación con la toolbar (lo completará el Manager)
        self.view.toolbar_view = None
        # Tracking del índice de paso para detectar cambios externos y limpiar hover
        self._last_step_index = None

    # Estado
    def is_active(self, _tool: str | None = None) -> bool:
        return bool(getattr(self.model, 'active', False))

    def activate(self) -> None:
        # Log activation intent with context
        try:
            logger.info(
                "[Tutorial] activate() called; colliders_mode=%s, picker_active=%s, step_index=%s\n%s",
                getattr(self.editor_state, 'colliders_mode', None),
                getattr(self.editor_state, 'picker_active', None),
                getattr(self.model, 'step_index', None),
                self._short_stack()
            )
        except Exception:
            pass
        self.model.active = True
        self.model.reset_runtime()
        # Comenzar desde el primer paso si deseamos
        if self.model.step_index < 0:
            self.model.step_index = 0
        # Sincronizar tracking de paso actual
        self._last_step_index = self.model.step_index
        # Resetear progreso de checklist y métricas runtime
        try:
            self.model.checklist_done_by_step.clear()
            _reset_metrics_util(self.model)
            _init_picker_tracking_util(self.model, self.editor_state)
            _clear_pulses_util(self.editor_state)
        except Exception:
            pass
        # Limpiar cualquier highlight por hover al iniciar el tutorial
        try:
            self._clear_hover_highlight()
        except Exception:
            pass

    def deactivate(self) -> None:
        # Log deactivation intent with context
        try:
            logger.info(
                "[Tutorial] deactivate() called; colliders_mode=%s, picker_active=%s, step_index=%s\n%s",
                getattr(self.editor_state, 'colliders_mode', None),
                getattr(self.editor_state, 'picker_active', None),
                getattr(self.model, 'step_index', None),
                self._short_stack()
            )
        except Exception:
            pass
        self.model.active = False
        self.model.reset_runtime()
        # Reset tracking
        self._last_step_index = None
        # Limpiar progreso/metricas para próxima sesión
        try:
            self.model.checklist_done_by_step.clear()
            _reset_metrics_util(self.model)
            _clear_pulses_util(self.editor_state)
        except Exception:
            pass
        # Limpiar cualquier highlight por hover al cerrar el tutorial
        try:
            self._clear_hover_highlight()
        except Exception:
            pass
        # Asegurar que la toolbar no quede marcando el botón activo tras cerrar por ESC/Cerrar
        try:
            tb_model = getattr(self.editor_manager, 'buildings_toolbar_model', None)
            if tb_model and getattr(tb_model, 'active_tool', None) == 'tutorial_building':
                tb_model.active_tool = None
        except Exception:
            pass

    def toggle(self) -> None:
        if self.is_active():
            self.deactivate()
        else:
            self.activate()

    def _short_stack(self, depth: int = 6) -> str:
        """Return a short formatted call stack (excluding this helper)."""
        try:
            return _short_stack_util(depth)
        except Exception:
            return "Call stack: <unavailable>"

    # Integración
    def handle_event(self, event) -> bool:
        return self.events.handle(event)

    def render(self, screen) -> None:
        if self.is_active():
            # Si el paso cambió por fuera de on_step_changed, limpiar hover y sincronizar
            try:
                cur_idx = int(getattr(self.model, 'step_index', 0) or 0)
            except Exception:
                cur_idx = 0
            if self._last_step_index is None or cur_idx != self._last_step_index:
                try:
                    self._clear_hover_highlight()
                except Exception:
                    pass
                self._last_step_index = cur_idx
            # Actualizar progreso del checklist antes de renderizar
            try:
                BuildingsTutorialChecklistService.update(self.model, self.editor_state, self.editor_view)
            except Exception:
                pass
            self.view.render(screen)

    # Llamado al cambiar de paso (Prev/Next)
    def on_step_changed(self, new_idx: int) -> None:
        try:
            # Limpiar progreso del paso destino para que el usuario lo rehaga
            self.model.checklist_done_by_step[new_idx] = set()
            # Resetear métricas runtime para evitar arrastre de estado entre pasos
            _reset_metrics_util(self.model)
            _clear_pulses_util(self.editor_state)
        except Exception:
            pass
        # Siempre limpiar resaltado por hover entre pasos para profesionalizar la UX
        try:
            self._clear_hover_highlight()
        except Exception:
            pass
        # Sincronizar tracking con el nuevo paso
        self._last_step_index = new_idx

    # --- Utilidades internas ---
    def _clear_hover_highlight(self) -> None:
        """Limpia el estado de hover y cualquier rect cacheado de hover en la vista.
        No toca la selección activa persistente (active_building)."""
        try:
            _clear_hover_util(self.editor_state, self.editor_view)
        except Exception:
            pass
    
    # --- Checklist ---
    def _update_checklist_progress(self) -> None:
        """Compatibilidad: mantiene la API anterior delegando al servicio."""
        BuildingsTutorialChecklistService.update(self.model, self.editor_state, self.editor_view)
