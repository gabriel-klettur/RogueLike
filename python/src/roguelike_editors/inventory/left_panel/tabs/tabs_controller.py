from roguelike_ui.services.json_persistence import load_from_json
import logging
logger = logging.getLogger(__name__)

class TabsController:
    """
    Controlador para el manejo de tabs/categorías del panel izquierdo.
    """
    
    def __init__(self, editor_controller, panel_model):
        self.editor_controller = editor_controller
        self.panel_model = panel_model
        # Debug flag: ensure prints only once per panel open
        self.debug_printed = False
    
    def change_category(self, category: str):
        """
        Cambiar categoría de listado.
        """
        # Cambiar categoría de listado
        self.panel_model.current_category = category
        # Al cambiar categoría, resetear selección
        self.panel_model.selected_eid = None
        self.editor_controller.model.current_category = category
        
        # Si cambiamos a 'monsters' o 'hostile', recargar datos activos desde JSON
        if category in ('monsters', 'hostile'):
            self._handle_monsters_category(category)
        elif category == 'player':
            self._handle_player_category()
    
    def _handle_monsters_category(self, category: str = 'monsters'):
        """
        Maneja la lógica específica para la categoría de monstruos.
        """
        active_path = self.editor_controller.data_controller.paths['monsters']['active']
        try:
            data = load_from_json(active_path)
            # Reflejar en ambas claves para transición 'hostile' <-> 'monsters'
            self.editor_controller.model.active_data['monsters'] = data
            self.editor_controller.model.active_data['hostile'] = data
        except Exception as e:
            logger.error("[TabsController] Error recargando inventory_monsters.json:", e)
        
        # Resetear debug para nuevas impresiones de diagnóstico
        # Delegar al list_controller que maneja el debug
        from ..list import ListController
        if hasattr(self.editor_controller, 'inventory_panel_controller'):
            if hasattr(self.editor_controller.inventory_panel_controller, 'list_controller'):
                self.editor_controller.inventory_panel_controller.list_controller.debug_printed = False
        self.editor_controller.model.editing_side = 'active'
        
        # Auto-seleccionar primer monstruo para mostrar sus items en el grid
        monsters_map = self.editor_controller.model.active_data.get(category, {})
        first_mon = next(iter(monsters_map.keys()), None)
        if first_mon:
            self._select_entity(first_mon)
    
    def _handle_player_category(self):
        """
        Maneja la lógica específica para la categoría de jugador.
        """
        self.editor_controller.model.editing_side = 'active'
        player_eid = getattr(self.editor_controller.world, 'player_entity', None)
        if player_eid is not None:
            self._select_entity(player_eid)
    
    def _select_entity(self, eid):
        """
        Seleccionar entidad (actualiza modelo de panel y modelo de editor).
        """
        self.panel_model.selected_eid = eid
        self.editor_controller.model.selected_eid = eid
