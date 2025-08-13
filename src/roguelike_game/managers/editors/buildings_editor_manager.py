from roguelike_editors.buildings.building_editor_model import BuildingsEditorModel
from roguelike_editors.buildings.building_editor_controller import BuildingEditorController
from roguelike_editors.buildings.building_editor_events import BuildingEditorEventHandler
from roguelike_editors.buildings.building_editor_view import BuildingEditorView
from roguelike_editors.buildings.buildings_colliders_panel import BuildingCollidersPanelController
from roguelike_editors.buildings.buildings_add_remove_panel.buildings_add_remove_panel_controller import BuildingsAddRemovePanelController
from roguelike_editors.buildings.buildings_tool_bar_panel.buildings_tool_bar_panel_model import BuildingsToolBarPanelModel
from roguelike_editors.buildings.buildings_tool_bar_panel.buildings_tool_bar_panel_view import BuildingsToolBarPanelView
from roguelike_editors.buildings.buildings_tool_bar_panel.buildings_tool_bar_panel_events import BuildingsToolBarPanelEventHandler
from roguelike_editors.buildings.buildings_tool_bar_panel.buildings_tool_bar_panel_controller import BuildingsToolBarPanelController
from roguelike_engine.config.map_config import global_map_settings
from roguelike_ui.ui_blocker import clear_blockers

class BuildingEditorManager:
    def __init__(self, game):
        # guardamos referencia al Game completo
        self.game = game
        state = game.state
        # tomamos la lista de edificios para pasarla también al event handler
        buildings = game.buildings.buildings

        # Inicialización del editor de edificios
        self.editor_state = BuildingsEditorModel()
        self.controller   = BuildingEditorController(state, self.editor_state, buildings, game.camera)
        self.view         = BuildingEditorView(state, self.editor_state)
        # Panel especializado de colisiones (delegación completa del "collision brush")
        self.colliders    = BuildingCollidersPanelController(state, self.editor_state, self.view)
        # Panel de Add/Remove (abre picker y acciones rápidas)
        self.add_remove   = BuildingsAddRemovePanelController(state, self.editor_state, self.view, self)
        # Asegurar estado inicial: panel Add/Remove y Picker apagados al abrir el editor
        try:
            self.add_remove.deactivate()
        except Exception:
            pass
        try:
            self.editor_state.picker_active = False
            self.editor_state.add_remove_panel_rect = None
        except Exception:
            pass

        # Ahora el event handler recibe también la lista de buildings

        # pasamos también los offsets de cada zona        
        self.handler      = BuildingEditorEventHandler(
            state,
            self.editor_state,
            self.controller,
            buildings,
            zone_offsets= global_map_settings.zone_offsets
        )
        # Permitir al manejador de eventos delegar al panel de colisiones
        try:
            self.handler.colliders = self.colliders
        except Exception:
            pass
        # Delegación al panel de add/remove
        try:
            self.handler.add_remove = self.add_remove
        except Exception:
            pass

        # --- Buildings Toolbar Panel ---
        # Crear toolbar (modelo, vista, eventos, controlador) siguiendo patrón Items
        self.buildings_toolbar_model = BuildingsToolBarPanelModel()
        # Construir vista y events con controlador placeholder y reinyectar después (resuelve circularidad)
        tmp_view = BuildingsToolBarPanelView(None, self.buildings_toolbar_model)
        tmp_events = BuildingsToolBarPanelEventHandler(None, self.buildings_toolbar_model)
        self.buildings_toolbar_controller = BuildingsToolBarPanelController(
            self, self.buildings_toolbar_model, tmp_view, tmp_events
        )
        # Reinyectar referencias al controlador real en vista y eventos
        tmp_view.controller = self.buildings_toolbar_controller
        # Asegurar que el widget compartido ToolbarView también tenga el controlador correcto
        try:
            if hasattr(tmp_view, 'widget'):
                tmp_view.widget.controller = self.buildings_toolbar_controller
        except Exception:
            pass
        tmp_events.controller = self.buildings_toolbar_controller
        # Permitir que el panel Add/Remove se alinee a la derecha del toolbar
        try:
            if hasattr(self.add_remove, 'view'):
                self.add_remove.view.toolbar_view = tmp_view
        except Exception:
            pass
        # Permitir que el panel de Colliders se alinee con el botón 'buildings_colliders' del toolbar
        try:
            if hasattr(self.colliders, 'view'):
                self.colliders.view.toolbar_view = tmp_view
        except Exception:
            pass
        # Permitir al event handler del editor delegar a la toolbar
        try:
            self.handler.buildings_toolbar_controller = self.buildings_toolbar_controller
        except Exception:
            pass

        # exponemos el state para que el Game lo use
        state.editor = self.editor_state

    def handle(self, camera, entities, events=None):
        self.handler.handle(camera, entities, events)

    def update(self, camera):
        if self.editor_state.active:
            self.controller.update(camera)

    def render(self, screen, camera, buildings):
        if self.editor_state.active:
            # Nota: los UI blockers ya se limpian una vez por frame en RendererManager.
            # No limpiar aquí para no borrar los blockers registrados por otros editores (p.ej. Tiles Collision Panel).
            # 1) Render de la toolbar primero (posicionada bajo el título)
            try:
                self.buildings_toolbar_controller.render(screen)
            except Exception:
                pass
            # 2) Render del panel de Add/Remove (se alinea a la derecha de la toolbar)
            try:
                self.add_remove.render(screen)
            except Exception:
                pass
            # 3) Render principal del editor (incluye título y picker/overlays)
            #    El picker se alineará a la derecha del Add/Remove usando editor_state.add_remove_panel_rect
            self.view.render(screen, camera, buildings)
            # Render del panel de colisiones por encima (overlay/picker)
            try:
                self.colliders.render(screen, camera, buildings)
            except Exception:
                pass