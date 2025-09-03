import pygame
from roguelike_editors.common.ui.panels import draw_translucent_panel
from roguelike_editors.inventory.right_panel.item_selection_panel.item_selection_panel_model import ItemSelectionPanelModel
from roguelike_editors.inventory.right_panel.item_selection_panel.item_selection_panel_controller import ItemSelectionPanelController
from roguelike_editors.inventory.right_panel.item_selection_panel.item_selection_panel_view import ItemSelectionPanelView
import os
import logging
from roguelike_editors.inventory.editor_model import InventoryEditorModel
from roguelike_game.ecs.components.item_models import load_items
import logging
logger = logging.getLogger(__name__)
from roguelike_editors.inventory.left_panel.panel_model import InventoryPanelModel
from roguelike_editors.inventory.left_panel.panel_view import PanelView
from roguelike_editors.inventory.right_panel.inventory_items_panel.inventory_items_panel_view import InventoryItemsPanelView
# Title rendering is delegated to inventory_title controller

class InventoryEditorView:
    """
    Vista del editor de inventario (MVC): dibuja grid y botones.
    """
    def __init__(self, assets: dict, font: pygame.font.Font):
        self.assets = assets
        self.font = font
        self.slot_size = 50
        self.margin = 5
        self.grid_origin = (50, 50)
        self.button_size = (120, 30)
        self.tab_rects = []
        self.save_default_rect = None
        self.save_active_rect = None
        self.save_rect = None
        # Botones de mostrar datos (default/active)
        self.show_default_rect = None
        self.show_active_rect = None
        # Cargar íconos de ítems
        cwd = os.getcwd()
        items_path = os.path.join(cwd, 'data', 'items', 'items.json')
        self.items = load_items(items_path)
        self.images = {}

        # Paneles y botones para flujo Add Item
        self.logger = logging.getLogger(f"{__name__}.{self.__class__.__name__}")
        # Instanciar vista de grid de inventario
        # Panel MVC para item selection
        self.item_panel_model = ItemSelectionPanelModel()
        self.item_panel_controller = ItemSelectionPanelController(self.item_panel_model)
        self.item_panel_view = ItemSelectionPanelView(self.font, margin=self.margin, button_size=self.button_size)
        # Panel MVC para listado de entidades
        self.inventory_panel_model = InventoryPanelModel()
        self.inventory_panel_view = PanelView(self.font, margin=self.margin)

        # Instanciar vista de grid de inventario
        self.grid_view = InventoryItemsPanelView(
            font=self.font,
            slot_size=self.slot_size,
            margin=self.margin,
            button_size=self.button_size,
            get_item_image_func=self._get_item_image,
            images=self.images,
            logger=self.logger
        )
        # El título se renderiza vía self.title_controller (inyectado por el controller)
        # self.title_controller: InventoryTitleController
        
        # Preparar subcomponentes si es necesario

    def draw(self, screen, model: InventoryEditorModel, world):
        if not model.visible:
            return
        # Allow hiding the overlay while keeping event handling active (press-and-hold on Pos)
        if getattr(model, 'overlay_hidden_while_hold', False):
            return
        self._draw_ui(screen, model, world)
        return

    def _draw_ui(self, screen: pygame.Surface, model: InventoryEditorModel, world):
        ow, oh = screen.get_size()
        # 1) Título: responsabilidad del módulo inventory_title
        #    Renderiza y devuelve el recto exacto para alinear paneles debajo.
        title_rect = self.title_controller.render(screen)
        # 1.1) Tabs del panel izquierdo: ubicarlas justo bajo el título
        tabs_gap = 12
        tabs_x = 10
        tabs_y = title_rect.bottom + tabs_gap
        # Informar a TabsView su posición base configurable
        self.inventory_panel_view.tabs_view.set_base_pos(tabs_x, tabs_y)
        # Configurar pestañas secundarias (Show Default/Active) en el panel izquierdo
        show_side = model.current_category in ('player', 'monsters', 'hostile')
        self.inventory_panel_view.tabs_view.set_side_tabs(model.editing_side, show_side)
        # Altura exacta de tabs (coherente con TabsView: h + padding//2, padding=10)
        tab_sample_surf = self.inventory_panel_view.tabs_view.font.render("Player", True, (255, 255, 255))
        tab_text_h = tab_sample_surf.get_height()
        tabs_h = tab_text_h + 5
        # 1.2) Paneles comienzan debajo de los tabs (agregar gap adicional)
        content_top = tabs_y + tabs_h + 12

        # 2) Panel izquierdo (delegado a InventoryPanelView)
        panel_x = 10
        panel_y = content_top
        cols = 5
        grid_w = self.slot_size * cols + self.margin * (cols - 1)
        panel_w = ow - grid_w - panel_x - 10
        panel_h = oh - panel_y - 10
        panel_rect = pygame.Rect(panel_x, panel_y, panel_w, panel_h)
        # 2.1) Fondo translúcido detrás de la barra de tabs (ancho del panel izquierdo)
        tabs_bg_rect = pygame.Rect(panel_x, tabs_y - 6, panel_w, tabs_h + 12)
        draw_translucent_panel(
            screen,
            tabs_bg_rect,
            bg_rgba=(24, 26, 32, 170),
            border_rgba=(255, 255, 255, 30),
            radius=8,
            shadow=True,
        )
        # Alinear pestañas secundarias al borde derecho del panel izquierdo
        self.inventory_panel_view.tabs_view.set_right_edge(panel_rect.right)
        # Guardar rectángulo del panel izquierdo para eventos de grid
        self.left_panel_rect = panel_rect
        # Sincronizar inventario activo del Player desde ECS para reflejar cambios en vivo
        if model.editing_side == 'active' and model.current_category == 'player':
            self._sync_active_player_from_ecs(model, world)
        # Obtener lista de elementos para panel
        items = self.inventory_panel_controller.get_items_list()
        # Dibujar panel mediante MVC sobre la pantalla directamente
        # IMPORTANTE: pasar el InventoryEditorModel completo para que la ListView
        # pueda acceder a editing_side y selected_default_player_class, etc.
        panel_rects = self.inventory_panel_view.draw(screen, model, panel_rect, items)
        # Guardar rectángulos de pestañas y panel para eventos
        self.tab_rects = panel_rects.get('tab_rects')

        # 3) Panel derecho: grid + flujo Add Item
        if model.current_category in ('player', 'monsters', 'hostile'):
            # 3.0) Fondo translúcido del panel derecho (ocupa grid + item selection panel)
            right_x = panel_rect.x + panel_rect.width + self.margin
            right_y = content_top
            right_w = grid_w
            right_h = oh - content_top - 10
            right_bg_rect = pygame.Rect(right_x, right_y, right_w, right_h)
            draw_translucent_panel(
                screen,
                right_bg_rect,
                bg_rgba=(24, 26, 32, 170),
                border_rgba=(255, 255, 255, 30),
                radius=8,
                shadow=True,
            )
            self._draw_grid(screen, model, panel_rect)
            # Item selection panel: debajo del botón Save del grid
            grid_origin_x = panel_rect.x + panel_rect.width + self.margin
            save_bottom = self.save_rect.bottom if self.save_rect else panel_rect.y
            base_rect = pygame.Rect(grid_origin_x, save_bottom, grid_w, 0)
            rects = self.item_panel_view.draw(screen, self.item_panel_model, base_rect)
            # Propagar rects a la vista para handlers
            pv = self.item_panel_view
            pv.panel_rect = rects.get('panel_rect')
            pv.header_rect = rects.get('header_rect')
            pv.input_rect = rects.get('input_rect')
            pv.add_button_rect = rects.get('add_button_rect')
            pv.tab_rects = rects.get('tab_rects')
            # Alias subview internals para handlers
            pv.text_input = pv.input_view.text_input
            pv.scroll_panel = pv.list_view.scroll_panel
            # Guardar rects para eventos
            self.item_list_panel_rect = rects.get('panel_rect')
            self.item_list_header_rect = rects.get('header_rect')
            # Exponer el scroll panel para el handler de Add Item
            # AddItemEventHandler espera self.view.item_list_scroll_panel para manejar scroll/selección
            self.item_list_scroll_panel = pv.scroll_panel
            self.add_to_inventory_button_rect = rects.get('add_button_rect')
        return

    def get_slot_at_pos(self, pos, count):
        x, y = pos
        origin_x, origin_y = self.grid_origin
        y0 = origin_y + 30
        cols = min(count, 10)
        logger.debug(f"[DEBUG] get_slot_at_pos: pos=({x},{y}), origin=({origin_x},{origin_y}), y0={y0}, cols={cols}")
        for idx in range(count):
            col = idx % cols
            row = idx // cols
            rx = origin_x + col * (self.slot_size + self.margin)
            ry = y0 + row * (self.slot_size + self.margin)
            rect = pygame.Rect(rx, ry, self.slot_size, self.slot_size)
            logger.debug(f"[DEBUG] Slot {idx}: rect=({rx},{ry},{self.slot_size},{self.slot_size})")
            if rect.collidepoint(x, y):
                logger.debug(f"[DEBUG] Found slot {idx} at position ({x},{y})")
                return idx
        logger.debug(f"[DEBUG] No slot found at position ({x},{y})")
        return None

    def _sync_active_player_from_ecs(self, model: InventoryEditorModel, world):
        """Sincroniza model.active_data['player'] desde el InventoryComponent del ECS.
        Esto permite que los ítems nuevos obtenidos por el jugador se reflejen inmediatamente
        en el Editor (lista izquierda y grid derecho) cuando está en Show Active.
        """
        try:
            # Determinar el EID del jugador y resolverlo contra el mapa de componentes
            raw_eid = model.selected_eid if model.selected_eid is not None else getattr(world, 'player_entity', None)
            inv_map = getattr(world, 'components', {}).get('InventoryComponent', {}) or {}
            if raw_eid is None and not inv_map:
                return
            update_key = None
            inv_comp = None
            # Intentar con int/str según corresponda
            if raw_eid is not None:
                cand_keys = []
                if isinstance(raw_eid, int):
                    cand_keys.append(raw_eid)
                    cand_keys.append(str(raw_eid))
                elif isinstance(raw_eid, str):
                    cand_keys.append(raw_eid)
                    if raw_eid.isdigit():
                        try:
                            cand_keys.append(int(raw_eid))
                        except Exception:
                            pass
                # Probar claves candidatas
                for k in cand_keys:
                    if k in inv_map:
                        inv_comp = inv_map[k]
                        update_key = str(k if not isinstance(k, str) or k.isdigit() else k)
                        break
            # Fallback: world.player_entity
            if inv_comp is None:
                peid = getattr(world, 'player_entity', None)
                if peid in inv_map:
                    inv_comp = inv_map[peid]
                    update_key = str(peid)
            # Fallback: si hay un único inventario en el mapa, usarlo
            if inv_comp is None and isinstance(inv_map, dict) and len(inv_map) == 1:
                only_key = next(iter(inv_map.keys()))
                inv_comp = inv_map[only_key]
                update_key = str(only_key)
            if inv_comp is None:
                return
            # Construir representación tipo JSON de los slots
            slots_json = []
            for s in getattr(inv_comp, 'slots', []) or []:
                if not s:
                    slots_json.append(None)
                else:
                    item_id = getattr(s, 'item_id', None) or getattr(s, 'item', None)
                    qty = getattr(s, 'quantity', 0)
                    slots_json.append({'item': item_id, 'quantity': qty})
            # Volcar en active_data del modelo con compatibilidad legacy
            if not isinstance(model.active_data.get('player'), dict):
                model.active_data['player'] = {}
            pmap = model.active_data['player']
            target_key = update_key or str(raw_eid)
            if target_key in pmap:
                # Normal path: actualizar por clave encontrada
                entry = pmap.get(target_key, {})
                entry['slots'] = slots_json
                pmap[target_key] = entry
            else:
                # Legacy: si hay una única entrada y no coincide con eid, actualizar esa
                if isinstance(pmap, dict) and len(pmap) == 1:
                    only_key = next(iter(pmap.keys()))
                    entry = pmap.get(only_key, {}) or {}
                    entry['slots'] = slots_json
                    pmap[only_key] = entry
                else:
                    # No hay entradas o hay varias: crear/actualizar por la clave resuelta
                    pmap[target_key] = {'slots': slots_json}
        except Exception as e:
            # No interrumpir el render; solo loguear
            try:
                self.logger.warning(f"[InventoryEditorView] Player ECS sync failed: {e}")
            except Exception:
                pass

    def _get_item_image(self, item_id):
        if item_id in self.images:
            return self.images[item_id]
        model = self.items.get(item_id)
        if not model:
            return None
        icon = getattr(model, 'icon_small', None) or (model.icon[0] if isinstance(model.icon, list) else model.icon)
        if not icon:
            return None
        try:
            raw = pygame.image.load(os.path.join(os.getcwd(), icon)).convert_alpha()
            img = pygame.transform.scale(raw, (self.slot_size-10, self.slot_size-10))
            self.images[item_id] = img
            return img
        except Exception as e:
            self.logger.error(f"Error loading image for item '{item_id}': {e}")
            return None

    def _draw_grid(self, overlay, model, panel_rect):
        # Delegar renderizado del grid al InventoryGridView
        rects = self.grid_view.draw(overlay, model, panel_rect)
        # Asignar rects para manejo de eventos
        self.show_default_rect = rects.get('show_default')
        self.show_active_rect = rects.get('show_active')
        self.save_rect = rects.get('save')
        # Exponer rects de Add/Delete para manejo de eventos
        self.add_item_rect = rects.get('add_item')
        self.delete_item_rect = rects.get('delete_item')
        # Highlight Add Item button when panel open
        if self.item_panel_model.show_panel:
            pygame.draw.rect(overlay, (255,255,0), self.add_item_rect, 2)
        return
