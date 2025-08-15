from __future__ import annotations
from dataclasses import dataclass, field
from typing import List, Dict, Any, Optional


@dataclass
class FsmGraphPanelModel:
    visible: bool = False
    pan_x: float = 0.0
    pan_y: float = 0.0
    zoom: float = 1.0
    # UI state
    legend_collapsed: bool = False
    active_graph_tool: Optional[str] = 'select'
    connect_source_node_id: Optional[str] = None
    selected_node_id: Optional[str] = None
    selected_edge_index: Optional[int] = None
    hover_node_id: Optional[str] = None
    hover_edge_index: Optional[int] = None
    # Hovered handle for current hovered edge: 'from' or 'to'
    hover_edge_handle_end: Optional[str] = None
    dragging_node_id: Optional[str] = None
    dragging_pan: bool = False
    drag_last_local_x: int = 0
    drag_last_local_y: int = 0
    drag_offset_x: float = 0.0
    drag_offset_y: float = 0.0
    # Edge handle dragging state
    dragging_edge_index: Optional[int] = None
    dragging_edge_end: Optional[str] = None  # 'from' or 'to'
    dragging_edge_preview_x: Optional[float] = None  # world coordinates
    dragging_edge_preview_y: Optional[float] = None  # world coordinates
    dragging_edge_orig_from: Optional[str] = None
    dragging_edge_orig_to: Optional[str] = None
    selected_set_id: Optional[str] = None
    # Inline text editing state (labels)
    editing_node_id: Optional[str] = None
    editing_edge_index: Optional[int] = None
    editing_text: Optional[str] = None
    nodes: List[Dict[str, Any]] = field(default_factory=list)   # {id,label,x,y,w,h}
    edges: List[Dict[str, Any]] = field(default_factory=list)   # {from,to}


__all__ = ["FsmGraphPanelModel"]

