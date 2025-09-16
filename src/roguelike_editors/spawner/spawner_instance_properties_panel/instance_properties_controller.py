from __future__ import annotations

from typing import Optional, List, Dict, Any, Tuple
import logging

from roguelike_ui.widgets.double_click_detector import DoubleClickDetector
from .instance_properties_model import InstancePropertiesModel
from .instance_properties_view import InstancePropertiesView
from .instance_properties_events import InstancePropertiesEventHandler
from .visuals.visuals_controller import VisualsController
from .visuals.visuals_picker import VisualsPicker

# Mixins (modularized functionality)
from .controller.logging_mixin import LoggingMixin
from .controller.selection_mixin import SelectionMixin
from .controller.render_mixin import RenderMixin
from .controller.visuals_picker_mixin import VisualsPickerMixin
from .controller.reload_mixin import ReloadMixin
from .controller.editor_visibility_mixin import EditorVisibilityMixin
from .controller.rows_mixin import RowsMixin
from .controller.visuals_index_mixin import VisualsIndexMixin
from .controller.visuals_rows_mixin import VisualsRowsMixin
from .controller.visuals_sanitize_mixin import VisualsSanitizeMixin
from .controller.buildings_gc_mixin import BuildingsGCMixin
from .controller.visuals_edit_mixin import VisualsEditMixin
from .controller.template_combo_mixin import TemplateComboMixin
from .controller.row_edit_mixin import RowEditMixin
from .controller.persistence_mixin import PersistenceMixin


class InstancePropertiesController(
    LoggingMixin,
    SelectionMixin,
    RenderMixin,
    VisualsPickerMixin,
    ReloadMixin,
    EditorVisibilityMixin,
    RowsMixin,
    VisualsIndexMixin,
    VisualsRowsMixin,
    VisualsSanitizeMixin,
    BuildingsGCMixin,
    VisualsEditMixin,
    TemplateComboMixin,
    RowEditMixin,
    PersistenceMixin,
):
    def __init__(self,
                 model: Optional[InstancePropertiesModel] = None,
                 view: Optional[InstancePropertiesView] = None) -> None:
        self.model = model or InstancePropertiesModel()
        self.view = view or InstancePropertiesView()
        self.events = InstancePropertiesEventHandler()
        # UI helpers
        self._dbl = DoubleClickDetector(interval_ms=450)
        # Text input for main rows (lazy-created in RowEditMixin.begin_edit_row)
        self._text_input = None
        # Cache flattened rows (key, value_str)
        self._rows: List[Tuple[str, str]] = []
        # visuals (Visuals table MVC)
        self.visuals = VisualsController(self)
        # Optional callback for editor to refresh Instances list after persistence
        # Signature: () -> None
        self.on_persist: Optional[callable] = None
        # Optional callback to notify editor about a saved instance with context
        # Signature: (inst: Dict[str, Any], changed_key: Optional[str]) -> None
        self.on_instance_saved: Optional[callable] = None
        # Track last edited dotted key path (e.g., "overrides.building_id")
        self._last_edit_key: Optional[str] = None
        # Cache of building instance id -> template_id (string)
        self._building_index: Dict[int, str] | None = None
        # Cache of valid building template ids
        self._building_template_ids: set[int] | None = None
        self.game = None
        # Visuals Picker orchestrator (lazy)
        self._visuals_picker: VisualsPicker | None = None
        # Toast defaults
        try:
            self._toast_ms = 1600
        except AttributeError:
            pass
        # Strict cleanup policy for visuals
        try:
            self.strict_visuals_cleanup = True
        except AttributeError:
            pass
        # Reduce repeated logs: keep a signature of last visuals we logged
        self._last_visuals_log_sig: tuple | None = None
        # Debounce window to avoid sanitizing right after creating/reusing/assigning
        self._sanitize_block_until_ms: int = 0
        # Logger
        self._log = logging.getLogger(__name__)
        try:
            if not self._log.handlers:
                _h = logging.StreamHandler()
                _h.setLevel(logging.DEBUG)
                _h.setFormatter(logging.Formatter('[%(levelname)s] %(name)s: %(message)s'))
                self._log.addHandler(_h)
            self._log.setLevel(logging.DEBUG)
            # Avoid duplicate logs due to root handlers
            self._log.propagate = False
        except (AttributeError, ValueError):
            pass
        # Log rate-limiting state
        self._log_last: Dict[str, Tuple[int, str]] = {}
        # Throttle post-write GC to avoid tight loops
        self._last_post_write_gc_ms: int = 0

    # --- API -----------------------------------------------------------------
    def set_game(self, game) -> None:
        """Provide access to game (camera/world) for visuals operations."""
        try:
            self.game = game
        except AttributeError:
            self.game = None
        # No need to pass to visuals: it dereferences parent.game dynamically


# --- Orchestrator: delegate method implementations to mixins ---------------
# Logging
InstancePropertiesController._now_ms = LoggingMixin._now_ms
InstancePropertiesController._should_log = LoggingMixin._should_log
InstancePropertiesController._log_info_rl = LoggingMixin._log_info_rl
InstancePropertiesController._log_debug_rl = LoggingMixin._log_debug_rl
InstancePropertiesController._log_warning_rl = LoggingMixin._log_warning_rl

# Selection / rows
InstancePropertiesController.set_instance = SelectionMixin.set_instance
InstancePropertiesController._flatten_instance = RowsMixin._flatten_instance
InstancePropertiesController.get_rows = RowsMixin.get_rows

# Render & events
InstancePropertiesController.render = RenderMixin.render
InstancePropertiesController.handle_event = RenderMixin.handle_event
InstancePropertiesController._show_toast = RenderMixin._show_toast

# Visuals picker orchestration
InstancePropertiesController._on_visuals_picker_selected = VisualsPickerMixin._on_visuals_picker_selected
InstancePropertiesController.open_visuals_picker_for_state = VisualsPickerMixin.open_visuals_picker_for_state
InstancePropertiesController.get_visuals_picker = VisualsPickerMixin.get_visuals_picker
InstancePropertiesController.handle_visuals_picker_event = VisualsPickerMixin.handle_visuals_picker_event
InstancePropertiesController.render_visuals_picker = VisualsPickerMixin.render_visuals_picker

# Reload from disk
InstancePropertiesController._reload_selected_from_json = ReloadMixin._reload_selected_from_json

# Editor/world visibility helpers
InstancePropertiesController._get_world = EditorVisibilityMixin._get_world
InstancePropertiesController._iter_building_entities = EditorVisibilityMixin._iter_building_entities
InstancePropertiesController._find_building_entity_by_id = EditorVisibilityMixin._find_building_entity_by_id
InstancePropertiesController._ensure_building_loaded = EditorVisibilityMixin._ensure_building_loaded
InstancePropertiesController._set_building_visible = EditorVisibilityMixin._set_building_visible
InstancePropertiesController._tag_and_reveal_building = EditorVisibilityMixin._tag_and_reveal_building
InstancePropertiesController.is_visual_building_visible = EditorVisibilityMixin.is_visual_building_visible
InstancePropertiesController.toggle_visual_building_visibility = EditorVisibilityMixin.toggle_visual_building_visibility
InstancePropertiesController._remove_building_entity_by_id = EditorVisibilityMixin._remove_building_entity_by_id

# Visuals index and rows
InstancePropertiesController._ensure_buildings_index = VisualsIndexMixin._ensure_buildings_index
InstancePropertiesController._ensure_building_templates = VisualsIndexMixin._ensure_building_templates
InstancePropertiesController.get_visuals_rows = VisualsRowsMixin.get_visuals_rows
InstancePropertiesController._build_visuals_rows = VisualsRowsMixin._build_visuals_rows

# Visuals sanitize
InstancePropertiesController._sanitize_visuals_instances = VisualsSanitizeMixin._sanitize_visuals_instances

# Buildings GC / persistence helpers
InstancePropertiesController._gc_invalid_building_instances = BuildingsGCMixin._gc_invalid_building_instances
InstancePropertiesController._load_buildings_instances = BuildingsGCMixin._load_buildings_instances
InstancePropertiesController._write_buildings_instances = BuildingsGCMixin._write_buildings_instances
InstancePropertiesController._count_instance_refs_in_visuals = BuildingsGCMixin._count_instance_refs_in_visuals
InstancePropertiesController._find_existing_visual_instance_by_template = BuildingsGCMixin._find_existing_visual_instance_by_template

# Visuals edit API
InstancePropertiesController._parse_int = VisualsEditMixin._parse_int
InstancePropertiesController._validate_template_text = VisualsEditMixin._validate_template_text
InstancePropertiesController.get_visual_input_validation = VisualsEditMixin.get_visual_input_validation
InstancePropertiesController.begin_edit_visual = VisualsEditMixin.begin_edit_visual
InstancePropertiesController.cancel_edit_visual = VisualsEditMixin.cancel_edit_visual
InstancePropertiesController.commit_visual_edit_if_finished = VisualsEditMixin.commit_visual_edit_if_finished
InstancePropertiesController.set_visual_template_via_picker = VisualsEditMixin.set_visual_template_via_picker
InstancePropertiesController.add_building_instance_for_visual = VisualsEditMixin.add_building_instance_for_visual
InstancePropertiesController.clear_visual_for_state = VisualsEditMixin.clear_visual_for_state

# Template combobox
InstancePropertiesController._load_template_options = TemplateComboMixin._load_template_options
InstancePropertiesController.get_template_options = TemplateComboMixin.get_template_options
InstancePropertiesController.get_current_template_index = TemplateComboMixin.get_current_template_index
InstancePropertiesController.select_template_by_index = TemplateComboMixin.select_template_by_index
InstancePropertiesController.set_template_id = TemplateComboMixin.set_template_id

# Row edit
InstancePropertiesController.begin_edit_row = RowEditMixin.begin_edit_row
InstancePropertiesController.is_editing = RowEditMixin.is_editing
InstancePropertiesController.get_text_input = RowEditMixin.get_text_input
InstancePropertiesController.commit_edit_if_finished = RowEditMixin.commit_edit_if_finished

# Persistence
InstancePropertiesController._parse_value = PersistenceMixin._parse_value
InstancePropertiesController._apply_edit = PersistenceMixin._apply_edit
InstancePropertiesController._set_by_path = PersistenceMixin._set_by_path
InstancePropertiesController._persist_instance = PersistenceMixin._persist_instance

__all__ = ["InstancePropertiesController"]
