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
    # Edge selection/hover (index kept for legacy, id for robustness)
    selected_edge_index: Optional[int] = None
    selected_edge_id: Optional[str] = None
    hover_node_id: Optional[str] = None
    hover_edge_index: Optional[int] = None
    hover_edge_id: Optional[str] = None
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
    dragging_edge_id: Optional[str] = None
    dragging_edge_end: Optional[str] = None  # 'from' or 'to'
    dragging_edge_preview_x: Optional[float] = None  # world coordinates
    dragging_edge_preview_y: Optional[float] = None  # world coordinates
    dragging_edge_orig_from: Optional[str] = None
    dragging_edge_orig_to: Optional[str] = None
    selected_set_id: Optional[str] = None
    # Inline text editing state (labels)
    editing_node_id: Optional[str] = None
    editing_edge_index: Optional[int] = None
    editing_edge_id: Optional[str] = None
    editing_text: Optional[str] = None
    # Primary data
    nodes: List[Dict[str, Any]] = field(default_factory=list)   # {id,label,x,y,w,h}
    edges: List[Dict[str, Any]] = field(default_factory=list)   # {id?,from,to,...}
    # Caches (editor-only, not persisted to sets.json)
    node_index_by_id: Dict[str, int] = field(default_factory=dict)
    edge_index_by_id: Dict[str, int] = field(default_factory=dict)
    edge_id_by_index: List[str] = field(default_factory=list)
    adj_out: Dict[str, List[str]] = field(default_factory=dict)  # node_id -> [edge_id]
    adj_in: Dict[str, List[str]] = field(default_factory=dict)   # node_id -> [edge_id]

    def rebuild_caches(self) -> None:
        """Recompute ID/index maps and adjacency from current nodes/edges.
        Assigns ephemeral edge IDs if missing. Editor-only; not persisted.
        """
        # Nodes index
        self.node_index_by_id = {}
        for i, n in enumerate(self.nodes or []):
            nid = n.get('id')
            if isinstance(nid, str) and nid:
                self.node_index_by_id[nid] = i
        # Edges IDs and indices
        self.edge_index_by_id = {}
        self.edge_id_by_index = []
        used: Dict[str, bool] = {}
        for i, e in enumerate(self.edges or []):
            eid = e.get('id')
            if not isinstance(eid, str) or not eid:
                # Fallback ephemeral id based on index; ensure unique within session
                base = f"e{i}"
                eid = base
                k = 1
                while eid in used:
                    eid = f"{base}_{k}"
                    k += 1
                e['id'] = eid
            self.edge_id_by_index.append(eid)
            self.edge_index_by_id[eid] = i
            used[eid] = True
        # Adjacency
        self.adj_out = {}
        self.adj_in = {}
        for i, e in enumerate(self.edges or []):
            fr = e.get('from'); to = e.get('to')
            eid = self.edge_id_by_index[i] if i < len(self.edge_id_by_index) else e.get('id')
            if isinstance(fr, str) and isinstance(to, str) and isinstance(eid, str):
                self.adj_out.setdefault(fr, []).append(eid)
                self.adj_in.setdefault(to, []).append(eid)


__all__ = ["FsmGraphPanelModel"]

