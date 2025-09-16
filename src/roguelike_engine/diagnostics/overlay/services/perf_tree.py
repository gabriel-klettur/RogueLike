"""
Utilities to build and operate on a hierarchical performance tree for the
Diagnostics Overlay.

Node shape (dict-based for simplicity and JSON-compatibility):
{
    'id': str | None,                # Group id (e.g., '1.2.3' or custom group name)
    'children': dict[str, Node],     # Subgroups keyed by id
    'items': list[tuple[str|None, str, float]],  # (numeric_id | None, label, avg_ms)
    'title': str,                    # Optional display title for group
    'total': float,                  # Sum of avg_ms in subtree
    'count': int                     # Number of items in subtree
}

Public helpers:
- build_perf_tree(perf_log)
- collect_group_ids(node)
- numeric_sort_key(gid)
- is_numeric_id(gid)
- find_sole_item(node)
"""
from __future__ import annotations

from typing import Dict, List, Optional, Tuple
import re


def _parse_numeric_id(key: str) -> Tuple[Optional[str], str]:
    """Extract a leading dotted numeric id and the remainder label.

    Returns (id_str, rest_label). If no dotted numeric prefix exists, id_str is None
    and rest_label is the original key.
    """
    m = re.match(r"^\s*(\d+(?:\.\d+)*)(?:\.)?\s*(.*)$", key)
    if m:
        id_str = m.group(1)
        rest = m.group(2) or ""
        return id_str, rest
    return None, key


def build_perf_tree(perf_log: Dict[str, List[float]]):
    """Build a hierarchical tree from a perf_log mapping.

    perf_log maps a key -> list[seconds per sample]. We average the last ~60 samples
    (if present) and group by the dotted numeric id prefix if available.
    """
    root = {"id": None, "children": {}, "items": [], "title": ""}
    for key, samples in perf_log.items():
        recent = samples[-60:]
        if not recent:
            continue
        avg_ms = (sum(recent) / len(recent)) * 1000.0
        id_str, rest_label = _parse_numeric_id(key)
        if id_str:
            parts = id_str.split(".")
            node = root
            for i in range(1, len(parts) + 1):
                sub_id = ".".join(parts[:i])
                if sub_id not in node["children"]:
                    node["children"][sub_id] = {"id": sub_id, "children": {}, "items": [], "title": ""}
                node = node["children"][sub_id]
            label = rest_label if rest_label else key
            node["items"].append((id_str, label, avg_ms))
            if rest_label:
                if not node["title"] or len(rest_label) < len(node["title"]):
                    node["title"] = rest_label
        else:
            group = key.split(".")[0].strip() or "Other"
            node = root["children"].setdefault(group, {"id": group, "children": {}, "items": [], "title": ""})
            node["items"].append((None, key, avg_ms))

    def _compute(n):
        total = sum(v for _i, _l, v in n["items"])
        count = len(n["items"])
        for child in n["children"].values():
            c_total, c_count = _compute(child)
            total += c_total
            count += c_count
        n["total"] = total
        n["count"] = count
        return total, count

    _compute(root)
    return root


def collect_group_ids(node) -> List[str]:
    """Collect all group ids (keys) recursively from the tree root."""
    ids: List[str] = []
    for gid, child in node.get("children", {}).items():
        ids.append(gid)
        ids.extend(collect_group_ids(child))
    return ids


def numeric_sort_key(gid: str):
    """Sort numeric dotted ids numerically by each component; others go after."""
    if re.match(r"^(\d+(?:\.\d+)*)$", gid):
        return (0, [int(p) for p in gid.split(".")])
    return (1, [gid])


def is_numeric_id(gid: Optional[str]) -> bool:
    return bool(gid and re.match(r"^(\d+(?:\.\d+)*)$", gid))


def find_sole_item(node) -> Optional[Tuple[str, str, float]]:
    """If the subtree has exactly one item, return (deepest_gid, label, avg_ms)."""
    if node.get("count", 0) != 1:
        return None
    # Item directly here
    if len(node.get("items", [])) == 1 and all(c.get("count", 0) == 0 for c in node.get("children", {}).values()):
        item_id, label, val = node["items"][0]
        gid = item_id if is_numeric_id(item_id) else (node.get("id") if is_numeric_id(node.get("id")) else "")
        return (gid or "", label, val)
    # Otherwise the sole item must be in the only child with count==1
    for child in node.get("children", {}).values():
        if child.get("count", 0) == 1:
            res = find_sole_item(child)
            if res:
                return res
    return None
