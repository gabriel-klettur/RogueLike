from roguelike_editors.buildings.controller import (
    selection_service,
    drag_service,
    persistence_service,
)
from roguelike_editors.buildings.tools.resize_tool.resize_tool import ResizeTool
from roguelike_editors.buildings.tools.default_tool.default_tool import DefaultTool
from roguelike_editors.buildings.tools.z_tool.z_tool import ZTool
from roguelike_editors.buildings.tools.split_z_tool.split_tool import SplitTool
from roguelike_editors.buildings.tools.placer_tool.placer_tool import PlacerTool
from roguelike_editors.buildings.tools.delete_tool.delete_tool import DeleteTool
from roguelike_editors.buildings.tools.default_tool.default_tool_view import DefaultToolView
from roguelike_editors.buildings.tools.collider_scope_tool import ColliderScopeTool
from roguelike_engine.buildings.services.collisions import resample_collision_map
from roguelike_ui.ui_blocker import is_blocked

from roguelike_editors.buildings.utils.zone_helpers import assign_zone_and_relatives
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
                    idx = buildings.index(ab) if ab in buildings else -1
                    logger.info(f"🗑️ Eliminado edificio activo via handle rojo (idx={idx}). Apilado en undo_stack.")
                    self._delete_building(ab, buildings)
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
            if ab is not None:
                # Si el punto cae en ambos paneles (top y bottom), decidir por cercanía al centro del botón clicado
                try:
                    top_m, top_p = self.z_tool_top._get_button_rects(ab, camera)
                except Exception:
                    top_m, top_p = None, None
                try:
                    bot_m, bot_p = self.z_tool_bottom._get_button_rects(ab, camera)
                except Exception:
                    bot_m, bot_p = None, None

                hits = []  # (dist2, tag)
                for tag, r in (("top", top_m), ("top", top_p), ("bottom", bot_m), ("bottom", bot_p)):
                    try:
                        if r is not None and r.collidepoint(mx, my):
                            dx = (r.centerx - mx)
                            dy = (r.centery - my)
                            hits.append((dx * dx + dy * dy, tag))
                    except Exception:
                        continue

                if hits:
                    hits.sort(key=lambda t: t[0])
                    winner = hits[0][1]
                    if winner == "top":
                        if self.z_tool_top.handle_mouse_click((mx, my), [ab], camera):
                            return
                    else:
                        if self.z_tool_bottom.handle_mouse_click((mx, my), [ab], camera):
                            return
                else:
                    # Sin solape o fuera de botones: probar normalmente
                    if self.z_tool_top.handle_mouse_click((mx, my), [ab], camera):
                        return
                    if self.z_tool_bottom.handle_mouse_click((mx, my), [ab], camera):
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
            logger.info("✅ Split ratio fijado: %.2f", float(self.editor.selected_building.split_ratio))

        # Guarda el building para recalcularlo
        building = self.editor.selected_building
        was_resizing = bool(self.editor.resizing)

        # 2) Reset de flags de arrastre
        self.editor.dragging = False
        self.editor.resizing = False
        self.editor.split_dragging = False

        # 2.5) Si veníamos redimensionando, aplicar remuestreo final de la grilla
        try:
            if was_resizing and (building is not None) and getattr(building, 'image', None) is not None:
                new_w, new_h = building.image.get_size()
                old_w, old_h = getattr(self.editor, 'initial_size', (new_w, new_h))
                cmap = getattr(building, 'collision_map', None)
                if isinstance(cmap, list) and cmap:
                    old_rows = len(cmap)
                    old_cols = len(cmap[0]) if old_rows > 0 else 0
                    if old_rows > 0 and old_cols > 0 and old_w > 0 and old_h > 0:
                        scale_y = new_h / float(old_h)
                        scale_x = new_w / float(old_w)
                        new_rows = max(1, int(round(old_rows * scale_y)))
                        new_cols = max(1, int(round(old_cols * scale_x)))
                        building.collision_map = resample_collision_map(cmap, new_rows, new_cols)
        except Exception:
            pass

        # 3) Si había un building arrastrado, le asignamos zona/relativos
        if building is not None:
            assign_zone_and_relatives(building)
            persistence_service.persist_spawner_drop_on_mouse_up(self.editor, building)

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
        hovered_list = selection_service.buildings_under_mouse(pos, camera, buildings)
        self.editor.hovered_buildings = hovered_list
        # Si el índice está fuera de rango, lo reiniciamos
        if self.editor.hovered_building_index >= len(hovered_list):
            self.editor.hovered_building_index = 0
        # hovered_building es el seleccionado por el índice
        if hovered_list:
            self.editor.hovered_building = hovered_list[self.editor.hovered_building_index]
        else:
            self.editor.hovered_building = None

    # ======================== CONFIRM DELETE ======================== #
    def _ask_confirm_delete(self, building) -> None:
        """Open a lightweight confirmation modal before deleting the active building.
        Shows how many Visuals references will be cleaned in cascade.
        """
        try:
            bid = getattr(building, 'id', None)
            if bid is None:
                return
            refs = persistence_service.count_spawner_refs(bid)
            self.editor.confirm_delete_visible = True
            try:
                self.editor.confirm_delete_target_id = int(bid)
            except Exception:
                self.editor.confirm_delete_target_id = bid
            self.editor.confirm_delete_refs_count = int(refs)
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
        """Internal delete used by tests; delegates to persistence service."""
        persistence_service.delete_building(self.editor, building, buildings)

    def _start_resize(self, building, mouse_start):
        drag_service.start_resize(self.editor, building, mouse_start)
        logger.info(f"🔧 Resize de {building.image_path} iniciado")

    def _start_drag(self, building, world_x, world_y):
        drag_service.start_drag(self.editor, building, world_x, world_y)
        logger.info(f"🏗️ Arrastre de {building.image_path} iniciado")
        assign_zone_and_relatives(self.editor.selected_building)
        persistence_service.snapshot_spawner_for_drag(self.editor, building)

    # ======================== ACTUALIZACIÓN ========================= #
    def update(self, camera):
        drag_service.update(self.editor, camera, self.resize_tool, self.split_tool)