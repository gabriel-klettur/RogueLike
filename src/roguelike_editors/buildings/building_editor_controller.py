import pygame
from roguelike_editors.buildings.tools.resize_tool.resize_tool import ResizeTool
from roguelike_editors.buildings.tools.default_tool.default_tool import DefaultTool
from roguelike_editors.buildings.tools.z_tool.z_tool      import ZTool
from roguelike_editors.buildings.tools.split_z_tool.split_tool  import SplitTool
from roguelike_editors.buildings.tools.placer_tool.placer_tool  import PlacerTool
from roguelike_editors.buildings.tools.delete_tool.delete_tool  import DeleteTool
from roguelike_editors.buildings.tools.default_tool.default_tool_view import DefaultToolView
from roguelike_editors.buildings.tools.collider_scope_tool import ColliderScopeTool
from roguelike_ui.ui_blocker import is_blocked

from roguelike_editors.buildings.utils.zone_helpers import assign_zone_and_relatives
from roguelike_editors.spawner.services.persistence import find_instance_in_json, persist_drop
from roguelike_editors.spawner.services.persistence import remove_visual_refs_by_building_id
from roguelike_editors.spawner.services.persistence import load_instances_json
from roguelike_editors.spawner.spawner_instance_properties_panel.services.buildings_service import (
    load_buildings_instances as _svc_load_buildings_instances,
    write_buildings_instances as _svc_write_buildings_instances,
)
from roguelike_engine.config.map_config import global_map_settings
from roguelike_engine.buildings.building_model import BuildingModel

from roguelike_editors.buildings.buildings_picker.building_picker_controller import BuildingPickerController
import logging
logger = logging.getLogger(__name__)

class BuildingEditorController:
    """Agrupa todas las herramientas y ofrece una API de eventos de mouse."""

    def __init__(self, state, editor_state, buildings, camera):
        self.state = state
        self.editor = editor_state
        
        self.resize_tool = ResizeTool(state, editor_state)
        self.default_tool = DefaultTool(state, editor_state)        
        self.default_view = DefaultToolView(state, editor_state)
        self.split_tool = SplitTool(state, editor_state)
        self.z_tool_bottom = ZTool(state, editor_state, target="bottom")
        self.z_tool_top    = ZTool(state, editor_state, target="top")        
        # Toggle CG/CU de alcance de colliders
        self.collider_scope_tool = ColliderScopeTool(state, editor_state)
        # Elegir clase de building de forma segura aunque la lista esté vacía
        try:
            building_cls = type(buildings[0]) if buildings else BuildingModel
        except Exception:
            building_cls = BuildingModel
        if building_cls is BuildingModel and not buildings:
            logger.warning("BuildingEditorController: buildings list is empty; using BuildingModel as fallback for placer tool.")
        self.placer_tool = PlacerTool(
            state, editor_state,
            building_class=building_cls,
            default_image="assets/buildings/dummy.png",
            default_scale=(512, 824),
            default_solid=True,
        )
        self.delete_tool = DeleteTool(state, editor_state, camera)

        self.picker = BuildingPickerController(editor_state, self.placer_tool)

    # =========================== EVENTOS ============================ #
    def on_mouse_down(self, pos, button, camera, buildings):
        """button: 1 = izq, 3 = der"""
        mx, my = pos

        # Do not process building clicks when mouse is over any UI panel
        try:
            if is_blocked(mx, my):
                return
        except Exception:
            pass

        world_x = mx / camera.zoom + camera.offset_x
        world_y = my / camera.zoom + camera.offset_y

        # Si el panel de colisiones está activo, deshabilitar clics de herramientas
        # excepto el toggle de alcance CG/CU y permitir seleccionar el edificio activo con LMB.
        if getattr(self.editor, 'colliders_mode', False):
            if button == 1:
                ab = getattr(self.editor, 'active_building', None)
                if ab is not None:
                    scope_rect = self.collider_scope_tool.get_handle_rect(ab, camera)
                    if scope_rect and scope_rect.collidepoint(mx, my):
                        self.collider_scope_tool.toggle_scope(ab)
                        return
                # Permitir selección persistente del edificio bajo el cursor en modo colisiones
                for b in reversed(buildings):
                    if b.rect.collidepoint(world_x, world_y):
                        self.editor.active_building = b
                        return
            return

        # Modo eliminar: con LMB borra el edificio bajo el cursor inmediatamente
        if button == 1 and getattr(self.editor, 'remove_mode_active', False):
            for b in reversed(buildings):
                if b.rect.collidepoint(world_x, world_y):
                    self._delete_building(b, buildings)
                    return

        # 1) Barra split (clic izq o der indistinto) SOLO sobre activo
        ab = getattr(self.editor, 'active_building', None)
        if ab is not None:
            if self.split_tool.check_handle_click((mx, my), ab, camera):
                self.split_tool.start_drag(ab)
                return

        # 2) Alcance colliders CG/CU (clic izq, esquina inferior derecha)
        if button == 1:
            ab = getattr(self.editor, 'active_building', None)
            if ab is not None:
                scope_rect = self.collider_scope_tool.get_handle_rect(ab, camera)
                if scope_rect and scope_rect.collidepoint(mx, my):
                    self.collider_scope_tool.toggle_scope(ab)
                    return
            # Botón eliminar (clic izq)
            # Usar la vista para el botón rojo
            if hasattr(self, 'default_view'):
                get_rect = self.default_view.get_delete_handle_rect
            else:
                # fallback por si acaso
                get_rect = lambda b, c: None
            # SOLO sobre edificio activo
            if ab is not None:
                delete_rect = get_rect(ab, camera)
                if delete_rect and delete_rect.collidepoint(mx, my):
                    logger.info("🗑️ Click en botón eliminar (handle rojo) → abrir confirmación")
                    self._ask_confirm_delete(ab)
                    return
                # Detect reset handle (click izquierdo)
                reset_rect = self.default_view.get_reset_handle_rect(ab, camera)
                if reset_rect.collidepoint(mx, my):
                    self.default_tool.apply_reset(ab)
                    return
                # Detect resize handle (click izquierdo)
                if self.resize_tool.check_resize_handle_click(mx, my, ab, camera):
                    self._start_resize(ab, (mx, my))
                    return

        # 3) Drag de edificio (clic der) SOLO sobre activo
        if button == 3:
            ab = getattr(self.editor, 'active_building', None)
            if ab and ab.rect.collidepoint(world_x, world_y):
                self._start_drag(ab, world_x, world_y)
            return

        # 4) Paneles Z (+ / –) (clic izq)
        if button == 1:
            ab = getattr(self.editor, 'active_building', None)
            targets = [ab] if ab is not None else []
            if ab and self.z_tool_bottom.handle_mouse_click((mx, my), targets, camera):
                return
            if ab and self.z_tool_top.handle_mouse_click((mx, my), targets, camera):
                return
            # 5) Selección persistente con clic izquierdo (si no consumieron otros handles)
            for b in reversed(buildings):
                if b.rect.collidepoint(world_x, world_y):
                    self.editor.active_building = b
                    return

    def on_mouse_up(self, button, camera, buildings):
        # 1) Finalizar resize / split (igual que antes)
        if self.editor.resizing:
            logger.info("✅ Resize terminado.")
        if self.editor.split_dragging:
            logger.info("✅ Split ratio fijado:", round(self.editor.selected_building.split_ratio, 2))

        # Guarda el building para recalcularlo
        building = self.editor.selected_building
        was_resizing = bool(self.editor.resizing)

        # 2) Reset de flags de arrastre
        self.editor.dragging = False
        self.editor.resizing = False
        self.editor.split_dragging = False

        # 3) Si había un building arrastrado, le asignamos zona/relativos
        if building is not None:
            assign_zone_and_relatives(building)
            # Si está vinculado a un spawner, persistir el cambio a JSON
            try:
                eid = getattr(building, "_spawner_eid", None)
                world = getattr(building, "_world_ref", None)
                start_entry = getattr(self.editor, "_spawner_drag_start_entry", None)
                if eid is not None and world is not None:
                    # No persistimos tamaños de imagen legacy; overrides relevantes se manejan vía building_id
                    persist_drop(world, eid, start_entry, overrides_update=None)
            except Exception:
                pass

        # 4) Ya podemos limpiar la selección
        self.editor.selected_building = None

    def on_mouse_motion(self, pos, camera, buildings):
        # Si estamos arrastrando/redimensionando, solo actualiza
        if self.editor.dragging or self.editor.resizing or self.editor.split_dragging:
            self.update(camera)
            return
        # Bloquear hover si el ratón está sobre cualquier panel UI registrado
        try:
            mx, my = pos
            if is_blocked(mx, my):
                self.editor.hovered_buildings = []
                self.editor.hovered_building = None
                return
        except Exception:
            pass
        # Detectar todos los edificios bajo el mouse (orden arriba-abajo)
        hovered_list = self._buildings_under_mouse(pos, camera, buildings)
        self.editor.hovered_buildings = hovered_list
        # Si el índice está fuera de rango, lo reiniciamos
        if self.editor.hovered_building_index >= len(hovered_list):
            self.editor.hovered_building_index = 0
        # hovered_building es el seleccionado por el índice
        if hovered_list:
            self.editor.hovered_building = hovered_list[self.editor.hovered_building_index]
        else:
            self.editor.hovered_building = None

    def _buildings_under_mouse(self, mouse_pos, camera, buildings):
        mx, my = mouse_pos
        wx = mx / camera.zoom + camera.offset_x
        wy = my / camera.zoom + camera.offset_y
        result = []
        for b in reversed(buildings):  # Reversed para priorizar el más arriba
            x, y = b.x, b.y
            w, h = b.image.get_size()
            rect = pygame.Rect(x, y, w, h)
            if rect.collidepoint(wx, wy):
                result.append(b)
        return result

    # ======================== CONFIRM DELETE ======================== #
    def _count_spawner_refs(self, bid: int) -> int:
        """Count how many visuals across all spawner instances reference this building id."""
        try:
            bid = int(bid)
        except Exception:
            return 0
        try:
            inst = load_instances_json()
        except Exception:
            inst = []
        count = 0
        for it in (inst or []):
            try:
                vis = it.get('visuals')
                if not isinstance(vis, dict) or not vis:
                    continue
                for _k, _v in vis.items():
                    try:
                        if isinstance(_v, dict):
                            _vid = _v.get('instance_id') or _v.get('id') or _v.get('building_instance_id')
                            _vid = int(_vid) if _vid is not None else None
                        else:
                            _vid = int(_v)
                    except Exception:
                        _vid = None
                    if _vid is not None and int(_vid) == int(bid):
                        count += 1
            except Exception:
                continue
        return count

    def _ask_confirm_delete(self, building) -> None:
        """Open a lightweight confirmation modal before deleting the active building.
        Shows how many Visuals references will be limpiadas en cascada.
        """
        try:
            bid = getattr(building, 'id', None)
            if bid is None:
                return
            refs = self._count_spawner_refs(bid)
            self.editor.confirm_delete_visible = True
            try:
                self.editor.confirm_delete_target_id = int(bid)
            except Exception:
                self.editor.confirm_delete_target_id = bid
            self.editor.confirm_delete_refs_count = int(refs)
            # Texto en español, consistente con confirmaciones existentes
            if refs > 0:
                self.editor.confirm_delete_text = (
                    f"¿Eliminar edificio ID {bid}?\n"
                    f"Se limpiarán también {refs} referencia(s) en Visuals de Spawners.\n"
                    f"Esta acción no se puede deshacer."
                )
            else:
                self.editor.confirm_delete_text = (
                    f"¿Eliminar edificio ID {bid}?\n"
                    f"Esta acción no se puede deshacer."
                )
        except Exception:
            # Si falla, no bloquear la UI
            self.editor.confirm_delete_visible = False
            self.editor.confirm_delete_text = ""
            self.editor.confirm_delete_target_id = None
            self.editor.confirm_delete_refs_count = 0

    def confirm_delete_yes(self, buildings):
        """User confirmed: perform cascade delete now."""
        try:
            tid = getattr(self.editor, 'confirm_delete_target_id', None)
            target = None
            if tid is not None:
                for b in buildings:
                    try:
                        if str(getattr(b, 'id', None)) == str(tid):
                            target = b
                            break
                    except Exception:
                        continue
            if target is not None:
                self._delete_building(target, buildings)
        except Exception:
            pass
        finally:
            # Cerrar modal siempre
            self.confirm_delete_no()

    def confirm_delete_no(self) -> None:
        """Dismiss confirmation modal."""
        try:
            self.editor.confirm_delete_visible = False
            self.editor.confirm_delete_text = ""
            self.editor.confirm_delete_target_id = None
            self.editor.confirm_delete_refs_count = 0
            self.editor.confirm_yes_rect = None
            self.editor.confirm_no_rect = None
        except Exception:
            pass

    def toggle_editor(self):
        """Activa/desactiva los handles del Building Editor, sin tocar el picker."""
        new_val = not self.editor.active
        self.editor.active = new_val
        logger.info("🟩 Building Editor ON" if new_val else "🟥 Building Editor OFF")

    def toggle_picker(self):
        """Activa/desactiva solo el picker (listado de assets)."""
        new_val = not self.editor.picker_active
        self.editor.picker_active = new_val
        logger.info("📂 Building Picker ON" if new_val else "📂 Building Picker OFF")


    # ======================== LÓGICA PRIVADA ======================== #
    def _delete_building(self, building, buildings):
        logger.info(f"❌ Eliminando edificio: {building} en index {buildings.index(building)}")
        # Elimina el edificio y lo guarda para undo
        if not hasattr(self.editor, 'undo_stack'):
            self.editor.undo_stack = []
        idx = buildings.index(building)
        self.editor.undo_stack.append((building, idx))
        buildings.remove(building)
        # Limpia selección/hover si corresponde
        if self.editor.selected_building == building:
            self.editor.selected_building = None
        if self.editor.hovered_building == building:
            self.editor.hovered_building = None
        # Pulso para el tutorial (cubre botón eliminar y cualquier llamada centralizada)
        try:
            setattr(self.editor, 'tutorial_deleted_pulse', True)
        except Exception:
            pass
        # Persistencia cruzada: limpiar Visuals de spawners y eliminar de buildings_instances.json
        try:
            bid = getattr(building, 'id', None)
            if bid is not None:
                try:
                    bid_int = int(bid)
                except Exception:
                    bid_int = None
                if bid_int is not None:
                    # 1) Primero, limpiar referencias en spawners_instances.json (cascada inmediata)
                    try:
                        removed_refs = remove_visual_refs_by_building_id(int(bid_int))
                        if removed_refs:
                            logger.info(f"[BuildingsEditor] Cleared {removed_refs} visual refs in spawners for building id={bid_int}")
                    except Exception:
                        pass
                    # 2) Luego, eliminar entrada(s) en data/buildings/buildings_instances.json
                    try:
                        data = _svc_load_buildings_instances()
                        before = len(data or [])
                        kept = []
                        for e in (data or []):
                            try:
                                if int(e.get('id')) == int(bid_int):
                                    continue
                            except Exception:
                                pass
                            kept.append(e)
                        if len(kept) != before:
                            _svc_write_buildings_instances(kept)
                            logger.info(f"[BuildingsEditor] Removed building instance id={bid_int} from buildings_instances.json")
                        else:
                            # Si no hubo cambios aparentes, reintenta tras la limpieza de spawners
                            # (protege contra estados intermedios)
                            try:
                                data2 = _svc_load_buildings_instances()
                                kept2 = []
                                for e in (data2 or []):
                                    try:
                                        if int(e.get('id')) == int(bid_int):
                                            continue
                                    except Exception:
                                        pass
                                    kept2.append(e)
                                if len(kept2) != len(data2 or []):
                                    _svc_write_buildings_instances(kept2)
                                    logger.info(f"[BuildingsEditor] Forced removal retry for id={bid_int} (post spawners cleanup)")
                            except Exception:
                                pass
                    except Exception:
                        pass
                    # Verificación: asegurar que el id ya no exista en el JSON
                    try:
                        cur = _svc_load_buildings_instances() or []
                        left = [e for e in cur if str(e.get('id')) == str(bid_int)]
                        if left:
                            logger.warning(f"[BuildingsEditor] Warning: building id={bid_int} still present after delete attempts ({len(left)} left) → forcing final removal")
                            forced = [e for e in cur if str(e.get('id')) != str(bid_int)]
                            _svc_write_buildings_instances(forced)
                            logger.info(f"[BuildingsEditor] Forced removal succeeded for id={bid_int}")
                    except Exception:
                        pass
        except Exception:
            pass

    def _start_resize(self, building, mouse_start):
        self.editor.selected_building = building
        self.editor.resizing = True
        self.editor.resize_origin = mouse_start
        self.editor.initial_size = building.image.get_size()
        logger.info(f"🔧 Resize de {building.image_path} iniciado")

    def _start_drag(self, building, world_x, world_y):
        self.editor.selected_building = building
        self.editor.dragging = True
        self.editor.offset_x = world_x - building.x
        self.editor.offset_y = world_y - building.y
        logger.info(f"🏗️ Arrastre de {building.image_path} iniciado")
        assign_zone_and_relatives(self.editor.selected_building)
        # Si es un spawner, capturar snapshot para persistencia (zona/local_tile/original overrides/id)
        try:
            eid = getattr(building, "_spawner_eid", None)
            world = getattr(building, "_world_ref", None)
            if eid is not None and world is not None:
                comps = getattr(world, 'components', {})
                cfg = comps.get('SpawnerConfig', {}).get(eid)
                if cfg is not None:
                    zone = cfg.zone
                    off_x, off_y = global_map_settings.zone_offsets.get(zone, (0, 0))
                    tx, ty = cfg.anchor_tile
                    local_start = (int(tx - off_x), int(ty - off_y))
                    tpl_id = cfg.template_id
                    data, idx, overrides = find_instance_in_json(tpl_id, zone, local_start)
                    inst_id = None
                    try:
                        if idx is not None:
                            inst_id = data[idx].get('id')
                    except Exception:
                        inst_id = None
                    self.editor._spawner_drag_start_entry = {
                        'template_id': tpl_id,
                        'zone': zone,
                        'local_tile': local_start,
                        'overrides': overrides,
                        'index': idx,
                        'id': inst_id,
                    }
        except Exception:
            # No romper flujo de editor por snapshot fallida
            self.editor._spawner_drag_start_entry = None

    # ======================== ACTUALIZACIÓN ========================= #
    def update(self, camera):
        if self.editor.dragging and self.editor.selected_building:
            mx, my = pygame.mouse.get_pos()
            wx = mx / camera.zoom + camera.offset_x
            wy = my / camera.zoom + camera.offset_y

            b = self.editor.selected_building
            b.x = wx - self.editor.offset_x
            b.y = wy - self.editor.offset_y
            b.rect.topleft = (b.x, b.y)

        elif self.editor.resizing and self.editor.selected_building:
            self.resize_tool.update_resizing(pygame.mouse.get_pos())

        elif self.editor.split_dragging:
            self.split_tool.update_drag(pygame.mouse.get_pos(), camera)