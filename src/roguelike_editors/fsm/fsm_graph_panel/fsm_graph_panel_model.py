from __future__ import annotations
from dataclasses import dataclass, field
from typing import List, Dict, Any, Optional


@dataclass
class FsmGraphPanelModel:
    visible: bool = False
    pan_x: float = 0.0
    pan_y: float = 0.0
    zoom: float = 1.0
    # Graph toolbar state
    graph_toolbar_buttons: List[str] = field(default_factory=lambda: [
        'select', 'add_node', 'clone_node', 'connect', 'disconnect', 'delete', 'mark_ini', 'mark_end', 'zoom_in', 'zoom_out'
    ])
    active_graph_tool: Optional[str] = 'select'
    connect_source_node_id: Optional[str] = None
    selected_node_id: Optional[str] = None
    selected_edge_index: Optional[int] = None
    dragging_node_id: Optional[str] = None
    dragging_pan: bool = False
    drag_last_local_x: int = 0
    drag_last_local_y: int = 0
    drag_offset_x: float = 0.0
    drag_offset_y: float = 0.0
    selected_set_id: Optional[str] = None
    nodes: List[Dict[str, Any]] = field(default_factory=list)   # {id,label,x,y,w,h}
    edges: List[Dict[str, Any]] = field(default_factory=list)   # {from,to}


__all__ = ["FsmGraphPanelModel"]
