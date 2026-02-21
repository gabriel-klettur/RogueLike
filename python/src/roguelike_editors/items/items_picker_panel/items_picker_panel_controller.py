import pygame
# Initialize font module to ensure SysFont works in tests
pygame.font.init()
_orig_sysfont = getattr(pygame.font, "_original_SysFont", None) or pygame.font.SysFont
import logging
logger = logging.getLogger(__name__)

def _safe_sysfont(*args, **kwargs):
    pygame.font.init()
    return _orig_sysfont(*args, **kwargs)
if not getattr(pygame.font, "_is_patched_sysfont", False):
    try:
        pygame.font._original_SysFont = _orig_sysfont
    except Exception:
        pass
    pygame.font.SysFont = _safe_sysfont
    try:
        pygame.font._is_patched_sysfont = True
    except Exception:
        pass
from typing import Any, Dict, Optional, Callable
from roguelike_editors.items.items_picker_panel.items_picker_panel_model import ItemPickerPanelModel
from roguelike_editors.items.items_picker_panel.items_picker_panel_view import ItemPickerPanelView
from roguelike_ui.widgets.picker_panel import PickerPanel, PickerPanelState
from roguelike_editors.items.items_properties_panel.items_properties_panel_controller import (
    ItemsPropertiesPanelController,
)
from roguelike_editors.items.items_picker_panel.items_picker_panel_events import ItemPickerPanelEventHandler

class ItemPickerPanelController:
    """Controller para editor de ítems: maneja visibilidad y navegación."""
    def __init__(self, items: Dict[str, Any], assets: Dict[str, Any], font: pygame.font.Font):
        self.model = ItemPickerPanelModel(items=items, assets=assets)
        self.view = ItemPickerPanelView(assets, font)

        # Text input y double-click ahora son gestionados por el panel de propiedades
        # La UI de instancias del mapa y parámetros se delega al ItemsInstancesPanelController

        # --- Hooks for orchestration (set by ItemsEditorController) ---
        # on_select_id(id: str) -> None
        # on_open_id(id: str) -> None
        # on_spawn_at_player(item_id: str) -> None  # RMB spawn callback
        self.on_select_id: Optional[Callable[[str], None]] = None
        self.on_open_id: Optional[Callable[[str], None]] = None
        self.on_spawn_at_player: Optional[Callable[[str], None]] = None
        # Back-compat internal properties panel (used only if no orchestrator is attached)
        self._internal_properties = ItemsPropertiesPanelController(items, font)

        # Reusable Picker Panel setup
        # State rect will be positioned each frame by the view
        self.picker_state = PickerPanelState(rect=pygame.Rect(0, 0, 0, 0))
        self.picker = PickerPanel(
            cell_size=(64, 64),
            draw_panel_bg=False,
            grid_bg_color=None,
            allow_dragging=False,
            max_columns=12,
        )

        def _get_item_ids() -> list[str]:
            # Excluir placeholder de imagen faltante y mantener orden estable
            return [i for i in self.model.items.keys() if i != "image_item_not_found"]

        self._get_item_ids = _get_item_ids  # store for reuse in callbacks

        self.picker.set_item_count(lambda: len(self._get_item_ids()))

        def _draw_item(surface: pygame.Surface, rect: pygame.Rect, index: int, selected: bool, hovered: bool) -> None:
            # Fondo de celda y icono escalado
            pygame.draw.rect(surface, (50, 50, 50), rect)
            item_ids = self._get_item_ids()
            if 0 <= index < len(item_ids):
                item_id = item_ids[index]
                icon = self.view.assets.get(item_id)
                if icon:
                    icon_surf = pygame.transform.smoothscale(icon, (rect.w, rect.h))
                    surface.blit(icon_surf, rect.topleft)

        self.picker.set_draw_item(_draw_item)

        def _on_select(index: int) -> None:
            item_ids = self._get_item_ids()
            if 0 <= index < len(item_ids):
                self.model.selected_item_id = item_ids[index]
                # Notify orchestrator
                if self.on_select_id is not None:
                    try:
                        self.on_select_id(self.model.selected_item_id)
                    except Exception:
                        logger.exception("on_select_id callback failed")

        self.picker.on_select = _on_select
        # Abrir (doble clic) notifica al orquestador para iniciar edición inline en propiedades
        def _on_open(index: int) -> None:
            _on_select(index)
            if self.on_open_id is not None and self.model.selected_item_id:
                try:
                    self.on_open_id(self.model.selected_item_id)
                except Exception:
                    logger.exception("on_open_id callback failed")
            elif self._internal_properties and self.model.selected_item_id:
                # Back-compat path: open inline edit on internal panel
                self._internal_properties.update_context(self.model.items, self.model.selected_item_id, self.model.hovered_item_id)
                self._internal_properties.start_inline_edit()

        self.picker.on_open = _on_open

        # Expose picker to the view for rendering and layout
        self.view.picker = self.picker
        self.view.picker_state = self.picker_state
        # Handler de eventos del grid
        self.event_handler = ItemPickerPanelEventHandler(self)

    def handle_event(self, event: pygame.event.Event) -> None:
        # Añadir nuevo ítem al mapa con clic derecho SOLO si el clic ocurre dentro del grid del picker.
        # Evita que RMB sobre el mapa (usado para drag) dispare spawns accidentales cuando el picker está visible.
        if self.model.visible and event.type == pygame.MOUSEBUTTONDOWN and event.button == 3:
            try:
                rect = getattr(self.picker_state, 'rect', None)
                if rect and hasattr(event, 'pos') and rect.collidepoint(*event.pos):
                    if self.model.selected_item_id and self.on_spawn_at_player is not None:
                        try:
                            self.on_spawn_at_player(self.model.selected_item_id)
                        except Exception:
                            logger.exception("on_spawn_at_player callback failed")
                    return  # Consumir RMB sólo si se manejó dentro del picker
            except Exception:
                logger.exception("[ItemPickerPanelController] RMB spawn handling failed")

        # Si existe panel de propiedades interno y clic cae sobre él, manejar ahí y no seguir
        if self.model.visible and hasattr(event, 'pos') and self._internal_properties:
            panel_rect = getattr(self._internal_properties.model, 'panel_rect', None)
            if panel_rect and panel_rect.collidepoint(*event.pos):
                # Mantener contexto actualizado
                self._internal_properties.update_context(self.model.items, self.model.selected_item_id, self.model.hovered_item_id)
                self._internal_properties.handle_event(event)
                return

        # Delegar entrada del picker (navegación/selección)
        self.event_handler.handle(event)
        return
    def draw(self, screen: pygame.Surface) -> None:
        # Mostrar editor de ítems original
        if not self.model.visible:
            return
        self.view.draw(screen, self.model)
        # Dibujar panel de propiedades interno si no hay orquestador enganchado
        if self._internal_properties and not (self.on_select_id or self.on_open_id):
            self._internal_properties.update_context(self.model.items, self.model.selected_item_id, self.model.hovered_item_id)
            self._internal_properties.draw(screen, getattr(self.view, 'title_rect', None))
        # El panel inferior (lista de instancias y params) es dibujado por ItemsInstancesPanelController
