from __future__ import annotations

from dataclasses import dataclass, field
from roguelike_editors.spawner.spawner_title.spawner_title_model import SpawnerTitleModel


@dataclass
class SpawnerEditorModel:
    """Minimal model to drive the Spawner Editor.

    - visible: whether the editor is active
    - dragging: whether RMB drag is active
    - dragging_eid: current spawner entity being dragged
    """
    visible: bool = False
    dragging: bool = False
    dragging_eid: int | None = None
    hovered_eid: int | None = None
    # Currently selected spawner entity (for selection ring / actions)
    selected_eid: int | None = None
    # Title submodel for consistent title bar rendering
    title_model: SpawnerTitleModel = field(default_factory=SpawnerTitleModel)
    # Pending zone confirmation overlay data, or None
    # {
    #   'eid': int,
    #   'orig_zone': str,
    #   'proposed_zone': str,
    #   'orig_local': tuple[int,int] | None,
    # }
    pending_zone_confirm: dict | None = None
    # Placement mode (initiated from Templates list "Add" button)
    # When not None: waiting for a map click to place this template as a new instance
    placing_template_id: str | None = None
    # Add mode: set True when user presses Add button and must choose a template from the list
    # This drives UI blinking and input suppression prior to entering placement mode
    add_mode_active: bool = False
    # Hold-to-focus state (when user holds click on coords in Instances panel)
    hold_focus_active: bool = False
    # World pixel target to focus camera while holding (x_px, y_px)
    hold_focus_target_px: tuple[float, float] | None = None
    # Remove mode: when True, LMB selects a spawner to delete (requires confirmation)
    remove_mode_active: bool = False
    # Pending delete confirmation overlay data, or None
    # {
    #   'eid': int,
    #   'template_id': str,
    #   'zone': str,
    #   'local_tile': tuple[int,int],
    # }
    pending_delete_confirm: dict | None = None
    # Resize mode for selected visual building
    resizing_visual: bool = False
    resizing_visual_bid: int | None = None
    resize_origin_mouse: tuple[int, int] | None = None
    resize_start_size: tuple[int, int] | None = None
    # Split ratio dragging for selected visual building
    split_drag_active: bool = False
    split_drag_bid: int | None = None
